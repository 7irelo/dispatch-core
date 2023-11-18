using System.Text.Json;

namespace DispatchCore.Contracts;

public sealed record CreateJobRequest
{
    public required string TenantId { get; init; }
    public required string Type { get; init; }
    public JsonElement? Payload { get; init; }
    public DateTimeOffset? RunAt { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public string? PartitionKey { get; init; }
    public string? IdempotencyKey { get; init; }
}
