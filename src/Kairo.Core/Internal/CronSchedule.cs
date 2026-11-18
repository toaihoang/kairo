using Cronos;

namespace Kairo.Internal;

/// <summary>
/// A parsed cron expression bound to a time zone. Daylight saving time is handled by Cronos:
/// a skipped local time runs at the next valid time, and a repeated local time runs once.
/// </summary>
internal sealed class CronSchedule
{
    private const int FieldCountWithSeconds = 6;

    private readonly CronExpression _expression;
    private readonly TimeZoneInfo _timeZone;

    private CronSchedule(CronExpression expression, TimeZoneInfo timeZone)
    {
        _expression = expression;
        _timeZone = timeZone;
    }

    /// <exception cref="ArgumentException">The expression or the time zone is invalid.</exception>
    public static CronSchedule Parse(string expression, string? timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        CronExpression cron = ParseExpression(expression);
        TimeZoneInfo timeZone = FindTimeZone(timeZoneId);
        return new CronSchedule(cron, timeZone);
    }

    /// <summary>Throws <see cref="ArgumentException"/> when the expression or the time zone is invalid.</summary>
    public static void Validate(string expression, string? timeZoneId)
    {
        Parse(expression, timeZoneId);
    }

    /// <summary>Returns the first occurrence strictly after <paramref name="from"/>, or null if there is none.</summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from)
    {
        return _expression.GetNextOccurrence(from, _timeZone, inclusive: false);
    }

    private static CronExpression ParseExpression(string expression)
    {
        CronFormat format = CronFormat.Standard;
        if (CountFields(expression) == FieldCountWithSeconds)
        {
            format = CronFormat.IncludeSeconds;
        }

        try
        {
            return CronExpression.Parse(expression, format);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException($"Invalid cron expression '{expression}': {ex.Message}", nameof(expression), ex);
        }
    }

    private static int CountFields(string expression)
    {
        string[] fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fields.Length;
    }

    private static TimeZoneInfo FindTimeZone(string? timeZoneId)
    {
        if (timeZoneId is null)
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException($"Unknown time zone '{timeZoneId}'.", nameof(timeZoneId), ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException($"Invalid time zone data for '{timeZoneId}'.", nameof(timeZoneId), ex);
        }
    }
}
