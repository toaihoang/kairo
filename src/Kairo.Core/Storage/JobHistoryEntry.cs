namespace Kairo.Storage;

/// <summary>
/// One state change of a job.
/// </summary>
/// <param name="At">When the change happened.</param>
/// <param name="State">The new state.</param>
/// <param name="Owner">The lease owner (worker) that made the change, if any.</param>
/// <param name="Reason">A short explanation, such as "Retry 2 of 5".</param>
/// <param name="Error">The error that caused the change, if any.</param>
public sealed record JobHistoryEntry(
    DateTimeOffset At,
    JobState State,
    string? Owner = null,
    string? Reason = null,
    JobError? Error = null);
