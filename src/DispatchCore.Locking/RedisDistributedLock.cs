using DispatchCore.Core.Interfaces;
using StackExchange.Redis;

namespace DispatchCore.Locking;

public sealed class RedisDistributedLock : IDistributedLock
{
    private readonly IDatabase _db;
    private readonly string _lockValue;

    public string Resource { get; }
    public bool IsAcquired { get; }

    internal RedisDistributedLock(IDatabase db, string resource, string lockValue, bool isAcquired)
    {
        _db = db;
        _lockValue = lockValue;
        Resource = resource;
        IsAcquired = isAcquired;
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsAcquired) return;

        // Release only if we still own the lock (compare-and-delete via Lua)
        const string script = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end
            """;

        try
        {
            await _db.ScriptEvaluateAsync(script,
                new RedisKey[] { Resource },
                new RedisValue[] { _lockValue });
        }
        catch
        {
            // Best-effort release
        }
    }
}

public sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    private readonly IConnectionMultiplexer _redis;

    public RedisDistributedLockProvider(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<IDistributedLock> AcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var lockKey = $"dispatch:lock:{resource}";
        var lockValue = Guid.NewGuid().ToString("N");

        var acquired = await db.StringSetAsync(lockKey, lockValue, expiry, When.NotExists);
        return new RedisDistributedLock(db, lockKey, lockValue, acquired);
    }
}
