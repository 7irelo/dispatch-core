using DispatchCore.Core.Interfaces;
using DispatchCore.RateLimit;
using FluentAssertions;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace DispatchCore.Tests.Integration;

public class RedisRateLimiterTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();
    private IConnectionMultiplexer _mux = null!;
    private RedisTokenBucketRateLimiter _limiter = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());

        var configRepo = Substitute.For<ITenantRateLimitRepository>();
        configRepo.GetMaxPerMinuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(5); // 5 per minute for testing

        _limiter = new RedisTokenBucketRateLimiter(_mux, configRepo);
    }

    public async Task DisposeAsync()
    {
        _mux.Dispose();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task TryConsume_UnderLimit_Succeeds()
    {
        var result = await _limiter.TryConsumeAsync("test-tenant");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryConsume_ExceedingLimit_ReturnsFalse()
    {
        // Consume all 5 tokens
        for (int i = 0; i < 5; i++)
        {
            var ok = await _limiter.TryConsumeAsync("exhaust-tenant");
            ok.Should().BeTrue($"token {i + 1} should be available");
        }

        // 6th should fail
        var exceeded = await _limiter.TryConsumeAsync("exhaust-tenant");
        exceeded.Should().BeFalse();
    }
}
