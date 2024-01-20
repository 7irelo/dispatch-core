using DispatchCore.Executor;
using Microsoft.Extensions.Options;

namespace DispatchCore.Worker;

public sealed class JobConsumerService : BackgroundService
{
    private readonly JobChannel _channel;
    private readonly JobExecutor _executor;
    private readonly ILogger<JobConsumerService> _logger;
    private readonly WorkerOptions _options;

    public JobConsumerService(
        JobChannel channel,
        JobExecutor executor,
        IOptions<WorkerOptions> options,
        ILogger<JobConsumerService> logger)
    {
        _channel = channel;
        _executor = executor;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job consumer started with concurrency={Concurrency}", _options.Concurrency);

        using var semaphore = new SemaphoreSlim(_options.Concurrency);
        var tasks = new List<Task>();

        await foreach (var envelope in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await semaphore.WaitAsync(stoppingToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    await _executor.ExecuteAsync(envelope);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error executing job {JobId}", envelope.Job.JobId);
                }
                finally
                {
                    semaphore.Release();
                }
            }, stoppingToken);

            tasks.Add(task);
            tasks.RemoveAll(t => t.IsCompleted);
        }

        await Task.WhenAll(tasks);
    }
}
