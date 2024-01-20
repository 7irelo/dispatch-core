using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using DispatchCore.Executor;
using Microsoft.Extensions.Options;

namespace DispatchCore.Worker;

public sealed class JobPollerService : BackgroundService
{
    private readonly IJobRepository _jobRepo;
    private readonly JobChannel _channel;
    private readonly ILogger<JobPollerService> _logger;
    private readonly WorkerOptions _options;

    public JobPollerService(
        IJobRepository jobRepo,
        JobChannel channel,
        IOptions<WorkerOptions> options,
        ILogger<JobPollerService> logger)
    {
        _jobRepo = jobRepo;
        _channel = channel;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var partitionKey = _options.PartitionKey
            ?? Environment.GetEnvironmentVariable("WORKER_PARTITION_KEY");

        _logger.LogInformation("Job poller started. PartitionKey={PartitionKey}, PollInterval={Interval}ms",
            partitionKey ?? "(all)", _options.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await _jobRepo.PollDueJobsAsync(_options.BatchSize, partitionKey, stoppingToken);

                if (jobs.Count > 0)
                {
                    _logger.LogInformation("Polled {Count} jobs", jobs.Count);
                }

                foreach (var job in jobs)
                {
                    var envelope = new JobEnvelope(job, stoppingToken);
                    await _channel.Writer.WriteAsync(envelope, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during job polling");
            }

            await Task.Delay(_options.PollIntervalMs, stoppingToken);
        }

        _channel.Writer.Complete();
    }
}
