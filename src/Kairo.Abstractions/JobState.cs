namespace Kairo;

/// <summary>
/// The life cycle of a job:
/// <c>Scheduled → Enqueued → Processing → Succeeded | Scheduled (retry) | DeadLettered | Cancelled</c>.
/// </summary>
public enum JobState
{
    /// <summary>Waiting for its due time (a delayed job or a retry).</summary>
    Scheduled = 0,

    /// <summary>Due and waiting for a worker.</summary>
    Enqueued = 1,

    /// <summary>Owned by a worker through a lease.</summary>
    Processing = 2,

    /// <summary>Finished without an error. Terminal.</summary>
    Succeeded = 3,

    /// <summary>Failed and will not be retried. Terminal.</summary>
    DeadLettered = 4,

    /// <summary>Cancelled before it finished. Terminal.</summary>
    Cancelled = 5,
}

public static class JobStateExtensions
{
    /// <summary>True for states a job never leaves.</summary>
    public static bool IsTerminal(this JobState state) =>
        state is JobState.Succeeded or JobState.DeadLettered or JobState.Cancelled;

    /// <summary>True for states where a job waits to be picked up by a worker.</summary>
    public static bool IsWaiting(this JobState state) =>
        state is JobState.Scheduled or JobState.Enqueued;
}
