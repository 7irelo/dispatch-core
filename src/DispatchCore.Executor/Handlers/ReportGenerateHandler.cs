using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using Microsoft.Extensions.Logging;

namespace DispatchCore.Executor.Handlers;

public sealed class ReportGenerateHandler : IJobHandler
{
    private readonly ILogger<ReportGenerateHandler> _logger;

    public string JobType => "report.generate";

    public ReportGenerateHandler(ILogger<ReportGenerateHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(Job job, CancellationToken ct)
    {
        _logger.LogInformation("Generating report for job {JobId}, tenant {TenantId}", job.JobId, job.TenantId);
        // Simulate report generation
        await Task.Delay(2000, ct);
        _logger.LogInformation("Report generated successfully for job {JobId}", job.JobId);
    }
}
