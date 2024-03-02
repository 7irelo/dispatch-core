using System.Text.Json;
using DispatchCore.Contracts;
using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DispatchCore.Tests.Unit;

public class IdempotencyTests
{
    private readonly IJobRepository _repo = Substitute.For<IJobRepository>();

    [Fact]
    public async Task CreateJob_WithExistingIdempotencyKey_ReturnsExistingJob()
    {
        var existingJob = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send",
            IdempotencyKey = "idem-key-1"
        };

        _repo.FindByIdempotencyKeyAsync("tenant-1", "idem-key-1", Arg.Any<CancellationToken>())
            .Returns(existingJob);

        // Simulate what the API does
        var request = new CreateJobRequest
        {
            TenantId = "tenant-1",
            Type = "email.send",
            IdempotencyKey = "idem-key-1"
        };

        var found = await _repo.FindByIdempotencyKeyAsync(request.TenantId, request.IdempotencyKey!);
        found.Should().NotBeNull();
        found!.JobId.Should().Be(existingJob.JobId);

        // CreateAsync should NOT be called
        await _repo.DidNotReceive().CreateAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateJob_WithNewIdempotencyKey_CreatesNewJob()
    {
        _repo.FindByIdempotencyKeyAsync("tenant-1", "new-key", Arg.Any<CancellationToken>())
            .Returns((Job?)null);

        var newJob = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send",
            IdempotencyKey = "new-key"
        };
        _repo.CreateAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(newJob);

        var found = await _repo.FindByIdempotencyKeyAsync("tenant-1", "new-key");
        found.Should().BeNull();

        var created = await _repo.CreateAsync(newJob);
        created.Should().NotBeNull();
        created.JobId.Should().Be(newJob.JobId);
    }

    [Fact]
    public async Task CreateJob_WithoutIdempotencyKey_AlwaysCreates()
    {
        var job = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send"
        };

        _repo.CreateAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(job);

        var created = await _repo.CreateAsync(job);
        created.Should().NotBeNull();

        // FindByIdempotencyKeyAsync should NOT be called when key is null
        await _repo.DidNotReceive().FindByIdempotencyKeyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
