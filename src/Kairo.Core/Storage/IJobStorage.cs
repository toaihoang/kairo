namespace Kairo.Storage;

/// <summary>
/// Persists jobs and recurring jobs. Every storage provider implements this contract,
/// and every method must be atomic.
/// </summary>
/// <remarks>
/// Ownership of a job is a lease: <see cref="AcquireAsync"/> gives a worker a job until
/// <see cref="JobRecord.LeaseExpiresAt"/>. The worker extends the lease with <see cref="RenewLeasesAsync"/>.
/// When the lease expires (the worker crashed or hung), the job can be acquired again by any worker.
/// </remarks>
public interface IJobStorage
{
    /// <summary>
    /// Stores a new job in the <see cref="JobState.Scheduled"/> or <see cref="JobState.Enqueued"/> state.
    /// When <see cref="JobRecord.IdempotencyKey"/> matches an existing job, nothing is stored
    /// and the existing job's id is returned.
    /// </summary>
    Task<string> CreateAsync(JobRecord job, CancellationToken cancellationToken);

    /// <summary>
    /// Takes up to <see cref="AcquireRequest.MaxCount"/> jobs that are due (or whose lease expired),
    /// oldest due time first, and moves them to <see cref="JobState.Processing"/>, owned by
    /// <see cref="AcquireRequest.Owner"/>. Each acquired job's <see cref="JobRecord.Attempt"/> is increased by one.
    /// A job is never returned to two owners with overlapping leases.
    /// </summary>
    Task<IReadOnlyList<JobRecord>> AcquireAsync(AcquireRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Extends the lease of the given jobs that <paramref name="owner"/> still owns,
    /// and returns the ids of those jobs. A missing id means the lease was lost.
    /// </summary>
    Task<IReadOnlyCollection<string>> RenewLeasesAsync(
        string owner,
        IReadOnlyCollection<string> jobIds,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a <see cref="JobState.Processing"/> job to its next state and clears the lease.
    /// Returns false, and changes nothing, when <see cref="JobTransition.Owner"/> no longer owns the job.
    /// </summary>
    Task<bool> TransitionAsync(JobTransition transition, CancellationToken cancellationToken);

    Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>Returns the state changes of a job, oldest first.</summary>
    Task<IReadOnlyList<JobHistoryEntry>> GetHistoryAsync(string jobId, CancellationToken cancellationToken);

    Task<RecurringJobRecord?> GetRecurringAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken);

    /// <summary>Inserts or replaces a recurring job, matched by <see cref="RecurringJobRecord.Name"/>.</summary>
    Task SaveRecurringAsync(RecurringJobRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Compare-and-set for one tick of a recurring job: when its <see cref="RecurringJobRecord.NextRunAt"/>
    /// still equals <see cref="RecurringAdvance.ExpectedNextRunAt"/>, creates the occurrence jobs
    /// (skipping any whose idempotency key exists) and moves <c>NextRunAt</c> forward.
    /// Returns false, and changes nothing, when another scheduler advanced it first.
    /// </summary>
    Task<bool> TryAdvanceRecurringAsync(RecurringAdvance advance, CancellationToken cancellationToken);
}
