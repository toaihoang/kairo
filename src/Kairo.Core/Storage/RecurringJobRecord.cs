namespace Kairo.Storage;

/// <summary>
/// A stored recurring job definition and its scheduling state.
/// </summary>
public sealed record RecurringJobRecord
{
    /// <summary>The unique name of the recurring job.</summary>
    public required string Name { get; init; }

    /// <summary>The registered job name each occurrence runs.</summary>
    public required string JobName { get; init; }

    /// <summary>A 5-field cron expression, or 6 fields when the first one is seconds.</summary>
    public required string Cron { get; init; }

    /// <summary>The time zone the cron expression is evaluated in; <see langword="null"/> means UTC.</summary>
    public string? TimeZoneId { get; init; }

    public string? Queue { get; init; }

    public string? Payload { get; init; }

    public Misfire Misfire { get; init; }

    /// <summary>When true, an occurrence is skipped while the previous one has not finished.</summary>
    public bool PreventOverlap { get; init; }

    public bool IsPaused { get; init; }

    /// <summary>The next planned occurrence; <see langword="null"/> when the schedule never fires again.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>The id of the job created for the latest occurrence.</summary>
    public string? LastJobId { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
