using DispatchCore.Core.Models;

namespace DispatchCore.Core.Interfaces;

public interface IJobHandler
{
    string JobType { get; }
    Task HandleAsync(Job job, CancellationToken ct);
}
