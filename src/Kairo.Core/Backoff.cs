namespace Kairo;

/// <summary>
/// Computes the wait before the next attempt of a failed job.
/// </summary>
public abstract class Backoff
{
    /// <summary>No wait: the next attempt is due at once.</summary>
    public static Backoff None { get; } = new FixedBackoff(TimeSpan.Zero);

    /// <summary>Returns the wait after attempt <paramref name="failedAttempt"/> (1-based) failed.</summary>
    public abstract TimeSpan GetDelay(int failedAttempt);

    /// <summary>The same wait after every failure.</summary>
    public static Backoff Fixed(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        return new FixedBackoff(delay);
    }

    /// <summary>
    /// Doubles the wait after every failure, starting at <paramref name="initialDelay"/> and capped at
    /// <paramref name="maxDelay"/>. With <paramref name="jitter"/>, each wait is randomly 80%–120% of
    /// that value, so jobs that failed together do not all retry at the same moment.
    /// </summary>
    public static Backoff Exponential(TimeSpan initialDelay, TimeSpan maxDelay, bool jitter = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, initialDelay);
        return new ExponentialBackoff(initialDelay, maxDelay, jitter);
    }

    private sealed class FixedBackoff : Backoff
    {
        private readonly TimeSpan _delay;

        public FixedBackoff(TimeSpan delay)
        {
            _delay = delay;
        }

        public override TimeSpan GetDelay(int failedAttempt)
        {
            return _delay;
        }
    }

    private sealed class ExponentialBackoff : Backoff
    {
        private const double MinJitterFactor = 0.8;
        private const double JitterRange = 0.4;

        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _maxDelay;
        private readonly bool _jitter;

        public ExponentialBackoff(TimeSpan initialDelay, TimeSpan maxDelay, bool jitter)
        {
            _initialDelay = initialDelay;
            _maxDelay = maxDelay;
            _jitter = jitter;
        }

        public override TimeSpan GetDelay(int failedAttempt)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempt, 1);

            // Computed in double, so a large attempt number cannot overflow before the cap is applied.
            double doubledTicks = _initialDelay.Ticks * Math.Pow(2, failedAttempt - 1);
            double cappedTicks = Math.Min(doubledTicks, _maxDelay.Ticks);
            if (!_jitter)
            {
                return TimeSpan.FromTicks((long)cappedTicks);
            }

            // A random factor between 0.8 and 1.2.
            double jitterFactor = MinJitterFactor + (Random.Shared.NextDouble() * JitterRange);
            double jitteredTicks = Math.Min(cappedTicks * jitterFactor, _maxDelay.Ticks);
            return TimeSpan.FromTicks((long)jitteredTicks);
        }
    }
}
