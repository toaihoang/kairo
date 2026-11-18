namespace Kairo;

/// <summary>
/// Creates jobs. Every method returns the id of the created job, or of the existing job
/// when <see cref="EnqueueOptions.IdempotencyKey"/> matches one.
/// </summary>
public interface IJobClient
{
    /// <summary>Creates a job that runs as soon as a worker is free.</summary>
    Task<string> EnqueueAsync<TJob>(EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>Creates a job with a payload that runs as soon as a worker is free.</summary>
    Task<string> EnqueueAsync<TJob, TPayload>(TPayload payload, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob<TPayload>;

    /// <summary>Creates a job that runs at <paramref name="runAt"/>. A time in the past runs it now.</summary>
    Task<string> ScheduleAsync<TJob>(DateTimeOffset runAt, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>Creates a job with a payload that runs at <paramref name="runAt"/>. A time in the past runs it now.</summary>
    Task<string> ScheduleAsync<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob<TPayload>;

    /// <summary>Creates a job that runs after <paramref name="delay"/>.</summary>
    Task<string> ScheduleAsync<TJob>(TimeSpan delay, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>Creates a job with a payload that runs after <paramref name="delay"/>.</summary>
    Task<string> ScheduleAsync<TJob, TPayload>(TPayload payload, TimeSpan delay, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob<TPayload>;
}
