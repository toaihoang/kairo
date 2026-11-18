namespace Kairo;

/// <summary>
/// Settings for one job type, given when the job is registered with <see cref="KairoBuilder.AddJob{TJob}(string?, Action{JobOptions}?)"/>.
/// </summary>
public sealed class JobOptions
{
    /// <summary>The default queue for this job. <see cref="EnqueueOptions.Queue"/> overrides it.</summary>
    public string? Queue { get; set; }

    /// <summary>The total number of attempts, including the first one. Defaults to <see cref="KairoOptions.DefaultMaxAttempts"/>.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>The wait between a failed attempt and the next one.</summary>
    public Backoff Backoff { get; set; } = Backoff.Exponential(TimeSpan.FromSeconds(15), TimeSpan.FromHours(1));

    /// <summary>When set, an attempt that runs longer is cancelled and counts as failed.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Decides whether an exception is retried. When <see langword="null"/>, every exception is retried.</summary>
    public Func<Exception, bool>? RetryWhen { get; set; }

    internal bool ShouldRetry(Exception exception)
    {
        if (RetryWhen is null)
        {
            return true;
        }

        return RetryWhen(exception);
    }
}
