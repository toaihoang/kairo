using Kairo.Internal;

namespace Kairo.Core.Tests;

public class ScheduleAndBackoffTests
{
    // Each row: a schedule helper call, and the cron expression it must produce.
    public static TheoryData<Func<RecurringSchedule, RecurringSchedule>, string> Helpers => new TheoryData<Func<RecurringSchedule, RecurringSchedule>, string>
    {
        { schedule => schedule.EverySeconds(15), "*/15 * * * * *" },
        { schedule => schedule.EveryMinute(), "* * * * *" },
        { schedule => schedule.EveryMinutes(5), "*/5 * * * *" },
        { schedule => schedule.Hourly(), "0 * * * *" },
        { schedule => schedule.HourlyAt(45), "45 * * * *" },
        { schedule => schedule.Daily(), "0 0 * * *" },
        { schedule => schedule.DailyAt(2, 30), "30 2 * * *" },
        { schedule => schedule.Weekly(DayOfWeek.Monday, 8), "0 8 * * 1" },
        { schedule => schedule.Monthly(15, 6, 5), "5 6 15 * *" },
    };

    [Theory]
    [MemberData(nameof(Helpers))]
    public void ScheduleHelpers_ProduceCronExpressions(Func<RecurringSchedule, RecurringSchedule> configure, string expected)
    {
        RecurringSchedule schedule = configure(new RecurringSchedule());

        Assert.Equal(expected, schedule.CronExpression);
        CronSchedule.Validate(expected, timeZoneId: null);
    }

    [Fact]
    public void ScheduleHelpers_RejectOutOfRangeValues()
    {
        RecurringSchedule schedule = new RecurringSchedule();

        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.EverySeconds(60));
        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.DailyAt(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.HourlyAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.Monthly(32));
    }

    [Fact]
    public void CronWithSeconds_FiresWithinTheMinute()
    {
        CronSchedule schedule = CronSchedule.Parse("*/15 * * * * *", timeZoneId: null);
        DateTimeOffset start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(start.AddSeconds(15), schedule.GetNextOccurrence(start));
    }

    [Fact]
    public void FixedBackoff_ReturnsTheSameDelay()
    {
        Backoff backoff = Backoff.Fixed(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), backoff.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(10), backoff.GetDelay(7));
    }

    [Fact]
    public void ExponentialBackoff_DoublesUpToTheCap()
    {
        Backoff backoff = Backoff.Exponential(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), jitter: false);

        Assert.Equal(TimeSpan.FromSeconds(15), backoff.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(30), backoff.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(60), backoff.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(60), backoff.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(60), backoff.GetDelay(1000));
    }

    [Fact]
    public void ExponentialBackoff_WithJitter_StaysWithin20Percent()
    {
        Backoff backoff = Backoff.Exponential(TimeSpan.FromSeconds(10), TimeSpan.FromHours(1));

        for (int i = 0; i < 1000; i++)
        {
            TimeSpan delay = backoff.GetDelay(2);
            Assert.InRange(delay, TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(24));
        }
    }
}
