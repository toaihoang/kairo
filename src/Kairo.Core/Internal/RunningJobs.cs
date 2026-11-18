using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Kairo.Internal;

/// <summary>
/// The jobs running on this server, shared by the queue workers, the lease renewer and shutdown.
/// </summary>
internal sealed class RunningJobs
{
    private readonly ConcurrentDictionary<string, RunningJob> _jobs = new ConcurrentDictionary<string, RunningJob>(StringComparer.Ordinal);

    public int Count => _jobs.Count;

    public ICollection<string> JobIds => _jobs.Keys;

    public RunningJob Add(string jobId)
    {
        RunningJob job = new RunningJob();
        _jobs[jobId] = job;
        return job;
    }

    public void Complete(string jobId)
    {
        if (_jobs.TryRemove(jobId, out RunningJob? job))
        {
            job.MarkCompleted();
        }
    }

    /// <summary>Tells a running job that its lease was lost, which cancels it.</summary>
    public void SignalLeaseLost(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out RunningJob? job))
        {
            job.SignalLeaseLost();
        }
    }

    /// <summary>Completes when every job running now has finished.</summary>
    public Task WhenAllCompleted()
    {
        List<Task> completions = new List<Task>();
        foreach (RunningJob job in _jobs.Values)
        {
            completions.Add(job.Completion);
        }

        return Task.WhenAll(completions);
    }
}

[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The token source has no timer, so it holds nothing to release. Disposing it could race with SignalLeaseLost.")]
internal sealed class RunningJob
{
    private readonly CancellationTokenSource _leaseLost = new CancellationTokenSource();
    private readonly TaskCompletionSource _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationToken LeaseLost => _leaseLost.Token;

    public Task Completion => _completion.Task;

    public void SignalLeaseLost()
    {
        _leaseLost.Cancel();
    }

    public void MarkCompleted()
    {
        _completion.TrySetResult();
    }
}
