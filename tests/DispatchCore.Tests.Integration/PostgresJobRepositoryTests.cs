using Dapper;
using DispatchCore.Contracts;
using DispatchCore.Core.Models;
using DispatchCore.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DispatchCore.Tests.Integration;

public class PostgresJobRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("dispatch_core")
        .WithUsername("dispatch")
        .WithPassword("dispatch_secret")
        .Build();

    private PostgresJobRepository _repo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var connStr = _postgres.GetConnectionString();
        _repo = new PostgresJobRepository(connStr);

        // Apply migrations
        var migrationsPath = FindMigrationsPath();
        var runner = new MigrationRunner(connStr, migrationsPath, NullLogger<MigrationRunner>.Instance);
        await runner.RunAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private static string FindMigrationsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "migrations")))
        {
            dir = dir.Parent;
        }
        return dir is not null
            ? Path.Combine(dir.FullName, "migrations")
            : throw new DirectoryNotFoundException("Cannot find migrations directory");
    }

    [Fact]
    public async Task CreateAndGetJob_RoundTrips()
    {
        var job = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send",
            Payload = "{\"to\":\"test@example.com\"}",
            MaxAttempts = 5
        };

        var created = await _repo.CreateAsync(job);
        created.JobId.Should().Be(job.JobId);

        var fetched = await _repo.GetByIdAsync(job.JobId);
        fetched.Should().NotBeNull();
        fetched!.TenantId.Should().Be("tenant-1");
        fetched.Type.Should().Be("email.send");
        fetched.MaxAttempts.Should().Be(5);
    }

    [Fact]
    public async Task IdempotencyKey_ReturnsExistingJob()
    {
        var job = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send",
            IdempotencyKey = "unique-key-1"
        };

        await _repo.CreateAsync(job);

        var existing = await _repo.FindByIdempotencyKeyAsync("tenant-1", "unique-key-1");
        existing.Should().NotBeNull();
        existing!.JobId.Should().Be(job.JobId);
    }

    [Fact]
    public async Task IdempotencyKey_DifferentTenant_AllowsSameKey()
    {
        var job1 = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-A",
            Type = "email.send",
            IdempotencyKey = "shared-key"
        };
        var job2 = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-B",
            Type = "email.send",
            IdempotencyKey = "shared-key"
        };

        await _repo.CreateAsync(job1);
        await _repo.CreateAsync(job2);

        var a = await _repo.FindByIdempotencyKeyAsync("tenant-A", "shared-key");
        var b = await _repo.FindByIdempotencyKeyAsync("tenant-B", "shared-key");
        a!.JobId.Should().Be(job1.JobId);
        b!.JobId.Should().Be(job2.JobId);
    }

    [Fact]
    public async Task PollDueJobs_ClaimsAtomically()
    {
        for (int i = 0; i < 5; i++)
        {
            await _repo.CreateAsync(new Job
            {
                JobId = Guid.NewGuid(),
                TenantId = "tenant-1",
                Type = "email.send",
                Status = JobStatus.Pending,
                RunAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        }

        var polled = await _repo.PollDueJobsAsync(3);
        polled.Should().HaveCount(3);
        polled.Should().AllSatisfy(j => j.Status.Should().Be(JobStatus.Running));
        polled.Should().AllSatisfy(j => j.LockedBy.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task PollDueJobs_WithPartitionKey_FiltersCorrectly()
    {
        await _repo.CreateAsync(new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send",
            PartitionKey = "shard-A",
            RunAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await _repo.CreateAsync(new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send",
            PartitionKey = "shard-B",
            RunAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        var polled = await _repo.PollDueJobsAsync(10, "shard-A");
        polled.Should().HaveCount(1);
        polled[0].PartitionKey.Should().Be("shard-A");
    }

    [Fact]
    public async Task GetMetrics_ReturnsCorrectCounts()
    {
        await _repo.CreateAsync(new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "t-metrics",
            Type = "test",
            Status = JobStatus.Pending
        });

        var metrics = await _repo.GetMetricsAsync();
        metrics.TotalJobs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ResetExpiredLocks_ResetsStaleJobs()
    {
        var job = new Job
        {
            JobId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Type = "email.send",
            Status = JobStatus.Running,
            LockedBy = "dead-worker",
            LockUntil = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        await _repo.CreateAsync(job);

        var reset = await _repo.ResetExpiredLocksAsync(TimeSpan.FromMinutes(5));
        reset.Should().BeGreaterThan(0);

        var updated = await _repo.GetByIdAsync(job.JobId);
        updated!.Status.Should().Be(JobStatus.Pending);
        updated.LockedBy.Should().BeNull();
    }
}
