using System.Globalization;

namespace Kairo;

/// <summary>
/// Describes when a recurring job runs. Every helper produces a cron expression,
/// so <c>DailyAt(2, 30)</c> is the same as <c>Cron("30 2 * * *")</c>.
/// </summary>
public sealed class RecurringSchedule
{
    internal string? CronExpression { get; private set; }

    internal string? TimeZoneId { get; private set; }

    internal string? Queue { get; private set; }

    internal Misfire MisfirePolicy { get; private set; } = Misfire.FireOnce;

    internal bool PreventOverlapping { get; private set; }

    /// <summary>
    /// A cron expression: 5 fields (minute hour day month weekday), or 6 fields with seconds first.
    /// Macros such as <c>@daily</c> are supported.
    /// </summary>
    public RecurringSchedule Cron(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        CronExpression = expression.Trim();
        return this;
    }

    /// <summary>Every <paramref name="seconds"/> seconds, counted from the start of each minute.</summary>
    public RecurringSchedule EverySeconds(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, 59);
        return Cron(Format("*/{0} * * * * *", seconds));
    }

    public RecurringSchedule EveryMinute() => Cron("* * * * *");

    /// <summary>Every <paramref name="minutes"/> minutes, counted from the start of each hour.</summary>
    public RecurringSchedule EveryMinutes(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, 59);
        return Cron(Format("*/{0} * * * *", minutes));
    }

    public RecurringSchedule Hourly() => HourlyAt(0);

    public RecurringSchedule HourlyAt(int minute)
    {
        ThrowIfInvalidMinute(minute);
        return Cron(Format("{0} * * * *", minute));
    }

    public RecurringSchedule Daily() => DailyAt(0);

    public RecurringSchedule DailyAt(int hour, int minute = 0)
    {
        ThrowIfInvalidHour(hour);
        ThrowIfInvalidMinute(minute);
        return Cron(Format("{0} {1} * * *", minute, hour));
    }

    public RecurringSchedule Weekly(DayOfWeek day, int hour = 0, int minute = 0)
    {
        ThrowIfInvalidHour(hour);
        ThrowIfInvalidMinute(minute);
        int cronDayOfWeek = (int)day; // Sunday = 0, as in cron.
        return Cron(Format("{0} {1} * * {2}", minute, hour, cronDayOfWeek));
    }

    /// <summary>On <paramref name="day"/> of every month. Months without that day are skipped.</summary>
    public RecurringSchedule Monthly(int day, int hour = 0, int minute = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 31);
        ThrowIfInvalidHour(hour);
        ThrowIfInvalidMinute(minute);
        return Cron(Format("{0} {1} {2} * *", minute, hour, day));
    }

    /// <summary>Evaluates the schedule in a time zone (IANA or Windows id). The default is UTC.</summary>
    public RecurringSchedule InTimeZone(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        TimeZoneId = timeZoneId;
        return this;
    }

    public RecurringSchedule InTimeZone(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return InTimeZone(timeZone.Id);
    }

    public RecurringSchedule OnQueue(string queue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        Queue = queue;
        return this;
    }

    public RecurringSchedule OnMisfire(Misfire misfire)
    {
        MisfirePolicy = misfire;
        return this;
    }

    /// <summary>Skips an occurrence while the job of the previous occurrence has not finished.</summary>
    public RecurringSchedule PreventOverlap()
    {
        PreventOverlapping = true;
        return this;
    }

    private static string Format(string format, params object[] values)
    {
        return string.Format(CultureInfo.InvariantCulture, format, values);
    }

    private static void ThrowIfInvalidHour(int hour)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hour, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
    }

    private static void ThrowIfInvalidMinute(int minute)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minute, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);
    }
}
