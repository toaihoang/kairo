namespace Kairo.Storage;

/// <summary>
/// A stored job. Immutable: storage providers return new instances for every change.
/// </summary>
public sealed record JobRecord
{
    public required string Id { get; init; }

    /// <summary>The registered job name, used to find the job type when it runs.</summary>
    public required string Name { get; init; }

    public required string Queue { get; init; }

    /// <summary>The payload as JSON, or <see langword="null"/> for jobs without a payload.</summary>
    public string? Payload { get; init; }

    public required JobState State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The earliest time the job may run.</summary>
    public required DateTimeOffset DueAt { get; init; }

    /// <summary>The number of attempts started so far.</summary>
    public int Attempt { get; init; }

    public required int MaxAttempts { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? RecurringJobName { get; init; }

    public string? LeaseOwner { get; init; }

    public DateTimeOffset? LeaseExpiresAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public JobError? LastError { get; init; }
}
