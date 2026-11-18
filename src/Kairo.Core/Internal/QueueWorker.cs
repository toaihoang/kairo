using Kairo.Storage;
using Microsoft.Extensions.Logging;

namespace Kairo.Internal;

/// <summary>
/// Fetches and runs the jobs of one queue. At most WorkerCount jobs run at the same time:
/// the worker only fetches as many jobs as it has free slots, so it never holds leases
/// on jobs it cannot start yet.
/// </summary>
internal sealed class QueueWorker
{
    private readonly string _queue;
    private readonly string[] _queues;
    private readonly int _workerCount;
    private readonly KairoServerContext _server;
    private readonly IJobStorage _storage;
    private readonly JobExecutor _executor;
    private readonly RunningJobs _runningJobs;
    private readonly JobSignal _signal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public QueueWorker(
        string queue,
        int workerCount,
        KairoServerContext server,
        IJobStorage storage,
        JobExecutor executor,
        RunningJobs runningJobs,
        JobSignal signal,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _queue = queue;
        _queues = new string[] { queue };
        _workerCount = workerCount;
        _server = server;
        _storage = storage;
        _executor = executor;
        _runningJobs = runningJobs;
        _signal = signal;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <param name="stopFetching">Stops taking new jobs. Running jobs continue.</param>
    /// <param name="shutdown">Cancels running jobs.</param>
    public async Task RunAsync(CancellationToken stopFetching, CancellationToken shutdown)
    {
        // One slot per worker. Not disposed: running jobs release their slot after this method returns.
        SemaphoreSlim freeSlots = new SemaphoreSlim(_workerCount, _workerCount);

        while (!stopFetching.IsCancellationRequested)
        {
            bool gotSlot = await WaitForFreeSlotAsync(freeSlots, stopFetching).ConfigureAwait(false);
            if (!gotSlot)
            {
                break;
            }

            // We hold one slot; take every other free slot too, to fetch a batch in one call.
            int slots = 1 + TakeAvailableSlots(freeSlots);

            // Taken before fetching, so a job created during the fetch still wakes this worker.
            Task newJobNotification = _signal.NextNotification;

            IReadOnlyList<JobRecord>? jobs = await TryAcquireAsync(slots, stopFetching).ConfigureAwait(false);
            if (jobs is null)
            {
                freeSlots.Release(slots);
                if (stopFetching.IsCancellationRequested)
                {
                    break;
                }

                await WaitForPollIntervalAsync(stopFetching).ConfigureAwait(false);
                continue;
            }

            int unusedSlots = slots - jobs.Count;
            if (unusedSlots > 0)
            {
                freeSlots.Release(unusedSlots);
            }

            foreach (JobRecord job in jobs)
            {
                StartJob(job, freeSlots, shutdown);
            }

            if (jobs.Count == 0)
            {
                await WaitForNewJobOrPollIntervalAsync(newJobNotification, stopFetching).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> WaitForFreeSlotAsync(SemaphoreSlim freeSlots, CancellationToken stopFetching)
    {
        try
        {
            await freeSlots.WaitAsync(stopFetching).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static int TakeAvailableSlots(SemaphoreSlim freeSlots)
    {
        int taken = 0;
        while (freeSlots.Wait(0))
        {
            taken++;
        }

        return taken;
    }

    /// <summary>Returns the acquired jobs, or null when fetching was stopped or the storage failed.</summary>
    private async Task<IReadOnlyList<JobRecord>?> TryAcquireAsync(int maxCount, CancellationToken stopFetching)
    {
        AcquireRequest request = new AcquireRequest(
            _queues,
            maxCount,
            _server.Owner,
            _timeProvider.GetUtcNow(),
            _server.Options.LeaseDuration);

        try
        {
            return await _storage.AcquireAsync(request, stopFetching).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopFetching.IsCancellationRequested)
        {
            return null;
        }
#pragma warning disable CA1031 // A storage outage must not stop the worker; it retries after the poll interval.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.AcquireFailed(_logger, ex, _queue);
            return null;
        }
    }

    private void StartJob(JobRecord job, SemaphoreSlim freeSlots, CancellationToken shutdown)
    {
        RunningJob runningJob = _runningJobs.Add(job.Id);

        // Task.Run keeps a job that blocks synchronously from blocking the fetch loop.
        // The task is not awaited here: RunningJobs tracks it, and shutdown waits for it.
        Task.Run(() => RunJobAsync(job, runningJob, freeSlots, shutdown), CancellationToken.None);
    }

    private async Task RunJobAsync(JobRecord job, RunningJob runningJob, SemaphoreSlim freeSlots, CancellationToken shutdown)
    {
        try
        {
            await _executor.ExecuteAsync(job, _server.Owner, runningJob.LeaseLost, shutdown).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The executor records job failures; this only catches storage errors.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.JobCrashed(_logger, ex, job.Id);
        }
        finally
        {
            _runningJobs.Complete(job.Id);
            freeSlots.Release();
        }
    }

    private async Task WaitForPollIntervalAsync(CancellationToken stopFetching)
    {
        try
        {
            await Task.Delay(_server.Options.PollInterval, _timeProvider, stopFetching).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping; the loop checks the token next.
        }
    }

    /// <summary>Waits until a new job is created in this process, the poll interval passes, or fetching stops.</summary>
    private async Task WaitForNewJobOrPollIntervalAsync(Task newJobNotification, CancellationToken stopFetching)
    {
        using CancellationTokenSource pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(stopFetching);
        Task poll = Task.Delay(_server.Options.PollInterval, _timeProvider, pollCancellation.Token);

        await Task.WhenAny(newJobNotification, poll).ConfigureAwait(false);

        // Stops the poll timer when the notification came first.
        await pollCancellation.CancelAsync().ConfigureAwait(false);
    }
}
