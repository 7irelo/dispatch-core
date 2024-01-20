using DispatchCore.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace DispatchCore.Worker;

public sealed class LockReaperService : BackgroundService
{
    private readonly IJobRepository _jobRepo;
    private readonly ILogger<LockReaperService> _logger;
    private readonly WorkerOptions _options;

    public LockReaperService(
        IJobRepository jobRepo,
        IOptions<WorkerOptions> options,
        ILogger<LockReaperService> logger)
    {
        _jobRepo = jobRepo;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lock reaper started. Interval={Interval}ms", _options.ReaperIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var resetCount = await _jobRepo.ResetExpiredLocksAsync(TimeSpan.FromMinutes(5), stoppingToken);
                if (resetCount > 0)
                {
                    _logger.LogWarning("Reaper reset {Count} expired locks", resetCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during lock reaping");
            }

            await Task.Delay(_options.ReaperIntervalMs, stoppingToken);
        }
    }
}
