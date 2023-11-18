namespace DispatchCore.Contracts;

public sealed record MetricsResponse
{
    public long TotalJobs { get; init; }
    public long PendingJobs { get; init; }
    public long ScheduledJobs { get; init; }
    public long RunningJobs { get; init; }
    public long SucceededJobs { get; init; }
    public long FailedJobs { get; init; }
    public long DeadLetterJobs { get; init; }
}
