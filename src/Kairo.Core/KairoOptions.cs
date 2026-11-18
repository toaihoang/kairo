namespace Kairo;

/// <summary>
/// Server-wide settings. Bind them from the "Kairo" configuration section, or set them in code
/// with <see cref="KairoBuilder.Configure"/>.
/// </summary>
public sealed class KairoOptions
{
    public const string SectionName = "Kairo";

    public const string DefaultQueue = "default";

    /// <summary>The name of this server in leases and history. Defaults to machine name and process id.</summary>
    public string ServerName { get; set; } = Environment.MachineName + ":" + Environment.ProcessId;

    /// <summary>The queues this server processes, with their worker counts.</summary>
#pragma warning disable CA2227 // A settable collection lets configuration binding replace it.
    public Dictionary<string, QueueOptions> Queues { get; set; } = CreateDefaultQueues();
#pragma warning restore CA2227

    /// <summary>How often an idle worker looks for due jobs. Jobs enqueued in this process wake workers at once.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a worker owns a job before it must renew the lease. Renewal runs every third of this.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long shutdown waits for running jobs before cancelling them and giving them back.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>The longest time the recurring scheduler sleeps between checks.</summary>
    public TimeSpan RecurringPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>An occurrence older than this is "missed", and <see cref="Misfire"/> decides what happens to it.</summary>
    public TimeSpan MisfireThreshold { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The number of attempts for jobs that do not set <see cref="JobOptions.MaxAttempts"/>.</summary>
    public int DefaultMaxAttempts { get; set; } = 5;

    private static Dictionary<string, QueueOptions> CreateDefaultQueues()
    {
        Dictionary<string, QueueOptions> queues = new Dictionary<string, QueueOptions>(StringComparer.Ordinal);
        queues.Add(DefaultQueue, new QueueOptions());
        return queues;
    }
}

public sealed class QueueOptions
{
    /// <summary>The number of jobs from this queue that run at the same time on this server.</summary>
    public int WorkerCount { get; set; } = 10;
}
