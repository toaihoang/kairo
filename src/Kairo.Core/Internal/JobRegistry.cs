namespace Kairo.Internal;

/// <summary>
/// The job types and recurring jobs registered in AddKairo. Read-only after startup.
/// </summary>
internal sealed class JobRegistry
{
    private readonly Dictionary<string, JobDescriptor> _jobsByName;
    private readonly Dictionary<Type, JobDescriptor> _jobsByType;
    private readonly Dictionary<string, RecurringJobDefinition> _recurringJobs;

    public JobRegistry(IEnumerable<JobDescriptor> jobs, IEnumerable<RecurringJobDefinition> recurringJobs)
    {
        _jobsByName = jobs.ToDictionary(job => job.Name, StringComparer.Ordinal);
        _jobsByType = _jobsByName.Values.ToDictionary(job => job.JobType);
        _recurringJobs = recurringJobs.ToDictionary(job => job.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<RecurringJobDefinition> RecurringJobs => _recurringJobs.Values;

    public JobDescriptor? FindJob(string name) => _jobsByName.GetValueOrDefault(name);

    public RecurringJobDefinition? FindRecurringJob(string name) => _recurringJobs.GetValueOrDefault(name);

    public JobDescriptor GetJob(Type jobType)
    {
        if (_jobsByType.TryGetValue(jobType, out JobDescriptor? job))
        {
            return job;
        }

        throw new InvalidOperationException(
            $"Job type '{jobType.FullName}' is not registered. Call AddJob<{jobType.Name}>() inside AddKairo(...).");
    }
}
