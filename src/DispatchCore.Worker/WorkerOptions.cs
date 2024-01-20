namespace DispatchCore.Worker;

public sealed class WorkerOptions
{
    public int PollIntervalMs { get; set; } = 1000;
    public int BatchSize { get; set; } = 10;
    public int Concurrency { get; set; } = 5;
    public int ReaperIntervalMs { get; set; } = 30000;
    public string? PartitionKey { get; set; }
}
