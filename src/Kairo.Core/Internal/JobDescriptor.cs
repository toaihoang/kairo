namespace Kairo.Internal;

/// <summary>
/// Runs a job: resolves it from the scope, deserializes the payload and calls ExecuteAsync.
/// Built from generic code at registration, so no reflection is needed at run time.
/// </summary>
internal delegate Task JobInvoker(IServiceProvider services, JobContext context, string? payload, CancellationToken cancellationToken);

internal sealed class JobDescriptor
{
    public JobDescriptor(string name, Type jobType, JobOptions options, JobInvoker invoker)
    {
        Name = name;
        JobType = jobType;
        Options = options;
        Invoker = invoker;
    }

    public string Name { get; }

    public Type JobType { get; }

    public JobOptions Options { get; }

    public JobInvoker Invoker { get; }
}

internal sealed class RecurringJobDefinition
{
    public RecurringJobDefinition(
        string name,
        JobDescriptor job,
        RecurringSchedule schedule,
        Func<KairoSerializer, string?> payloadFactory)
    {
        Name = name;
        Job = job;
        Schedule = schedule;
        PayloadFactory = payloadFactory;
    }

    public string Name { get; }

    public JobDescriptor Job { get; }

    public RecurringSchedule Schedule { get; }

    /// <summary>Serializes the payload once the serializer, with all registered resolvers, exists.</summary>
    public Func<KairoSerializer, string?> PayloadFactory { get; }
}
