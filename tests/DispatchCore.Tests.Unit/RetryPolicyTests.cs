using DispatchCore.Core.Scheduling;
using FluentAssertions;
using Xunit;

namespace DispatchCore.Tests.Unit;

public class RetryPolicyTests
{
    [Fact]
    public void CalculateDelay_FirstAttempt_ReturnsBaseDelay()
    {
        var delay = RetryPolicy.CalculateDelay(1, TimeSpan.FromSeconds(2));

        // Base is 2s * 2^0 = 2s, plus up to 30% jitter
        delay.TotalSeconds.Should().BeInRange(2.0, 2.6);
    }

    [Fact]
    public void CalculateDelay_SecondAttempt_ReturnsExponentialDelay()
    {
        var delay = RetryPolicy.CalculateDelay(2, TimeSpan.FromSeconds(2));

        // Base is 2s * 2^1 = 4s, plus up to 30% jitter
        delay.TotalSeconds.Should().BeInRange(4.0, 5.2);
    }

    [Fact]
    public void CalculateDelay_ThirdAttempt_ReturnsLargerDelay()
    {
        var delay = RetryPolicy.CalculateDelay(3, TimeSpan.FromSeconds(2));

        // Base is 2s * 2^2 = 8s, plus up to 30% jitter
        delay.TotalSeconds.Should().BeInRange(8.0, 10.4);
    }

    [Fact]
    public void ShouldDeadLetter_WhenAttemptsReachMax_ReturnsTrue()
    {
        RetryPolicy.ShouldDeadLetter(3, 3).Should().BeTrue();
    }

    [Fact]
    public void ShouldDeadLetter_WhenAttemptsExceedMax_ReturnsTrue()
    {
        RetryPolicy.ShouldDeadLetter(5, 3).Should().BeTrue();
    }

    [Fact]
    public void ShouldDeadLetter_WhenAttemptsBelowMax_ReturnsFalse()
    {
        RetryPolicy.ShouldDeadLetter(1, 3).Should().BeFalse();
    }

    [Fact]
    public void NextRunAt_ReturnsTimeInFuture()
    {
        var before = DateTimeOffset.UtcNow;
        var nextRun = RetryPolicy.NextRunAt(1);
        nextRun.Should().BeAfter(before);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CalculateDelay_IncrementsExponentially(int attempt)
    {
        var delay = RetryPolicy.CalculateDelay(attempt, TimeSpan.FromSeconds(1));
        var expectedMin = Math.Pow(2, attempt - 1);
        delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(expectedMin);
    }
}
