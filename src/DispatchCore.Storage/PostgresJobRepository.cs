using Dapper;
using DispatchCore.Contracts;
using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using Npgsql;

namespace DispatchCore.Storage;

public sealed class PostgresJobRepository : IJobRepository
{
    private readonly string _connectionString;

    public PostgresJobRepository(string connectionString)
    {
        _connectionString = connectionString;
        DapperConfig.Initialize();
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<Job> CreateAsync(Job job, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO jobs (job_id, tenant_id, type, payload, status, run_at, attempts, max_attempts,
                              last_error, created_at, updated_at, locked_by, lock_until, partition_key, idempotency_key)
            VALUES (@JobId, @TenantId, @Type, @Payload::jsonb, @Status::job_status, @RunAt, @Attempts, @MaxAttempts,
                    @LastError, @CreatedAt, @UpdatedAt, @LockedBy, @LockUntil, @PartitionKey, @IdempotencyKey)
            RETURNING *;
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var result = await conn.QuerySingleAsync<Job>(sql, job);
        return result;
    }

    public async Task<Job?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM jobs WHERE job_id = @JobId";
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Job>(sql, new { JobId = jobId });
    }

    public async Task<Job?> FindByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM jobs WHERE tenant_id = @TenantId AND idempotency_key = @IdempotencyKey";
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Job>(sql, new { TenantId = tenantId, IdempotencyKey = idempotencyKey });
    }

    public async Task<IReadOnlyList<Job>> GetByTenantAsync(string tenantId, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM jobs WHERE tenant_id = @TenantId ORDER BY created_at DESC LIMIT @Limit OFFSET @Offset";
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var results = await conn.QueryAsync<Job>(sql, new { TenantId = tenantId, Limit = limit, Offset = offset });
        return results.ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<Job>> PollDueJobsAsync(int batchSize, string? partitionKey = null, CancellationToken ct = default)
    {
        var partitionFilter = partitionKey is not null
            ? "AND partition_key = @PartitionKey"
            : "";

        var sql = $"""
            UPDATE jobs
            SET status = 'Running'::job_status,
                locked_by = @LockedBy,
                lock_until = @LockUntil,
                updated_at = now()
            WHERE job_id IN (
                SELECT job_id FROM jobs
                WHERE status IN ('Pending'::job_status, 'Scheduled'::job_status)
                  AND run_at <= now()
                  {partitionFilter}
                ORDER BY run_at
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED
            )
            RETURNING *;
            """;

        var workerId = $"worker-{Environment.MachineName}-{Environment.ProcessId}";

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var results = await conn.QueryAsync<Job>(sql, new
        {
            BatchSize = batchSize,
            LockedBy = workerId,
            LockUntil = DateTimeOffset.UtcNow.AddMinutes(5),
            PartitionKey = partitionKey
        });
        return results.ToList().AsReadOnly();
    }

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE jobs SET
                status = @Status::job_status,
                run_at = @RunAt,
                attempts = @Attempts,
                max_attempts = @MaxAttempts,
                last_error = @LastError,
                updated_at = now(),
                locked_by = @LockedBy,
                lock_until = @LockUntil
            WHERE job_id = @JobId;
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, job);
    }

    public async Task<int> ResetExpiredLocksAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE jobs
            SET status = 'Pending'::job_status,
                locked_by = NULL,
                lock_until = NULL,
                updated_at = now()
            WHERE status = 'Running'::job_status
              AND lock_until < now()
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.ExecuteAsync(sql);
    }

    public async Task<MetricsResponse> GetMetricsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                COUNT(*) AS TotalJobs,
                COUNT(*) FILTER (WHERE status = 'Pending') AS PendingJobs,
                COUNT(*) FILTER (WHERE status = 'Scheduled') AS ScheduledJobs,
                COUNT(*) FILTER (WHERE status = 'Running') AS RunningJobs,
                COUNT(*) FILTER (WHERE status = 'Succeeded') AS SucceededJobs,
                COUNT(*) FILTER (WHERE status = 'Failed') AS FailedJobs,
                COUNT(*) FILTER (WHERE status = 'DeadLetter') AS DeadLetterJobs
            FROM jobs;
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<MetricsResponse>(sql);
    }
}
