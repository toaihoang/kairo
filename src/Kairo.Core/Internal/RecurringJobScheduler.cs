using Kairo.Storage;
using Microsoft.Extensions.Logging;

namespace Kairo.Internal;

/// <summary>
/// Turns due occurrences of recurring jobs into normal jobs.
/// </summary>
/// <remarks>
/// Safe with several schedulers on one storage: each tick is a compare-and-set on NextRunAt,
/// and each occurrence job has the idempotency key "recurring:{name}:{occurrence}",
/// so an occurrence is never created twice.
/// </remarks>
internal sealed class RecurringJobScheduler
{
    private const int MaxOccurrencesPerTick = 100;

    private readonly IJobStorage _storage;
    private readonly JobRegistry _registry;
    private readonly JobFactory _jobFactory;
    private readonly KairoSerializer _serializer;
    private readonly KairoServerContext _server;
    private readonly JobSignal _signal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecurringJobScheduler> _logger;

    // Parsed schedules, reused between ticks.
    private readonly Dictionary<ScheduleKey, CronSchedule> _schedules = new Dictionary<ScheduleKey, CronSchedule>();
    private readonly Lock _schedulesLock = new Lock();

    public RecurringJobScheduler(
        IJobStorage storage,
        JobRegistry registry,
        JobFactory jobFactory,
        KairoSerializer serializer,
        KairoServerContext server,
        JobSignal signal,
        TimeProvider timeProvider,
        ILogger<RecurringJobScheduler> logger)
    {
        _storage = storage;
        _registry = registry;
        _jobFactory = jobFactory;
        _serializer = serializer;
        _server = server;
        _signal = signal;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (_registry.RecurringJobs.Count == 0)
        {
            return;
        }

        bool definitionsSaved = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = _server.Options.RecurringPollInterval;
            try
            {
                if (!definitionsSaved)
                {
                    await SyncDefinitionsAsync(stoppingToken).ConfigureAwait(false);
                    definitionsSaved = true;
                }

                DateTimeOffset? nextRunAt = await TickAsync(stoppingToken).ConfigureAwait(false);
                delay = GetDelayUntil(nextRunAt);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Keep scheduling; a storage outage is retried on the next tick.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.RecurringTickFailed(_logger, ex);
            }

            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Saves the recurring jobs declared in code. A job keeps its NextRunAt and pause state
    /// unless its schedule changed.
    /// </summary>
    public async Task SyncDefinitionsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (RecurringJobDefinition definition in _registry.RecurringJobs)
        {
            RecurringJobRecord? existing = await _storage.GetRecurringAsync(definition.Name, cancellationToken).ConfigureAwait(false);
            RecurringJobRecord record = CreateRecord(definition, existing, now);
            await _storage.SaveRecurringAsync(record, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Creates the jobs of all due occurrences and returns the earliest next occurrence.</summary>
    public async Task<DateTimeOffset?> TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset? earliestNextRunAt = null;

        IReadOnlyList<RecurringJobRecord> records = await _storage.GetRecurringJobsAsync(cancellationToken).ConfigureAwait(false);
        foreach (RecurringJobRecord record in records)
        {
            // Only this app's declared jobs: another app on the same storage may own the rest.
            RecurringJobDefinition? definition = _registry.FindRecurringJob(record.Name);
            if (definition is null)
            {
                continue;
            }

            if (record.IsPaused)
            {
                continue;
            }

            if (!record.NextRunAt.HasValue)
            {
                continue;
            }

            DateTimeOffset? nextRunAt = record.NextRunAt;
            if (record.NextRunAt.Value <= now)
            {
                nextRunAt = await FireAsync(record, record.NextRunAt.Value, definition, now, cancellationToken).ConfigureAwait(false);
            }

            earliestNextRunAt = Earliest(earliestNextRunAt, nextRunAt);
        }

        return earliestNextRunAt;
    }

    private RecurringJobRecord CreateRecord(RecurringJobDefinition definition, RecurringJobRecord? existing, DateTimeOffset now)
    {
        RecurringSchedule schedule = definition.Schedule;
        string cron = schedule.CronExpression!; // Checked when the job was registered.

        DateTimeOffset? nextRunAt;
        bool isPaused = false;
        string? lastJobId = null;

        if (existing is null)
        {
            nextRunAt = GetSchedule(cron, schedule.TimeZoneId).GetNextOccurrence(now);
        }
        else
        {
            isPaused = existing.IsPaused;
            lastJobId = existing.LastJobId;

            bool scheduleChanged = existing.Cron != cron || existing.TimeZoneId != schedule.TimeZoneId;
            if (scheduleChanged)
            {
                nextRunAt = GetSchedule(cron, schedule.TimeZoneId).GetNextOccurrence(now);
            }
            else
            {
                nextRunAt = existing.NextRunAt;
            }
        }

        return new RecurringJobRecord
        {
            Name = definition.Name,
            JobName = definition.Job.Name,
            Cron = cron,
            TimeZoneId = schedule.TimeZoneId,
            Queue = schedule.Queue,
            Payload = definition.PayloadFactory(_serializer),
            Misfire = schedule.MisfirePolicy,
            PreventOverlap = schedule.PreventOverlapping,
            IsPaused = isPaused,
            NextRunAt = nextRunAt,
            LastJobId = lastJobId,
            UpdatedAt = now,
        };
    }

    /// <summary>Creates the jobs for the due occurrences and returns the new NextRunAt.</summary>
    private async Task<DateTimeOffset?> FireAsync(
        RecurringJobRecord record,
        DateTimeOffset firstDueOccurrence,
        RecurringJobDefinition definition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CronSchedule schedule;
        try
        {
            schedule = GetSchedule(record.Cron, record.TimeZoneId);
        }
        catch (ArgumentException ex)
        {
            Log.RecurringScheduleInvalid(_logger, ex, record.Name);
            return null;
        }

        DueOccurrences due = CollectDueOccurrences(schedule, firstDueOccurrence, now);
        List<DateTimeOffset> occurrencesToRun = SelectOccurrences(record, due.Occurrences, now);

        bool mustSkipForOverlap = await MustSkipForOverlapAsync(record, occurrencesToRun, cancellationToken).ConfigureAwait(false);
        if (mustSkipForOverlap)
        {
            occurrencesToRun.Clear();
        }

        List<JobRecord> jobs = new List<JobRecord>();
        foreach (DateTimeOffset occurrence in occurrencesToRun)
        {
            jobs.Add(CreateOccurrenceJob(record, definition, occurrence, now));
        }

        RecurringAdvance advance = new RecurringAdvance(record.Name, record.NextRunAt, due.NextRunAt, jobs, now);
        bool advanced = await _storage.TryAdvanceRecurringAsync(advance, cancellationToken).ConfigureAwait(false);
        if (advanced && jobs.Count > 0)
        {
            _signal.Notify();
        }

        return due.NextRunAt;
    }

    /// <summary>Returns the occurrences from <paramref name="first"/> up to now, and the first one after now.</summary>
    private static DueOccurrences CollectDueOccurrences(CronSchedule schedule, DateTimeOffset first, DateTimeOffset now)
    {
        DueOccurrences result = new DueOccurrences();
        DateTimeOffset? occurrence = first;

        while (occurrence.HasValue && occurrence.Value <= now)
        {
            if (result.Occurrences.Count == MaxOccurrencesPerTick)
            {
                // Far behind: stop collecting and continue from now.
                result.NextRunAt = schedule.GetNextOccurrence(now);
                return result;
            }

            result.Occurrences.Add(occurrence.Value);
            occurrence = schedule.GetNextOccurrence(occurrence.Value);
        }

        result.NextRunAt = occurrence;
        return result;
    }

    private List<DateTimeOffset> SelectOccurrences(RecurringJobRecord record, List<DateTimeOffset> due, DateTimeOffset now)
    {
        DateTimeOffset latest = due[due.Count - 1];
        List<DateTimeOffset> selected = new List<DateTimeOffset>();

        switch (record.Misfire)
        {
            case Misfire.FireAll:
                selected.AddRange(due);
                break;

            case Misfire.Skip:
                bool latestIsOnTime = now - latest <= _server.Options.MisfireThreshold;
                if (latestIsOnTime)
                {
                    selected.Add(latest);
                }

                int skippedCount = due.Count - selected.Count;
                if (skippedCount > 0)
                {
                    Log.RecurringMisfireSkipped(_logger, record.Name, skippedCount);
                }

                break;

            default: // Misfire.FireOnce
                selected.Add(latest);
                break;
        }

        return selected;
    }

    /// <summary>True when the job prevents overlap and the job of its previous occurrence has not finished.</summary>
    private async Task<bool> MustSkipForOverlapAsync(
        RecurringJobRecord record,
        List<DateTimeOffset> occurrencesToRun,
        CancellationToken cancellationToken)
    {
        if (!record.PreventOverlap)
        {
            return false;
        }

        if (occurrencesToRun.Count == 0)
        {
            return false;
        }

        if (record.LastJobId is null)
        {
            return false;
        }

        JobRecord? previousJob = await _storage.GetAsync(record.LastJobId, cancellationToken).ConfigureAwait(false);
        if (previousJob is null)
        {
            return false;
        }

        if (previousJob.State.IsTerminal())
        {
            return false;
        }

        DateTimeOffset skippedOccurrence = occurrencesToRun[occurrencesToRun.Count - 1];
        Log.RecurringOverlapSkipped(_logger, record.Name, skippedOccurrence, previousJob.Id);
        return true;
    }

    private JobRecord CreateOccurrenceJob(
        RecurringJobRecord record,
        RecurringJobDefinition definition,
        DateTimeOffset occurrence,
        DateTimeOffset now)
    {
        // The same key for the same occurrence, so a second scheduler cannot create it again.
        string idempotencyKey = "recurring:" + record.Name + ":" + occurrence.UtcTicks;

        return _jobFactory.Create(
            definition.Job,
            record.Payload,
            dueAt: occurrence,
            now,
            queue: record.Queue,
            idempotencyKey: idempotencyKey,
            recurringJobName: record.Name);
    }

    /// <summary>The time until <paramref name="nextRunAt"/>, but never longer than RecurringPollInterval.</summary>
    private TimeSpan GetDelayUntil(DateTimeOffset? nextRunAt)
    {
        TimeSpan maxDelay = _server.Options.RecurringPollInterval;
        if (!nextRunAt.HasValue)
        {
            return maxDelay;
        }

        TimeSpan untilNextRun = nextRunAt.Value - _timeProvider.GetUtcNow();
        if (untilNextRun < maxDelay)
        {
            return untilNextRun;
        }

        return maxDelay;
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (!first.HasValue)
        {
            return second;
        }

        if (!second.HasValue)
        {
            return first;
        }

        if (second.Value < first.Value)
        {
            return second;
        }

        return first;
    }

    private CronSchedule GetSchedule(string cron, string? timeZoneId)
    {
        ScheduleKey key = new ScheduleKey(cron, timeZoneId);
        lock (_schedulesLock)
        {
            if (_schedules.TryGetValue(key, out CronSchedule? cached))
            {
                return cached;
            }

            CronSchedule schedule = CronSchedule.Parse(cron, timeZoneId);
            _schedules.Add(key, schedule);
            return schedule;
        }
    }

    private sealed record ScheduleKey(string Cron, string? TimeZoneId);

    private sealed class DueOccurrences
    {
        /// <summary>The occurrences at or before now, oldest first. Never empty.</summary>
        public List<DateTimeOffset> Occurrences { get; } = new List<DateTimeOffset>();

        /// <summary>The first occurrence after now, or null when the schedule never fires again.</summary>
        public DateTimeOffset? NextRunAt { get; set; }
    }
}
