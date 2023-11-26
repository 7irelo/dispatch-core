namespace DispatchCore.Core.Scheduling;

public static class RetryPolicy
{
    private static readonly Random Jitter = new();

    public static TimeSpan CalculateDelay(int attempt, TimeSpan? baseDelay = null)
    {
        var @base = baseDelay ?? TimeSpan.FromSeconds(2);
        var exponential = Math.Pow(2, attempt - 1);
        var delaySeconds = @base.TotalSeconds * exponential;
        var jitterSeconds = Jitter.NextDouble() * delaySeconds * 0.3;
        return TimeSpan.FromSeconds(delaySeconds + jitterSeconds);
    }

    public static DateTimeOffset NextRunAt(int attempt, TimeSpan? baseDelay = null)
    {
        return DateTimeOffset.UtcNow.Add(CalculateDelay(attempt, baseDelay));
    }

    public static bool ShouldDeadLetter(int attempts, int maxAttempts)
    {
        return attempts >= maxAttempts;
    }
}
