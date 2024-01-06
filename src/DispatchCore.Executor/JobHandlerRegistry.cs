using DispatchCore.Core.Interfaces;

namespace DispatchCore.Executor;

public sealed class JobHandlerRegistry
{
    private readonly Dictionary<string, IJobHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IJobHandler handler)
    {
        _handlers[handler.JobType] = handler;
    }

    public IJobHandler? GetHandler(string jobType)
    {
        _handlers.TryGetValue(jobType, out var handler);
        return handler;
    }

    public IReadOnlyCollection<string> RegisteredTypes => _handlers.Keys;
}
