namespace Kairo.Storage.InMemory;

/// <summary>
/// Keeps jobs in process memory. Fast and needs no setup, but jobs are lost when the process
/// stops and cannot be shared between processes. Use it for development, tests and small apps
/// where losing queued work is acceptable.
/// </summary>
/// <remarks>
/// One lock guards all state, which makes every operation atomic. Methods whose names end in
/// "WhileLocked" must only be called while holding that lock. Waiting jobs are indexed per queue,
/// sorted by due time, so acquiring does not scan the finished jobs.
/// </remarks>
public sealed class InMemoryJobStorage : IJobStorage
{
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, JobRecord> _jobs = new Dictionary<string, JobRecord>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<JobHistoryEntry>> _history = new Dictionary<string, List<JobHistoryEntry>>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _jobIdsByIdempotencyKey = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<WaitingJob>> _waitingJobsByQueue = new Dictionary<string, SortedSet<WaitingJob>>(StringComparer.Ordinal);
    private readonly HashSet<string> _processingJobIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, RecurringJobRecord> _recurringJobs = new Dictionary<string, RecurringJobRecord>(StringComparer.Ordinal);

    // Increases with every waiting job, so jobs with the same due time keep their insertion order.
    private long _waitingSequence;

    public Task<string> CreateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        lock (_lock)
        {
            string jobId = CreateJobWhileLocked(job);
            return Task.FromResult(jobId);
        }
    }

    public Task<IReadOnlyList<JobRecord>> AcquireAsync(AcquireRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxCount, 1);

        List<JobRecord> acquired = new List<JobRecord>();
        lock (_lock)
        {
            // Jobs whose lease expired are the most overdue, so they go first.
            foreach (string jobId in FindExpiredLeasesWhileLocked(request))
            {
                if (acquired.Count == request.MaxCount)
                {
                    break;
                }

                acquired.Add(TakeJobWhileLocked(_jobs[jobId], request, "The previous lease expired."));
            }

            while (acquired.Count < request.MaxCount)
            {
                string? jobId = TakeNextDueJobIdWhileLocked(request);
                if (jobId is null)
                {
                    break;
                }

                acquired.Add(TakeJobWhileLocked(_jobs[jobId], request, reason: null));
            }
        }

        return Task.FromResult<IReadOnlyList<JobRecord>>(acquired);
    }

    public Task<IReadOnlyCollection<string>> RenewLeasesAsync(
        string owner,
        IReadOnlyCollection<string> jobIds,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        List<string> renewedJobIds = new List<string>();
        lock (_lock)
        {
            foreach (string jobId in jobIds)
            {
                if (!_jobs.TryGetValue(jobId, out JobRecord? job))
                {
                    continue;
                }

                if (!IsOwnedBy(job, owner))
                {
                    continue;
                }

                _jobs[jobId] = job with { LeaseExpiresAt = leaseExpiresAt };
                renewedJobIds.Add(jobId);
            }
        }

        return Task.FromResult<IReadOnlyCollection<string>>(renewedJobIds);
    }

    public Task<bool> TransitionAsync(JobTransition transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ValidateTransition(transition);

        lock (_lock)
        {
            if (!_jobs.TryGetValue(transition.JobId, out JobRecord? job))
            {
                return Task.FromResult(false);
            }

            if (!IsOwnedBy(job, transition.Owner))
            {
                return Task.FromResult(false);
            }

            JobRecord updated = ApplyTransition(job, transition);
            _jobs[job.Id] = updated;
            _processingJobIds.Remove(job.Id);

            if (!updated.State.IsTerminal())
            {
                AddWaitingJobWhileLocked(updated);
            }

            JobHistoryEntry entry = new JobHistoryEntry(transition.Now, transition.NewState, transition.Owner, transition.Reason, transition.Error);
            AddHistoryWhileLocked(job.Id, entry);
            return Task.FromResult(true);
        }
    }

    public Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _jobs.TryGetValue(jobId, out JobRecord? job);
            return Task.FromResult(job);
        }
    }

    public Task<IReadOnlyList<JobHistoryEntry>> GetHistoryAsync(string jobId, CancellationToken cancellationToken)
    {
        List<JobHistoryEntry> copy = new List<JobHistoryEntry>();
        lock (_lock)
        {
            if (_history.TryGetValue(jobId, out List<JobHistoryEntry>? entries))
            {
                copy.AddRange(entries);
            }
        }

        return Task.FromResult<IReadOnlyList<JobHistoryEntry>>(copy);
    }

    public Task<RecurringJobRecord?> GetRecurringAsync(string name, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _recurringJobs.TryGetValue(name, out RecurringJobRecord? record);
            return Task.FromResult(record);
        }
    }

    public Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken)
    {
        List<RecurringJobRecord> copy;
        lock (_lock)
        {
            copy = new List<RecurringJobRecord>(_recurringJobs.Values);
        }

        return Task.FromResult<IReadOnlyList<RecurringJobRecord>>(copy);
    }

    public Task SaveRecurringAsync(RecurringJobRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_lock)
        {
            _recurringJobs[record.Name] = record;
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryAdvanceRecurringAsync(RecurringAdvance advance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(advance);

        lock (_lock)
        {
            if (!_recurringJobs.TryGetValue(advance.Name, out RecurringJobRecord? record))
            {
                return Task.FromResult(false);
            }

            // Another scheduler already handled this tick.
            if (record.NextRunAt != advance.ExpectedNextRunAt)
            {
                return Task.FromResult(false);
            }

            string? lastJobId = record.LastJobId;
            foreach (JobRecord job in advance.Jobs)
            {
                lastJobId = CreateJobWhileLocked(job);
            }

            _recurringJobs[advance.Name] = record with
            {
                NextRunAt = advance.NextRunAt,
                LastJobId = lastJobId,
                UpdatedAt = advance.Now,
            };
            return Task.FromResult(true);
        }
    }

    private static bool IsOwnedBy(JobRecord job, string owner)
    {
        if (job.State != JobState.Processing)
        {
            return false;
        }

        return string.Equals(job.LeaseOwner, owner, StringComparison.Ordinal);
    }

    private static void ValidateTransition(JobTransition transition)
    {
        if (!JobTransition.IsValidNewState(transition.NewState))
        {
            throw new ArgumentException($"A processing job cannot move to {transition.NewState}.", nameof(transition));
        }

        if (transition.NewState == JobState.Scheduled && !transition.DueAt.HasValue)
        {
            throw new ArgumentException("A transition to Scheduled needs a DueAt.", nameof(transition));
        }
    }

    private static JobRecord ApplyTransition(JobRecord job, JobTransition transition)
    {
        DateTimeOffset dueAt = job.DueAt;
        if (transition.DueAt.HasValue)
        {
            dueAt = transition.DueAt.Value;
        }

        int attempt = job.Attempt;
        if (transition.UndoAttempt)
        {
            attempt = job.Attempt - 1;
        }

        DateTimeOffset? completedAt = null;
        if (transition.NewState.IsTerminal())
        {
            completedAt = transition.Now;
        }

        JobError? lastError = job.LastError;
        if (transition.Error is not null)
        {
            lastError = transition.Error;
        }

        return job with
        {
            State = transition.NewState,
            DueAt = dueAt,
            Attempt = attempt,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            CompletedAt = completedAt,
            LastError = lastError,
        };
    }

    private string CreateJobWhileLocked(JobRecord job)
    {
        if (!job.State.IsWaiting())
        {
            throw new ArgumentException($"A new job must be Scheduled or Enqueued, not {job.State}.", nameof(job));
        }

        if (job.IdempotencyKey is not null)
        {
            if (_jobIdsByIdempotencyKey.TryGetValue(job.IdempotencyKey, out string? existingJobId))
            {
                return existingJobId;
            }
        }

        if (!_jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException($"A job with id '{job.Id}' already exists.");
        }

        if (job.IdempotencyKey is not null)
        {
            _jobIdsByIdempotencyKey.Add(job.IdempotencyKey, job.Id);
        }

        AddWaitingJobWhileLocked(job);
        AddHistoryWhileLocked(job.Id, new JobHistoryEntry(job.CreatedAt, job.State));
        return job.Id;
    }

    private List<string> FindExpiredLeasesWhileLocked(AcquireRequest request)
    {
        List<string> expired = new List<string>();
        foreach (string jobId in _processingJobIds)
        {
            JobRecord job = _jobs[jobId];
            if (job.LeaseExpiresAt > request.Now)
            {
                continue;
            }

            if (!request.Queues.Contains(job.Queue))
            {
                continue;
            }

            expired.Add(jobId);
        }

        return expired;
    }

    /// <summary>
    /// Removes the waiting job with the earliest due time across the requested queues from the index
    /// and returns its id, or returns null when no job is due.
    /// </summary>
    private string? TakeNextDueJobIdWhileLocked(AcquireRequest request)
    {
        SortedSet<WaitingJob>? earliestQueue = null;
        WaitingJob? earliestJob = null;

        foreach (string queue in request.Queues)
        {
            if (!_waitingJobsByQueue.TryGetValue(queue, out SortedSet<WaitingJob>? waitingJobs))
            {
                continue;
            }

            if (waitingJobs.Count == 0)
            {
                continue;
            }

            WaitingJob first = waitingJobs.Min!; // Not null: the set is not empty.
            if (first.DueAt > request.Now)
            {
                continue;
            }

            if (earliestJob is null || first.CompareTo(earliestJob) < 0)
            {
                earliestQueue = waitingJobs;
                earliestJob = first;
            }
        }

        if (earliestQueue is null || earliestJob is null)
        {
            return null;
        }

        earliestQueue.Remove(earliestJob);
        return earliestJob.JobId;
    }

    private JobRecord TakeJobWhileLocked(JobRecord job, AcquireRequest request, string? reason)
    {
        JobRecord taken = job with
        {
            State = JobState.Processing,
            Attempt = job.Attempt + 1,
            LeaseOwner = request.Owner,
            LeaseExpiresAt = request.Now + request.LeaseDuration,
        };

        _jobs[job.Id] = taken;
        _processingJobIds.Add(job.Id);
        AddHistoryWhileLocked(job.Id, new JobHistoryEntry(request.Now, JobState.Processing, request.Owner, reason));
        return taken;
    }

    private void AddWaitingJobWhileLocked(JobRecord job)
    {
        if (!_waitingJobsByQueue.TryGetValue(job.Queue, out SortedSet<WaitingJob>? waitingJobs))
        {
            waitingJobs = new SortedSet<WaitingJob>();
            _waitingJobsByQueue.Add(job.Queue, waitingJobs);
        }

        _waitingSequence++;
        waitingJobs.Add(new WaitingJob(job.DueAt, _waitingSequence, job.Id));
    }

    private void AddHistoryWhileLocked(string jobId, JobHistoryEntry entry)
    {
        if (!_history.TryGetValue(jobId, out List<JobHistoryEntry>? entries))
        {
            entries = new List<JobHistoryEntry>();
            _history.Add(jobId, entries);
        }

        entries.Add(entry);
    }

    /// <summary>A job in the waiting index, ordered by due time, then by insertion order.</summary>
    private sealed class WaitingJob : IComparable<WaitingJob>
    {
        public WaitingJob(DateTimeOffset dueAt, long sequence, string jobId)
        {
            DueAt = dueAt;
            Sequence = sequence;
            JobId = jobId;
        }

        public DateTimeOffset DueAt { get; }

        public long Sequence { get; }

        public string JobId { get; }

        public int CompareTo(WaitingJob? other)
        {
            if (other is null)
            {
                return 1;
            }

            int byDueAt = DueAt.CompareTo(other.DueAt);
            if (byDueAt != 0)
            {
                return byDueAt;
            }

            return Sequence.CompareTo(other.Sequence);
        }
    }
}
