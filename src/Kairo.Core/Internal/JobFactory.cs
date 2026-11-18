using Kairo.Storage;
using Microsoft.Extensions.Options;

namespace Kairo.Internal;

/// <summary>
/// Builds new job records with the same defaults for the client and the recurring scheduler.
/// </summary>
internal sealed class JobFactory
{
    private readonly IOptions<KairoOptions> _options;

    public JobFactory(IOptions<KairoOptions> options)
    {
        _options = options;
    }

    public JobRecord Create(
        JobDescriptor job,
        string? payload,
        DateTimeOffset dueAt,
        DateTimeOffset now,
        string? queue = null,
        string? idempotencyKey = null,
        string? recurringJobName = null)
    {
        JobState state = JobState.Enqueued;
        if (dueAt > now)
        {
            state = JobState.Scheduled;
        }

        int maxAttempts = _options.Value.DefaultMaxAttempts;
        if (job.Options.MaxAttempts.HasValue)
        {
            maxAttempts = job.Options.MaxAttempts.Value;
        }

        return new JobRecord
        {
            // Version 7 GUIDs start with the timestamp, so ids sort by creation time.
            Id = Guid.CreateVersion7(now).ToString("N"),
            Name = job.Name,
            Queue = ChooseQueue(queue, job),
            Payload = payload,
            State = state,
            CreatedAt = now,

            // A past due time is kept: the job runs now, ahead of jobs that became due later,
            // and a late recurring occurrence still reports the time it was planned for.
            DueAt = dueAt,
            MaxAttempts = maxAttempts,
            IdempotencyKey = idempotencyKey,
            RecurringJobName = recurringJobName,
        };
    }

    /// <summary>The queue given for this job, else the job type's queue, else the default queue.</summary>
    private static string ChooseQueue(string? requestedQueue, JobDescriptor job)
    {
        if (requestedQueue is not null)
        {
            return requestedQueue;
        }

        if (job.Options.Queue is not null)
        {
            return job.Options.Queue;
        }

        return KairoOptions.DefaultQueue;
    }
}
