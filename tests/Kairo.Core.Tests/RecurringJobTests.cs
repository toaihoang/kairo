using Kairo.Storage;
using Kairo.Storage.InMemory;
using Kairo.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Core.Tests;

public class RecurringJobTests
{
    private static readonly DateTimeOffset s_midnight = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly JobProbe _probe = new JobProbe();

    [Fact]
    public async Task RecurringJob_RunsOnEveryOccurrence()
    {
        await using KairoTestHost host = CreateHost(
            kairo => kairo.AddRecurringJob<RecordingJob>("tick", schedule => schedule.EveryMinute()),
            s_midnight.AddSeconds(30));

        Assert.Equal(0, await host.RunDueJobsAsync());
        Assert.Equal(1, await host.AdvanceAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, await host.AdvanceAsync(TimeSpan.FromMinutes(1)));

        DateTimeOffset[] expectedTimes = new[] { s_midnight.AddMinutes(1), s_midnight.AddMinutes(2) };
        Assert.Equal(expectedTimes, _probe.Runs.Select(run => run.ScheduledAt));
        Assert.All(_probe.Runs, run => Assert.Equal("tick", run.RecurringJobName));
    }

    [Fact]
    public async Task RecurringJob_WithPayload_PassesItEveryTime()
    {
        await using KairoTestHost host = CreateHost(
            kairo => kairo.AddRecurringJob<GreetingJob, Greeting>("greet", new Greeting("Kairo"), schedule => schedule.EveryMinute()),
            s_midnight);

        await host.AdvanceAsync(TimeSpan.FromMinutes(1));
        await host.AdvanceAsync(TimeSpan.FromMinutes(1));

        Greeting[] expectedPayloads = new[] { new Greeting("Kairo"), new Greeting("Kairo") };
        Assert.Equal(expectedPayloads, _probe.Payloads);
    }

    [Theory]
    [InlineData(Misfire.FireOnce, 1)]
    [InlineData(Misfire.FireAll, 5)]
    [InlineData(Misfire.Skip, 0)]
    public async Task MissedOccurrences_FollowTheMisfirePolicy(Misfire misfire, int expectedRuns)
    {
        await using KairoTestHost host = CreateHost(
            kairo => kairo
                .AddRecurringJob<RecordingJob>("tick", schedule => schedule.EveryMinute().OnMisfire(misfire))
                .Configure(options => options.MisfireThreshold = TimeSpan.FromSeconds(10)),
            s_midnight);
        await host.RunDueJobsAsync();

        // Occurrences 00:01 to 00:05 are missed; the latest is 30 s old, beyond the 10 s threshold.
        int runs = await host.AdvanceAsync(TimeSpan.FromMinutes(5.5));

        Assert.Equal(expectedRuns, runs);
        RecurringJobRecord record = await GetRecurringJobAsync(host.Storage, "tick");
        Assert.Equal(s_midnight.AddMinutes(6), record.NextRunAt);
    }

    [Fact]
    public async Task Skip_StillRunsAnOccurrenceWithinTheThreshold()
    {
        await using KairoTestHost host = CreateHost(
            kairo => kairo.AddRecurringJob<RecordingJob>("tick", schedule => schedule.EveryMinute().OnMisfire(Misfire.Skip)),
            s_midnight);
        await host.RunDueJobsAsync();

        // The latest occurrence (00:05) is 30 s old, within the default threshold of 1 minute.
        Assert.Equal(1, await host.AdvanceAsync(TimeSpan.FromMinutes(5.5)));
        Assert.Equal(s_midnight.AddMinutes(5), Assert.Single(_probe.Runs).ScheduledAt);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task PreventOverlap_SkipsWhileThePreviousJobIsUnfinished(bool preventOverlap, int expectedRunsOfSecondOccurrence)
    {
        await using KairoTestHost host = CreateHost(
            kairo => kairo
                .AddJob<FailingJob>(configure: options => options.Backoff = Backoff.Fixed(TimeSpan.FromHours(1)))
                .AddRecurringJob<FailingJob>("tick", schedule =>
                {
                    schedule.EveryMinute();
                    if (preventOverlap)
                    {
                        schedule.PreventOverlap();
                    }
                }),
            s_midnight);
        await host.RunDueJobsAsync();

        // The first occurrence fails and waits an hour for its retry, so it is unfinished.
        Assert.Equal(1, await host.AdvanceAsync(TimeSpan.FromMinutes(1)));
        Assert.Equal(expectedRunsOfSecondOccurrence, await host.AdvanceAsync(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Schedule_IsEvaluatedInItsTimeZone()
    {
        await using KairoTestHost host = CreateHost(
            kairo => kairo.AddRecurringJob<RecordingJob>("nightly", schedule => schedule.DailyAt(2, 0).InTimeZone("Asia/Ho_Chi_Minh")),
            s_midnight);

        await host.RunDueJobsAsync();

        // 02:00 in Ho Chi Minh City (UTC+7) is 19:00 UTC the day before.
        RecurringJobRecord record = await GetRecurringJobAsync(host.Storage, "nightly");
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 19, 0, 0, TimeSpan.Zero), record.NextRunAt);
    }

    [Fact]
    public async Task Restart_KeepsNextRunAt_WhenTheScheduleIsUnchanged()
    {
        InMemoryJobStorage storage = new InMemoryJobStorage();

        await using (KairoTestHost first = CreateHost(SharedStorage(storage, schedule => schedule.EveryMinutes(5)), s_midnight))
        {
            await first.RunDueJobsAsync();
        }

        // Down from 00:00 to 00:07: the kept NextRunAt (00:05) makes the missed occurrence run.
        await using KairoTestHost restarted = CreateHost(SharedStorage(storage, schedule => schedule.EveryMinutes(5)), s_midnight.AddMinutes(7));

        Assert.Equal(1, await restarted.RunDueJobsAsync());
        Assert.Equal(s_midnight.AddMinutes(5), Assert.Single(_probe.Runs).ScheduledAt);
        RecurringJobRecord record = await GetRecurringJobAsync(storage, "tick");
        Assert.Equal(s_midnight.AddMinutes(10), record.NextRunAt);
    }

    [Fact]
    public async Task Restart_RecomputesNextRunAt_WhenTheScheduleChanged()
    {
        InMemoryJobStorage storage = new InMemoryJobStorage();

        await using (KairoTestHost first = CreateHost(SharedStorage(storage, schedule => schedule.EveryMinutes(5)), s_midnight))
        {
            await first.RunDueJobsAsync();
        }

        await using KairoTestHost restarted = CreateHost(SharedStorage(storage, schedule => schedule.Hourly()), s_midnight.AddMinutes(3));
        await restarted.RunDueJobsAsync();

        RecurringJobRecord record = await GetRecurringJobAsync(storage, "tick");
        Assert.Equal(s_midnight.AddHours(1), record.NextRunAt);
        Assert.Equal("0 * * * *", record.Cron);
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("61 * * * *")]
    public void InvalidCron_FailsAtRegistration(string cron)
    {
        Assert.Throws<ArgumentException>(() => CreateHost(
            kairo => kairo.AddRecurringJob<RecordingJob>("bad", schedule => schedule.Cron(cron)),
            s_midnight));
    }

    [Fact]
    public void UnknownTimeZone_FailsAtRegistration()
    {
        Assert.Throws<ArgumentException>(() => CreateHost(
            kairo => kairo.AddRecurringJob<RecordingJob>("bad", schedule => schedule.Daily().InTimeZone("Mars/Olympus_Mons")),
            s_midnight));
    }

    private static Action<KairoBuilder> SharedStorage(InMemoryJobStorage storage, Action<RecurringSchedule> schedule)
    {
        return kairo => kairo.UseInMemoryStorage(storage).AddRecurringJob<RecordingJob>("tick", schedule);
    }

    private static async Task<RecurringJobRecord> GetRecurringJobAsync(IJobStorage storage, string name)
    {
        RecurringJobRecord? record = await storage.GetRecurringAsync(name, CancellationToken.None);
        Assert.NotNull(record);
        return record;
    }

    private KairoTestHost CreateHost(Action<KairoBuilder> configure, DateTimeOffset startTime)
    {
        return KairoTestHost.Create(configure, services => services.AddSingleton(_probe), startTime);
    }
}
