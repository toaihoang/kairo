namespace Kairo.Storage;

/// <summary>
/// Parameters of <see cref="IJobStorage.TransitionAsync"/>: moves a processing job to its next state.
/// </summary>
public sealed record JobTransition
{
    public required string JobId { get; init; }

    /// <summary>The lease owner. The transition fails when it no longer owns the job.</summary>
    public required string Owner { get; init; }

    /// <summary>
    /// <see cref="JobState.Succeeded"/>, <see cref="JobState.DeadLettered"/>, <see cref="JobState.Cancelled"/>,
    /// <see cref="JobState.Scheduled"/> (retry later) or <see cref="JobState.Enqueued"/> (give the job back).
    /// </summary>
    public required JobState NewState { get; init; }

    public required DateTimeOffset Now { get; init; }

    /// <summary>The new due time. Required for <see cref="JobState.Scheduled"/>.</summary>
    public DateTimeOffset? DueAt { get; init; }

    public JobError? Error { get; init; }

    public string? Reason { get; init; }

    /// <summary>
    /// True when the attempt should not count, for example when the job is given back on shutdown.
    /// The stored <see cref="JobRecord.Attempt"/> is then decreased by one.
    /// </summary>
    public bool UndoAttempt { get; init; }

    /// <summary>
    /// A processing job either goes back to waiting (a retry, or given back on shutdown)
    /// or finishes. It cannot stay in Processing.
    /// </summary>
    public static bool IsValidNewState(JobState state) =>
        state.IsWaiting() || state.IsTerminal();
}
