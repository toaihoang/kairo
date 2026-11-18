namespace Kairo.Storage;

/// <summary>
/// Parameters of <see cref="IJobStorage.AcquireAsync"/>.
/// </summary>
/// <param name="Queues">The queues to take jobs from.</param>
/// <param name="MaxCount">The maximum number of jobs to take.</param>
/// <param name="Owner">The worker that will own the jobs.</param>
/// <param name="Now">The current time; jobs due at or before it are taken.</param>
/// <param name="LeaseDuration">How long the lease lasts before it must be renewed.</param>
public sealed record AcquireRequest(
    IReadOnlyList<string> Queues,
    int MaxCount,
    string Owner,
    DateTimeOffset Now,
    TimeSpan LeaseDuration);
