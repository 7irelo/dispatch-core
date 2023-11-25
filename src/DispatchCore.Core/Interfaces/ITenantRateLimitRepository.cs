using DispatchCore.Core.Models;

namespace DispatchCore.Core.Interfaces;

public interface ITenantRateLimitRepository
{
    Task<TenantRateLimitConfig?> GetAsync(string tenantId, CancellationToken ct = default);
    Task<int> GetMaxPerMinuteAsync(string tenantId, CancellationToken ct = default);
}
