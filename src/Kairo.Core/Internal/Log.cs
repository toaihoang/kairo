using Microsoft.Extensions.Logging;

namespace Kairo.Internal;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Debug, "Job {JobId} ({JobName}) succeeded in {ElapsedMilliseconds} ms.")]
    public static partial void JobSucceeded(ILogger logger, string jobId, string jobName, long elapsedMilliseconds);

    [LoggerMessage(2, LogLevel.Warning, "Job {JobId} ({JobName}) failed on attempt {Attempt} of {MaxAttempts}; the next attempt is due at {DueAt}.")]
    public static partial void JobRetryScheduled(ILogger logger, Exception exception, string jobId, string jobName, int attempt, int maxAttempts, DateTimeOffset dueAt);

    [LoggerMessage(3, LogLevel.Error, "Job {JobId} ({JobName}) failed on attempt {Attempt} of {MaxAttempts} and was dead-lettered.")]
    public static partial void JobDeadLettered(ILogger logger, Exception exception, string jobId, string jobName, int attempt, int maxAttempts);

    [LoggerMessage(4, LogLevel.Error, "Job {JobId} was dead-lettered: {Reason}")]
    public static partial void JobRejected(ILogger logger, string jobId, string reason);

    [LoggerMessage(5, LogLevel.Warning, "Lost the lease of job {JobId} ({JobName}); another worker may run it.")]
    public static partial void JobLeaseLost(ILogger logger, string jobId, string jobName);

    [LoggerMessage(6, LogLevel.Information, "Job {JobId} ({JobName}) was cancelled by shutdown and given back to its queue.")]
    public static partial void JobReleased(ILogger logger, string jobId, string jobName);

    [LoggerMessage(7, LogLevel.Warning, "Could not record the result of job {JobId}: this worker no longer owns it.")]
    public static partial void TransitionRejected(ILogger logger, string jobId);

    [LoggerMessage(8, LogLevel.Error, "Could not fetch jobs from queue {Queue}.")]
    public static partial void AcquireFailed(ILogger logger, Exception exception, string queue);

    [LoggerMessage(9, LogLevel.Error, "Could not renew the leases of {Count} running job(s).")]
    public static partial void LeaseRenewalFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(10, LogLevel.Error, "The recurring job scheduler failed; it will try again.")]
    public static partial void RecurringTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(11, LogLevel.Information, "Recurring job {Name}: skipped the occurrence at {Occurrence} because job {JobId} from the previous occurrence has not finished.")]
    public static partial void RecurringOverlapSkipped(ILogger logger, string name, DateTimeOffset occurrence, string jobId);

    [LoggerMessage(12, LogLevel.Information, "Recurring job {Name}: skipped {Count} missed occurrence(s).")]
    public static partial void RecurringMisfireSkipped(ILogger logger, string name, int count);

    [LoggerMessage(13, LogLevel.Error, "Recurring job {Name} has an invalid schedule and was not run.")]
    public static partial void RecurringScheduleInvalid(ILogger logger, Exception exception, string name);

    [LoggerMessage(14, LogLevel.Information, "Kairo server {ServerName} started. Queues: {Queues}.")]
    public static partial void ServerStarted(ILogger logger, string serverName, string queues);

    [LoggerMessage(15, LogLevel.Warning, "{Count} job(s) still running after the shutdown timeout of {Timeout}; cancelling them.")]
    public static partial void ShutdownTimedOut(ILogger logger, int count, TimeSpan timeout);

    [LoggerMessage(16, LogLevel.Information, "Kairo server {ServerName} stopped.")]
    public static partial void ServerStopped(ILogger logger, string serverName);

    [LoggerMessage(17, LogLevel.Error, "An unexpected error stopped job {JobId}.")]
    public static partial void JobCrashed(ILogger logger, Exception exception, string jobId);
}
