namespace Kairo.Storage;

/// <summary>
/// The error of a failed attempt.
/// </summary>
/// <param name="Type">The exception type name.</param>
/// <param name="Message">The exception message.</param>
/// <param name="Details">The full exception text, including inner exceptions and stack traces.</param>
public sealed record JobError(string Type, string Message, string? Details)
{
    private const int MaxDetailsLength = 8000;

    public static JobError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Type exceptionType = exception.GetType();
        string typeName = exceptionType.FullName ?? exceptionType.Name;

        string details = exception.ToString();
        if (details.Length > MaxDetailsLength)
        {
            details = details.Substring(0, MaxDetailsLength);
        }

        return new JobError(typeName, exception.Message, details);
    }
}
