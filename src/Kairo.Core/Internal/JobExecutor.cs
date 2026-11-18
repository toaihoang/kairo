using Kairo.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kairo.Internal;

/// <summary>
/// Runs one acquired job and records the outcome: success, a retry, dead-letter,
/// or giving the job back when the server shuts down.
/// </summary>
internal sealed class JobExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobRegistry _registry;
    private readonly IJobStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IServiceScopeFactory scopeFactory,
        JobRegistry registry,
        IJobStorage storage,
        TimeProvider timeProvider,
        ILogger<JobExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <param name="job">A job in the Processing state, owned by <paramref name="owner"/>.</param>
    /// <param name="owner">The lease owner.</param>
    /// <param name="leaseLost">Cancelled when the lease renewer finds that another worker took the job.</param>
    /// <param name="shutdown">Cancelled when the server stops waiting for running jobs.</param>
    public async Task ExecuteAsync(JobRecord job, string owner, CancellationToken leaseLost, CancellationToken shutdown)
    {
        JobDescriptor? descriptor = _registry.FindJob(job.Name);
        if (descriptor is null)
        {
            await RejectAsync(job, owner, $"No job is registered with the name '{job.Name}'.").ConfigureAwait(false);
            return;
        }

        // Only possible after lease recovery: the job crashed its worker on every attempt.
        if (job.Attempt > job.MaxAttempts)
        {
            await RejectAsync(job, owner, $"All {job.MaxAttempts} attempts were used up by workers that stopped while running it.").ConfigureAwait(false);
            return;
        }

        using CancellationTokenSource? timeout = CreateTimeout(descriptor);
        CancellationToken timeoutToken = CancellationToken.None;
        if (timeout is not null)
        {
            timeoutToken = timeout.Token;
        }

        // Cancelled by whichever comes first: lost lease, shutdown or timeout.
        using CancellationTokenSource jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(leaseLost, shutdown, timeoutToken);

        long startedAt = _timeProvider.GetTimestamp();
        try
        {
            await InvokeInNewScopeAsync(descriptor, CreateContext(job), job.Payload, jobCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (leaseLost.IsCancellationRequested)
        {
            // Another worker owns the job now; it records the outcome.
            Log.JobLeaseLost(_logger, job.Id, job.Name);
            return;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            await ReleaseAsync(job, owner).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException ex) when (timeoutToken.IsCancellationRequested)
        {
            TimeoutException timeoutException = new TimeoutException($"The job ran longer than its timeout of {descriptor.Options.Timeout}.", ex);
            await FailAsync(job, owner, descriptor, timeoutException).ConfigureAwait(false);
            return;
        }
#pragma warning disable CA1031 // A job may throw anything; the failure is recorded, not rethrown.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await FailAsync(job, owner, descriptor, ex).ConfigureAwait(false);
            return;
        }

        await SucceedAsync(job, owner).ConfigureAwait(false);

        TimeSpan elapsed = _timeProvider.GetElapsedTime(startedAt);
        Log.JobSucceeded(_logger, job.Id, job.Name, (long)elapsed.TotalMilliseconds);
    }

    private static JobContext CreateContext(JobRecord job)
    {
        return new JobContext
        {
            JobId = job.Id,
            JobName = job.Name,
            Queue = job.Queue,
            Attempt = job.Attempt,
            MaxAttempts = job.MaxAttempts,
            ScheduledAt = job.DueAt,
            RecurringJobName = job.RecurringJobName,
        };
    }

    /// <summary>Returns a token source that cancels after the job's timeout, or null when the job has none.</summary>
    private CancellationTokenSource? CreateTimeout(JobDescriptor descriptor)
    {
        if (!descriptor.Options.Timeout.HasValue)
        {
            return null;
        }

        return new CancellationTokenSource(descriptor.Options.Timeout.Value, _timeProvider);
    }

    /// <summary>Runs the job in its own dependency injection scope, so scoped services are not shared between runs.</summary>
    private async Task InvokeInNewScopeAsync(JobDescriptor descriptor, JobContext context, string? payload, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        try
        {
            await descriptor.Invoker(scope.ServiceProvider, context, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SucceedAsync(JobRecord job, string owner)
    {
        await TransitionAsync(new JobTransition
        {
            JobId = job.Id,
            Owner = owner,
            NewState = JobState.Succeeded,
            Now = _timeProvider.GetUtcNow(),
        }).ConfigureAwait(false);
    }

    private async Task FailAsync(JobRecord job, string owner, JobDescriptor descriptor, Exception exception)
    {
        bool hasAttemptsLeft = job.Attempt < job.MaxAttempts;
        bool isRetryable = descriptor.Options.ShouldRetry(exception);

        if (hasAttemptsLeft && isRetryable)
        {
            await RetryLaterAsync(job, owner, descriptor, exception).ConfigureAwait(false);
            return;
        }

        string reason = $"Attempt {job.Attempt} of {job.MaxAttempts} failed.";
        if (hasAttemptsLeft)
        {
            reason = "The exception is not retried.";
        }

        await TransitionAsync(new JobTransition
        {
            JobId = job.Id,
            Owner = owner,
            NewState = JobState.DeadLettered,
            Now = _timeProvider.GetUtcNow(),
            Error = JobError.FromException(exception),
            Reason = reason,
        }).ConfigureAwait(false);

        Log.JobDeadLettered(_logger, exception, job.Id, job.Name, job.Attempt, job.MaxAttempts);
    }

    private async Task RetryLaterAsync(JobRecord job, string owner, JobDescriptor descriptor, Exception exception)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset dueAt = now + descriptor.Options.Backoff.GetDelay(job.Attempt);

        await TransitionAsync(new JobTransition
        {
            JobId = job.Id,
            Owner = owner,
            NewState = JobState.Scheduled,
            Now = now,
            DueAt = dueAt,
            Error = JobError.FromException(exception),
            Reason = $"Attempt {job.Attempt} of {job.MaxAttempts} failed; retrying.",
        }).ConfigureAwait(false);

        Log.JobRetryScheduled(_logger, exception, job.Id, job.Name, job.Attempt, job.MaxAttempts, dueAt);
    }

    private async Task RejectAsync(JobRecord job, string owner, string reason)
    {
        await TransitionAsync(new JobTransition
        {
            JobId = job.Id,
            Owner = owner,
            NewState = JobState.DeadLettered,
            Now = _timeProvider.GetUtcNow(),
            Reason = reason,
        }).ConfigureAwait(false);

        Log.JobRejected(_logger, job.Id, reason);
    }

    /// <summary>Gives the job back to its queue without counting the attempt.</summary>
    private async Task ReleaseAsync(JobRecord job, string owner)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await TransitionAsync(new JobTransition
        {
            JobId = job.Id,
            Owner = owner,
            NewState = JobState.Enqueued,
            Now = now,
            DueAt = now,
            UndoAttempt = true,
            Reason = "Cancelled by server shutdown.",
        }).ConfigureAwait(false);

        Log.JobReleased(_logger, job.Id, job.Name);
    }

    private async Task TransitionAsync(JobTransition transition)
    {
        // Not cancellable: the outcome must be recorded even while the server shuts down.
        bool recorded = await _storage.TransitionAsync(transition, CancellationToken.None).ConfigureAwait(false);
        if (!recorded)
        {
            Log.TransitionRejected(_logger, transition.JobId);
        }
    }
}
