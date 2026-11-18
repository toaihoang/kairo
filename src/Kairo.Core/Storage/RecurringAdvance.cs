namespace Kairo.Storage;

/// <summary>
/// Parameters of <see cref="IJobStorage.TryAdvanceRecurringAsync"/>.
/// </summary>
/// <param name="Name">The recurring job name.</param>
/// <param name="ExpectedNextRunAt">The <c>NextRunAt</c> value the scheduler read.</param>
/// <param name="NextRunAt">The new <c>NextRunAt</c> value.</param>
/// <param name="Jobs">The occurrence jobs to create; may be empty when occurrences are skipped.</param>
/// <param name="Now">The current time.</param>
public sealed record RecurringAdvance(
    string Name,
    DateTimeOffset? ExpectedNextRunAt,
    DateTimeOffset? NextRunAt,
    IReadOnlyList<JobRecord> Jobs,
    DateTimeOffset Now);
