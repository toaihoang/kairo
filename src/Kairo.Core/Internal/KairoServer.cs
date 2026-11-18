using Kairo.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Internal;

/// <summary>
/// The hosted service that runs the queue workers, the lease renewer and the recurring scheduler.
/// </summary>
/// <remarks>
/// Shutdown order: stop fetching, wait up to ShutdownTimeout for running jobs, cancel the jobs
/// still running (they are given back to their queue), then stop renewing leases.
/// </remarks>
internal sealed class KairoServer : IHostedService, IDisposable
{
    private readonly KairoServerContext _server;
    private readonly IJobStorage _storage;
    private readonly JobExecutor _executor;
    private readonly RunningJobs _runningJobs;
    private readonly JobSignal _signal;
    private readonly LeaseRenewer _leaseRenewer;
    private readonly RecurringJobScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<KairoServer> _logger;

    private readonly CancellationTokenSource _stopFetching = new CancellationTokenSource();
    private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
    private readonly CancellationTokenSource _stopBackground = new CancellationTokenSource();
    private readonly List<Task> _workerTasks = new List<Task>();
    private readonly List<Task> _backgroundTasks = new List<Task>();

    public KairoServer(
        KairoServerContext server,
        IJobStorage storage,
        JobExecutor executor,
        RunningJobs runningJobs,
        JobSignal signal,
        LeaseRenewer leaseRenewer,
        RecurringJobScheduler scheduler,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _server = server;
        _storage = storage;
        _executor = executor;
        _runningJobs = runningJobs;
        _signal = signal;
        _leaseRenewer = leaseRenewer;
        _scheduler = scheduler;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<KairoServer>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ILogger workerLogger = _loggerFactory.CreateLogger<QueueWorker>();
        foreach (KeyValuePair<string, QueueOptions> queue in _server.Options.Queues)
        {
            QueueWorker worker = new QueueWorker(
                queue.Key,
                queue.Value.WorkerCount,
                _server,
                _storage,
                _executor,
                _runningJobs,
                _signal,
                _timeProvider,
                workerLogger);

            _workerTasks.Add(Task.Run(() => worker.RunAsync(_stopFetching.Token, _shutdown.Token), CancellationToken.None));
        }

        _backgroundTasks.Add(Task.Run(() => _leaseRenewer.RunAsync(_stopBackground.Token), CancellationToken.None));
        _backgroundTasks.Add(Task.Run(() => _scheduler.RunAsync(_stopFetching.Token), CancellationToken.None));

        string queueNames = string.Join(", ", _server.Options.Queues.Keys);
        Log.ServerStarted(_logger, _server.Options.ServerName, queueNames);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopFetching.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_workerTasks).ConfigureAwait(false);

        bool allJobsFinished = await WaitForRunningJobsAsync(cancellationToken).ConfigureAwait(false);
        if (!allJobsFinished)
        {
            await CancelRunningJobsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Leases are renewed until here, so jobs that were still finishing kept their leases.
        await _stopBackground.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_backgroundTasks).ConfigureAwait(false);

        Log.ServerStopped(_logger, _server.Options.ServerName);
    }

    public void Dispose()
    {
        _stopFetching.Dispose();
        _shutdown.Dispose();
        _stopBackground.Dispose();
    }

    /// <summary>Returns false when jobs are still running after ShutdownTimeout, or the host stopped waiting.</summary>
    private async Task<bool> WaitForRunningJobsAsync(CancellationToken hostCancellation)
    {
        try
        {
            await _runningJobs.WhenAllCompleted()
                .WaitAsync(_server.Options.ShutdownTimeout, _timeProvider, hostCancellation)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Cancels the running jobs; each one gives itself back to its queue.</summary>
    private async Task CancelRunningJobsAsync(CancellationToken hostCancellation)
    {
        Log.ShutdownTimedOut(_logger, _runningJobs.Count, _server.Options.ShutdownTimeout);
        await _shutdown.CancelAsync().ConfigureAwait(false);

        // Cancelled jobs need a moment to record that they were given back.
        try
        {
            await _runningJobs.WhenAllCompleted().WaitAsync(hostCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host stopped waiting; leases of jobs still running expire and other servers recover them.
        }
    }
}
