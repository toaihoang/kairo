using Microsoft.Extensions.Options;

namespace Kairo.Internal;

internal sealed class KairoOptionsValidator : IValidateOptions<KairoOptions>
{
    private static readonly TimeSpan s_minLeaseDuration = TimeSpan.FromSeconds(1);

    public ValidateOptionsResult Validate(string? name, KairoOptions options)
    {
        List<string> errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServerName))
        {
            errors.Add("Kairo:ServerName must not be empty.");
        }

        ValidateQueues(errors, options.Queues);

        AddErrorIfNotPositive(errors, options.PollInterval, nameof(KairoOptions.PollInterval));
        AddErrorIfNotPositive(errors, options.RecurringPollInterval, nameof(KairoOptions.RecurringPollInterval));
        AddErrorIfNotPositive(errors, options.MisfireThreshold, nameof(KairoOptions.MisfireThreshold));

        if (options.LeaseDuration < s_minLeaseDuration)
        {
            errors.Add($"Kairo:LeaseDuration must be at least {s_minLeaseDuration}.");
        }

        if (options.ShutdownTimeout < TimeSpan.Zero)
        {
            errors.Add("Kairo:ShutdownTimeout must not be negative.");
        }

        if (options.DefaultMaxAttempts < 1)
        {
            errors.Add("Kairo:DefaultMaxAttempts must be at least 1.");
        }

        if (errors.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateQueues(List<string> errors, Dictionary<string, QueueOptions> queues)
    {
        if (queues.Count == 0)
        {
            errors.Add("Kairo:Queues must contain at least one queue.");
            return;
        }

        foreach (KeyValuePair<string, QueueOptions> queue in queues)
        {
            if (string.IsNullOrWhiteSpace(queue.Key))
            {
                errors.Add("Kairo:Queues contains an empty queue name.");
            }

            if (queue.Value.WorkerCount < 1)
            {
                errors.Add($"Kairo:Queues:{queue.Key}:WorkerCount must be at least 1.");
            }
        }
    }

    private static void AddErrorIfNotPositive(List<string> errors, TimeSpan value, string optionName)
    {
        if (value <= TimeSpan.Zero)
        {
            errors.Add($"Kairo:{optionName} must be greater than zero.");
        }
    }
}
