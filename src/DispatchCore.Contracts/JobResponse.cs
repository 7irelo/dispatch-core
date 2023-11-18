using System.Text.Json;

namespace DispatchCore.Contracts;

public sealed record JobResponse
{
    public Guid JobId { get; init; }
    public string TenantId { get; init; } = default!;
    public string Type { get; init; } = default!;
    public JsonElement? Payload { get; init; }
    public JobStatus Status { get; init; }
    public DateTimeOffset RunAt { get; init; }
    public int Attempts { get; init; }
    public int MaxAttempts { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? LockedBy { get; init; }
    public string? PartitionKey { get; init; }
    public string? IdempotencyKey { get; init; }
}
