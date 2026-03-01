using DispatchCore.Contracts;
using DispatchCore.Core.Models;

namespace DispatchCore.Core.Interfaces;

public interface IJobRepository
{
    Task<Job> CreateAsync(Job job, CancellationToken ct = default);
    Task<Job?> GetByIdAsync(Guid jobId, CancellationToken ct = default);
    Task<Job?> FindByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetByTenantAsync(string tenantId, int limit = 50, int offset = 0, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> PollDueJobsAsync(int batchSize, string? partitionKey = null, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
    Task<int> ResetExpiredLocksAsync(TimeSpan lockTimeout, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetRecentJobsAsync(int limit = 25, CancellationToken ct = default);
    Task<MetricsResponse> GetMetricsAsync(CancellationToken ct = default);
}
