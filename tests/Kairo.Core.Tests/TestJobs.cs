namespace Kairo.Core.Tests;

public sealed record Greeting(string Name);

/// <summary>Records what the test jobs did. Registered as a singleton in each test.</summary>
public sealed class JobProbe
{
    private readonly Lock _lock = new Lock();
    private readonly List<JobContext> _runs = new List<JobContext>();
    private readonly List<Greeting> _payloads = new List<Greeting>();
    private readonly TaskCompletionSource _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _running;
    private int _maxRunning;

    /// <summary>FailingJob throws on every attempt up to and including this one.</summary>
    public int FailUntilAttempt { get; set; } = int.MaxValue;

    public TimeSpan SlowJobDuration { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Completes when the first job calls <see cref="Record"/>.</summary>
    public Task Started => _started.Task;

    public List<JobContext> Runs
    {
        get
        {
            lock (_lock)
            {
                return new List<JobContext>(_runs);
            }
        }
    }

    public List<Greeting> Payloads
    {
        get
        {
            lock (_lock)
            {
                return new List<Greeting>(_payloads);
            }
        }
    }

    /// <summary>The highest number of SlowJobs that ran at the same time.</summary>
    public int MaxRunning
    {
        get
        {
            lock (_lock)
            {
                return _maxRunning;
            }
        }
    }

    public void Record(JobContext context)
    {
        lock (_lock)
        {
            _runs.Add(context);
        }

        _started.TrySetResult();
    }

    public void RecordPayload(Greeting payload)
    {
        lock (_lock)
        {
            _payloads.Add(payload);
        }
    }

    public void EnterRunning()
    {
        lock (_lock)
        {
            _running++;
            if (_running > _maxRunning)
            {
                _maxRunning = _running;
            }
        }
    }

    public void ExitRunning()
    {
        lock (_lock)
        {
            _running--;
        }
    }

    public async Task WaitForRunsAsync(int count, TimeSpan timeout)
    {
        using CancellationTokenSource deadline = new CancellationTokenSource(timeout);
        while (Runs.Count < count)
        {
            await Task.Delay(10, deadline.Token);
        }
    }
}

public sealed class RecordingJob : IJob
{
    private readonly JobProbe _probe;

    public RecordingJob(JobProbe probe)
    {
        _probe = probe;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _probe.Record(context);
        return Task.CompletedTask;
    }
}

public sealed class GreetingJob : IJob<Greeting>
{
    private readonly JobProbe _probe;

    public GreetingJob(JobProbe probe)
    {
        _probe = probe;
    }

    public Task ExecuteAsync(Greeting payload, JobContext context, CancellationToken cancellationToken)
    {
        _probe.Record(context);
        _probe.RecordPayload(payload);
        return Task.CompletedTask;
    }
}

public sealed class FailingJob : IJob
{
    private readonly JobProbe _probe;

    public FailingJob(JobProbe probe)
    {
        _probe = probe;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _probe.Record(context);
        if (context.Attempt <= _probe.FailUntilAttempt)
        {
            throw new InvalidOperationException("boom");
        }

        return Task.CompletedTask;
    }
}

/// <summary>Runs until it is cancelled.</summary>
public sealed class HangingJob : IJob
{
    private readonly JobProbe _probe;

    public HangingJob(JobProbe probe)
    {
        _probe = probe;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _probe.Record(context);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

/// <summary>Takes <see cref="JobProbe.SlowJobDuration"/> of real time and tracks how many run at once.</summary>
public sealed class SlowJob : IJob
{
    private readonly JobProbe _probe;

    public SlowJob(JobProbe probe)
    {
        _probe = probe;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _probe.EnterRunning();
        try
        {
            await Task.Delay(_probe.SlowJobDuration, cancellationToken);
        }
        finally
        {
            _probe.ExitRunning();
        }

        _probe.Record(context);
    }
}
