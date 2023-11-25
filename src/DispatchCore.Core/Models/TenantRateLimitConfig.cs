namespace DispatchCore.Core.Models;

public sealed class TenantRateLimitConfig
{
    public string TenantId { get; set; } = default!;
    public int MaxPerMinute { get; set; } = 10;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
