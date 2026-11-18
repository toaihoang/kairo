namespace Kairo;

/// <summary>
/// Information about the current run of a job.
/// </summary>
public sealed class JobContext
{
    public required string JobId { get; init; }

    public required string JobName { get; init; }

    public required string Queue { get; init; }

    /// <summary>The 1-based number of this attempt.</summary>
    public required int Attempt { get; init; }

    public required int MaxAttempts { get; init; }

    /// <summary>The time this run was planned for.</summary>
    public required DateTimeOffset ScheduledAt { get; init; }

    /// <summary>The recurring job that created this job, or <see langword="null"/>.</summary>
    public string? RecurringJobName { get; init; }

    /// <summary>True when a failure of this attempt will not be retried.</summary>
    public bool IsLastAttempt => Attempt >= MaxAttempts;
}
