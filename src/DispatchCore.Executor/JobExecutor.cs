using System.Threading.Channels;
using DispatchCore.Contracts;
using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using DispatchCore.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace DispatchCore.Executor;

public sealed class JobExecutor
{
    private readonly IJobRepository _jobRepo;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IRateLimiter _rateLimiter;
    private readonly JobHandlerRegistry _handlerRegistry;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IJobRepository jobRepo,
        IDistributedLockProvider lockProvider,
        IRateLimiter rateLimiter,
        JobHandlerRegistry handlerRegistry,
        ILogger<JobExecutor> logger)
    {
        _jobRepo = jobRepo;
        _lockProvider = lockProvider;
        _rateLimiter = rateLimiter;
        _handlerRegistry = handlerRegistry;
        _logger = logger;
    }

    public async Task ExecuteAsync(JobEnvelope envelope)
    {
        var job = envelope.Job;
        var ct = envelope.CancellationToken;

        // Rate limit check - reschedule without incrementing attempts if exceeded
        if (!await _rateLimiter.TryConsumeAsync(job.TenantId, ct))
        {
            _logger.LogWarning("Rate limit exceeded for tenant {TenantId}, rescheduling job {JobId}",
                job.TenantId, job.JobId);

            job.Status = JobStatus.Pending;
            job.RunAt = DateTimeOffset.UtcNow.AddSeconds(10);
            job.LockedBy = null;
            job.LockUntil = null;
            await _jobRepo.UpdateAsync(job, ct);
            return;
        }

        // Best-effort Redis lock
        await using var redisLock = await _lockProvider.AcquireAsync(
            $"job:{job.JobId}", TimeSpan.FromMinutes(5), ct);

        if (!redisLock.IsAcquired)
        {
            _logger.LogWarning("Could not acquire Redis lock for job {JobId}, skipping", job.JobId);
            return;
        }

        var handler = _handlerRegistry.GetHandler(job.Type);
        if (handler is null)
        {
            _logger.LogError("No handler registered for job type {JobType}", job.Type);
            job.Status = JobStatus.Failed;
            job.LastError = $"No handler registered for job type '{job.Type}'";
            job.LockedBy = null;
            job.LockUntil = null;
            await _jobRepo.UpdateAsync(job, ct);
            return;
        }

        try
        {
            job.Attempts++;
            await handler.HandleAsync(job, ct);

            job.Status = JobStatus.Succeeded;
            job.LockedBy = null;
            job.LockUntil = null;
            await _jobRepo.UpdateAsync(job, ct);

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed on attempt {Attempt}", job.JobId, job.Attempts);

            job.LastError = ex.Message;
            job.LockedBy = null;
            job.LockUntil = null;

            if (RetryPolicy.ShouldDeadLetter(job.Attempts, job.MaxAttempts))
            {
                job.Status = JobStatus.DeadLetter;
                _logger.LogWarning("Job {JobId} moved to dead letter after {Attempts} attempts",
                    job.JobId, job.Attempts);
            }
            else
            {
                job.Status = JobStatus.Pending;
                job.RunAt = RetryPolicy.NextRunAt(job.Attempts);
                _logger.LogInformation("Job {JobId} scheduled for retry at {RunAt}", job.JobId, job.RunAt);
            }

            await _jobRepo.UpdateAsync(job, ct);
        }
    }
}
