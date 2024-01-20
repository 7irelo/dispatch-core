using DispatchCore.Storage;

namespace DispatchCore.Worker;

public sealed class MigrationHostedService : IHostedService
{
    private readonly MigrationRunner _runner;

    public MigrationHostedService(MigrationRunner runner)
    {
        _runner = runner;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _runner.RunAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
