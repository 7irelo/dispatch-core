using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using Microsoft.Extensions.Logging;

namespace DispatchCore.Executor.Handlers;

public sealed class EmailSendHandler : IJobHandler
{
    private readonly ILogger<EmailSendHandler> _logger;

    public string JobType => "email.send";

    public EmailSendHandler(ILogger<EmailSendHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(Job job, CancellationToken ct)
    {
        _logger.LogInformation("Sending email for job {JobId}, tenant {TenantId}", job.JobId, job.TenantId);
        // Simulate email sending
        await Task.Delay(500, ct);
        _logger.LogInformation("Email sent successfully for job {JobId}", job.JobId);
    }
}
