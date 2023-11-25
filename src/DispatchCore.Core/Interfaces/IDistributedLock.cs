namespace DispatchCore.Core.Interfaces;

public interface IDistributedLock : IAsyncDisposable
{
    string Resource { get; }
    bool IsAcquired { get; }
}

public interface IDistributedLockProvider
{
    Task<IDistributedLock> AcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default);
}
