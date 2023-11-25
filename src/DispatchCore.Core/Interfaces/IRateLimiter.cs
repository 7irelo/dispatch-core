namespace DispatchCore.Core.Interfaces;

public interface IRateLimiter
{
    Task<bool> TryConsumeAsync(string tenantId, CancellationToken ct = default);
}
