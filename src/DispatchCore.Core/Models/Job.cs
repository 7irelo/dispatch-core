using DispatchCore.Contracts;

namespace DispatchCore.Core.Models;

public sealed class Job
{
    public Guid JobId { get; set; }
    public string TenantId { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Payload { get; set; } = "{}";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockUntil { get; set; }
    public string? PartitionKey { get; set; }
    public string? IdempotencyKey { get; set; }
}
