using Kairo.Storage;

namespace Kairo.Internal;

internal sealed class JobClient : IJobClient
{
    private readonly IJobStorage _storage;
    private readonly JobRegistry _registry;
    private readonly JobFactory _jobFactory;
    private readonly KairoSerializer _serializer;
    private readonly JobSignal _signal;
    private readonly TimeProvider _timeProvider;

    public JobClient(
        IJobStorage storage,
        JobRegistry registry,
        JobFactory jobFactory,
        KairoSerializer serializer,
        JobSignal signal,
        TimeProvider timeProvider)
    {
        _storage = storage;
        _registry = registry;
        _jobFactory = jobFactory;
        _serializer = serializer;
        _signal = signal;
        _timeProvider = timeProvider;
    }

    public Task<string> EnqueueAsync<TJob>(EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob
        => CreateAsync(typeof(TJob), payload: null, runAt: null, options, cancellationToken);

    public Task<string> EnqueueAsync<TJob, TPayload>(TPayload payload, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob<TPayload>
        => CreateAsync(typeof(TJob), _serializer.Serialize(payload), runAt: null, options, cancellationToken);

    public Task<string> ScheduleAsync<TJob>(DateTimeOffset runAt, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob
        => CreateAsync(typeof(TJob), payload: null, runAt, options, cancellationToken);

    public Task<string> ScheduleAsync<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob<TPayload>
        => CreateAsync(typeof(TJob), _serializer.Serialize(payload), runAt, options, cancellationToken);

    public Task<string> ScheduleAsync<TJob>(TimeSpan delay, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob
        => CreateAsync(typeof(TJob), payload: null, _timeProvider.GetUtcNow() + delay, options, cancellationToken);

    public Task<string> ScheduleAsync<TJob, TPayload>(TPayload payload, TimeSpan delay, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob<TPayload>
        => CreateAsync(typeof(TJob), _serializer.Serialize(payload), _timeProvider.GetUtcNow() + delay, options, cancellationToken);

    private async Task<string> CreateAsync(
        Type jobType,
        string? payload,
        DateTimeOffset? runAt,
        EnqueueOptions? options,
        CancellationToken cancellationToken)
    {
        JobDescriptor descriptor = _registry.GetJob(jobType);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        DateTimeOffset dueAt = now;
        if (runAt.HasValue)
        {
            dueAt = runAt.Value;
        }

        string? queue = null;
        string? idempotencyKey = null;
        if (options is not null)
        {
            queue = options.Queue;
            idempotencyKey = options.IdempotencyKey;
        }

        JobRecord job = _jobFactory.Create(descriptor, payload, dueAt, now, queue, idempotencyKey);

        string jobId = await _storage.CreateAsync(job, cancellationToken).ConfigureAwait(false);
        if (job.State == JobState.Enqueued)
        {
            _signal.Notify();
        }

        return jobId;
    }
}
