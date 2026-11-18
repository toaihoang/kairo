namespace Kairo;

/// <summary>
/// Per-call options for <see cref="IJobClient"/>.
/// </summary>
public sealed class EnqueueOptions
{
    /// <summary>The queue to put the job on. Overrides the queue set when the job was registered.</summary>
    public string? Queue { get; init; }

    /// <summary>
    /// When set, a second job with the same key is not created; the id of the existing job is returned instead.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
