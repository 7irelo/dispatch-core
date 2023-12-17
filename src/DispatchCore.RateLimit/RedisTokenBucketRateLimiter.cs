using DispatchCore.Core.Interfaces;
using StackExchange.Redis;

namespace DispatchCore.RateLimit;

public sealed class RedisTokenBucketRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ITenantRateLimitRepository _configRepo;

    // Lua script implements token bucket algorithm
    private const string TokenBucketScript = """
        local key = KEYS[1]
        local max_tokens = tonumber(ARGV[1])
        local refill_rate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local ttl = tonumber(ARGV[4])

        local bucket = redis.call('HMGET', key, 'tokens', 'last_refill')
        local tokens = tonumber(bucket[1])
        local last_refill = tonumber(bucket[2])

        if tokens == nil then
            tokens = max_tokens
            last_refill = now
        end

        local elapsed = now - last_refill
        local new_tokens = tokens + (elapsed * refill_rate)
        if new_tokens > max_tokens then
            new_tokens = max_tokens
        end

        if new_tokens >= 1 then
            new_tokens = new_tokens - 1
            redis.call('HMSET', key, 'tokens', new_tokens, 'last_refill', now)
            redis.call('EXPIRE', key, ttl)
            return 1
        else
            redis.call('HMSET', key, 'tokens', new_tokens, 'last_refill', now)
            redis.call('EXPIRE', key, ttl)
            return 0
        end
        """;

    public RedisTokenBucketRateLimiter(IConnectionMultiplexer redis, ITenantRateLimitRepository configRepo)
    {
        _redis = redis;
        _configRepo = configRepo;
    }

    public async Task<bool> TryConsumeAsync(string tenantId, CancellationToken ct = default)
    {
        var maxPerMinute = await _configRepo.GetMaxPerMinuteAsync(tenantId, ct);
        var db = _redis.GetDatabase();
        var key = $"dispatch:ratelimit:{tenantId}";
        var refillRate = maxPerMinute / 60.0; // tokens per second
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var ttl = 120; // 2 minute TTL for the bucket key

        var result = await db.ScriptEvaluateAsync(TokenBucketScript,
            new RedisKey[] { key },
            new RedisValue[] { maxPerMinute, refillRate, now, ttl });

        return (int)result == 1;
    }
}
