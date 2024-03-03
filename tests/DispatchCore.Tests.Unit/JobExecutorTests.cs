using DispatchCore.Contracts;
using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using DispatchCore.Executor;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DispatchCore.Tests.Unit;

public class JobExecutorTests
{
    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly IDistributedLockProvider _lockProvider = Substitute.For<IDistributedLockProvider>();
    private readonly IRateLimiter _rateLimiter = Substitute.For<IRateLimiter>();
    private readonly JobHandlerRegistry _registry = new();
    private readonly JobExecutor _executor;

    public JobExecutorTests()
    {
        _rateLimiter.TryConsumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var mockLock = Substitute.For<IDistributedLock>();
        mockLock.IsAcquired.Returns(true);
        _lockProvider.AcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(mockLock);

        _executor = new JobExecutor(
            _jobRepo, _lockProvider, _rateLimiter, _registry,
            Substitute.For<ILogger<JobExecutor>>());
    }

    [Fact]
    public async Task Execute_RateLimitExceeded_ReschedulesWithoutIncrementingAttempts()
    {
        _rateLimiter.TryConsumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var job = new Job { JobId = Guid.NewGuid(), TenantId = "t1", Type = "email.send", Attempts = 0 };
        var envelope = new JobEnvelope(job, CancellationToken.None);

        await _executor.ExecuteAsync(envelope);

        job.Status.Should().Be(JobStatus.Pending);
        job.Attempts.Should().Be(0); // NOT incremented
        await _jobRepo.Received(1).UpdateAsync(Arg.Is<Job>(j => j.Attempts == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NoHandler_MarksJobFailed()
    {
        var job = new Job { JobId = Guid.NewGuid(), TenantId = "t1", Type = "unknown.type" };
        var envelope = new JobEnvelope(job, CancellationToken.None);

        await _executor.ExecuteAsync(envelope);

        job.Status.Should().Be(JobStatus.Failed);
        job.LastError.Should().Contain("No handler registered");
    }

    [Fact]
    public async Task Execute_HandlerSucceeds_MarksSucceeded()
    {
        var handler = Substitute.For<IJobHandler>();
        handler.JobType.Returns("test.job");
        _registry.Register(handler);

        var job = new Job { JobId = Guid.NewGuid(), TenantId = "t1", Type = "test.job", MaxAttempts = 3 };
        var envelope = new JobEnvelope(job, CancellationToken.None);

        await _executor.ExecuteAsync(envelope);

        job.Status.Should().Be(JobStatus.Succeeded);
        job.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Execute_HandlerFails_RetriesOrDeadLetters()
    {
        var handler = Substitute.For<IJobHandler>();
        handler.JobType.Returns("test.job");
        handler.HandleAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));
        _registry.Register(handler);

        // Attempt 2 of 3 -> should retry
        var job = new Job { JobId = Guid.NewGuid(), TenantId = "t1", Type = "test.job", Attempts = 1, MaxAttempts = 3 };
        var envelope = new JobEnvelope(job, CancellationToken.None);

        await _executor.ExecuteAsync(envelope);

        job.Status.Should().Be(JobStatus.Pending);
        job.Attempts.Should().Be(2);
        job.LastError.Should().Be("boom");
    }

    [Fact]
    public async Task Execute_HandlerFails_MaxAttemptsReached_DeadLetters()
    {
        var handler = Substitute.For<IJobHandler>();
        handler.JobType.Returns("test.job");
        handler.HandleAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("final failure"));
        _registry.Register(handler);

        var job = new Job { JobId = Guid.NewGuid(), TenantId = "t1", Type = "test.job", Attempts = 2, MaxAttempts = 3 };
        var envelope = new JobEnvelope(job, CancellationToken.None);

        await _executor.ExecuteAsync(envelope);

        job.Status.Should().Be(JobStatus.DeadLetter);
        job.Attempts.Should().Be(3);
    }
}
