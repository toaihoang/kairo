using System.Text.Json.Serialization;

namespace Kairo.Samples.MinimalApi;

public sealed record WelcomeEmail(string Address);

/// <summary>
/// Source-generated JSON metadata for job payloads. Required under Native AOT,
/// where reflection-based serialization is disabled.
/// </summary>
[JsonSerializable(typeof(WelcomeEmail))]
internal sealed partial class SampleJsonContext : JsonSerializerContext
{
}

/// <summary>Pretends to send an email. Fails on the first attempt for addresses starting with "retry".</summary>
internal sealed partial class SendWelcomeEmailJob : IJob<WelcomeEmail>
{
    private readonly ILogger<SendWelcomeEmailJob> _logger;

    public SendWelcomeEmailJob(ILogger<SendWelcomeEmailJob> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(WelcomeEmail payload, JobContext context, CancellationToken cancellationToken)
    {
        bool isFirstAttempt = context.Attempt == 1;
        bool shouldFailOnce = payload.Address.StartsWith("retry", StringComparison.OrdinalIgnoreCase);
        if (isFirstAttempt && shouldFailOnce)
        {
            throw new InvalidOperationException("The mail server is not responding.");
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        LogSent(payload.Address, context.Attempt);
    }

    [LoggerMessage(LogLevel.Information, "Sent the welcome email to {Address} on attempt {Attempt}.")]
    private partial void LogSent(string address, int attempt);
}

/// <summary>Registered by the source generator: see AddKairoJobs() in Program.cs.</summary>
[Job("heartbeat")]
internal sealed partial class HeartbeatJob : IJob
{
    private readonly ILogger<HeartbeatJob> _logger;

    public HeartbeatJob(ILogger<HeartbeatJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        LogHeartbeat(context.ScheduledAt);
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "Heartbeat scheduled for {ScheduledAt:O}.")]
    private partial void LogHeartbeat(DateTimeOffset scheduledAt);
}
