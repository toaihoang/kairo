using Kairo.Internal;
using Kairo.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Kairo.Testing;

/// <summary>
/// Runs Kairo jobs in tests without a hosted server or real time. Jobs run one by one when you call
/// <see cref="RunDueJobsAsync"/>, and time only moves when you call <see cref="AdvanceAsync"/>,
/// so tests are deterministic.
/// </summary>
/// <example>
/// <code>
/// await using KairoTestHost host = KairoTestHost.Create(kairo => kairo.AddJob&lt;SendEmailJob, Email&gt;());
/// await host.Client.ScheduleAsync&lt;SendEmailJob, Email&gt;(email, TimeSpan.FromMinutes(5));
/// await host.AdvanceAsync(TimeSpan.FromMinutes(5));   // the job runs here
/// </code>
/// </example>
public sealed class KairoTestHost : IAsyncDisposable
{
    private const int MaxRunsPerCall = 10_000;
    private const string Owner = "kairo-test-host";

    private static readonly DateTimeOffset s_defaultStartTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly JobExecutor _executor;
    private readonly RecurringJobScheduler _scheduler;
    private readonly KairoOptions _options;
    private readonly List<string> _queues;
    private bool _recurringJobsSynced;

    private KairoTestHost(ServiceProvider services, FakeTimeProvider time)
    {
        _services = services;
        Time = time;
        Client = services.GetRequiredService<IJobClient>();
        Storage = services.GetRequiredService<IJobStorage>();
        _executor = services.GetRequiredService<JobExecutor>();
        _scheduler = services.GetRequiredService<RecurringJobScheduler>();
        _options = services.GetRequiredService<IOptions<KairoOptions>>().Value;
        _queues = new List<string>(_options.Queues.Keys);
    }

    /// <summary>The clock seen by Kairo and by jobs that inject <see cref="TimeProvider"/>.</summary>
    public FakeTimeProvider Time { get; }

    public IServiceProvider Services => _services;

    public IJobClient Client { get; }

    public IJobStorage Storage { get; }

    /// <param name="configure">Registers jobs. In-memory storage is used unless another storage is set here.</param>
    /// <param name="configureServices">Registers the services your jobs depend on.</param>
    /// <param name="startTime">The initial fake time. Defaults to 2026-01-01 00:00 UTC.</param>
    public static KairoTestHost Create(
        Action<KairoBuilder> configure,
        Action<IServiceCollection>? configureServices = null,
        DateTimeOffset? startTime = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        DateTimeOffset initialTime = s_defaultStartTime;
        if (startTime.HasValue)
        {
            initialTime = startTime.Value;
        }

        FakeTimeProvider time = new FakeTimeProvider(initialTime);

        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        if (configureServices is not null)
        {
            configureServices(services);
        }

        // Silent logging, unless the test registered its own.
        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));

        services.AddKairo(kairo =>
        {
            kairo.UseInMemoryStorage();
            configure(kairo);
        });

        ServiceProviderOptions providerOptions = new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        };
        ServiceProvider provider = services.BuildServiceProvider(providerOptions);
        return new KairoTestHost(provider, time);
    }

    /// <summary>
    /// Creates the jobs of due recurring occurrences, then runs every due job, including retries
    /// that become due with zero backoff, until none is left. Returns the number of runs.
    /// </summary>
    public async Task<int> RunDueJobsAsync(CancellationToken cancellationToken = default)
    {
        await SyncRecurringJobsAsync(cancellationToken).ConfigureAwait(false);

        int runs = 0;
        while (true)
        {
            await _scheduler.TickAsync(cancellationToken).ConfigureAwait(false);

            // One job at a time, so each run sees the results of the previous one.
            AcquireRequest request = new AcquireRequest(_queues, 1, Owner, Time.GetUtcNow(), _options.LeaseDuration);
            IReadOnlyList<JobRecord> jobs = await Storage.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
            if (jobs.Count == 0)
            {
                return runs;
            }

            await _executor.ExecuteAsync(jobs[0], Owner, CancellationToken.None, CancellationToken.None).ConfigureAwait(false);

            runs++;
            if (runs == MaxRunsPerCall)
            {
                throw new InvalidOperationException($"More than {MaxRunsPerCall} job runs in one call; a job may be rescheduling itself forever.");
            }
        }
    }

    /// <summary>Moves the fake clock forward, then runs the jobs that became due. Returns the number of runs.</summary>
    public async Task<int> AdvanceAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        // A real server saves recurring jobs at startup, before any time passes.
        await SyncRecurringJobsAsync(cancellationToken).ConfigureAwait(false);

        Time.Advance(duration);
        return await RunDueJobsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a stored job, or throws when it does not exist.</summary>
    public async Task<JobRecord> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        JobRecord? job = await Storage.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            throw new InvalidOperationException($"Job '{jobId}' does not exist.");
        }

        return job;
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private async Task SyncRecurringJobsAsync(CancellationToken cancellationToken)
    {
        if (_recurringJobsSynced)
        {
            return;
        }

        await _scheduler.SyncDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        _recurringJobsSynced = true;
    }
}
