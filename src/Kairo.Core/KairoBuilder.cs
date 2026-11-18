using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Kairo.Internal;
using Kairo.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo;

/// <summary>
/// Registers jobs, recurring jobs, storage and settings inside <c>services.AddKairo(...)</c>.
/// </summary>
public sealed class KairoBuilder
{
    private readonly Dictionary<string, JobDescriptor> _jobs = new Dictionary<string, JobDescriptor>(StringComparer.Ordinal);
    private readonly Dictionary<string, RecurringJobDefinition> _recurringJobs = new Dictionary<string, RecurringJobDefinition>(StringComparer.Ordinal);
    private readonly List<IJsonTypeInfoResolver> _jsonResolvers = new List<IJsonTypeInfoResolver>();

    internal KairoBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    internal bool HasStorage { get; private set; }

    /// <summary>Changes server-wide settings. Applied after values bound from configuration.</summary>
    public KairoBuilder Configure(Action<KairoOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Registers a job type. <paramref name="name"/> is stored with every job, so keep it stable:
    /// renaming it orphans jobs that are already stored. It defaults to the class name.
    /// </summary>
    public KairoBuilder AddJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJob>(
        string? name = null,
        Action<JobOptions>? configure = null)
        where TJob : class, IJob
    {
        AddJobCore<TJob>(name, configure, InvokeJob<TJob>);
        return this;
    }

    /// <summary>Registers a job type that receives a <typeparamref name="TPayload"/> payload.</summary>
    /// <inheritdoc cref="AddJob{TJob}(string?, Action{JobOptions}?)" path="/remarks"/>
    public KairoBuilder AddJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJob, TPayload>(
        string? name = null,
        Action<JobOptions>? configure = null)
        where TJob : class, IJob<TPayload>
    {
        AddJobCore<TJob>(name, configure, InvokeJobWithPayload<TJob, TPayload>);
        return this;
    }

    /// <summary>
    /// Runs <typeparamref name="TJob"/> on a schedule. The job type is registered with default
    /// options if <see cref="AddJob{TJob}"/> was not called for it.
    /// </summary>
    /// <param name="name">A unique, stable name for the recurring job.</param>
    /// <param name="schedule">For example <c>s =&gt; s.DailyAt(2, 30).InTimeZone("Asia/Ho_Chi_Minh")</c>.</param>
    public KairoBuilder AddRecurringJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJob>(
        string name,
        Action<RecurringSchedule> schedule)
        where TJob : class, IJob
    {
        if (FindJob(typeof(TJob)) is null)
        {
            AddJob<TJob>();
        }

        JobDescriptor job = FindJob(typeof(TJob))!;
        AddRecurringJobCore(name, job, schedule, NoPayload);
        return this;
    }

    /// <summary>Runs <typeparamref name="TJob"/> on a schedule with the same payload every time.</summary>
    /// <inheritdoc cref="AddRecurringJob{TJob}(string, Action{RecurringSchedule})"/>
    public KairoBuilder AddRecurringJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJob, TPayload>(
        string name,
        TPayload payload,
        Action<RecurringSchedule> schedule)
        where TJob : class, IJob<TPayload>
    {
        if (FindJob(typeof(TJob)) is null)
        {
            AddJob<TJob, TPayload>();
        }

        JobDescriptor job = FindJob(typeof(TJob))!;

        // Serialized later, once the serializer with all registered resolvers exists.
        string? SerializePayload(KairoSerializer serializer)
        {
            return serializer.Serialize(payload);
        }

        AddRecurringJobCore(name, job, schedule, SerializePayload);
        return this;
    }

    /// <summary>
    /// Adds source-generated JSON metadata for payload types, for example <c>MyJsonContext.Default</c>.
    /// Required under Native AOT, where reflection-based serialization is disabled.
    /// </summary>
    public KairoBuilder AddJsonTypeInfoResolver(IJsonTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _jsonResolvers.Add(resolver);
        return this;
    }

    /// <summary>Uses a storage provider. Storage packages call this from their <c>UseXxxStorage()</c> method.</summary>
    public KairoBuilder UseStorage(Func<IServiceProvider, IJobStorage> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.Replace(ServiceDescriptor.Singleton(factory));
        HasStorage = true;
        return this;
    }

    internal JobRegistry BuildRegistry()
    {
        return new JobRegistry(_jobs.Values, _recurringJobs.Values);
    }

    internal KairoSerializer BuildSerializer()
    {
        return new KairoSerializer(_jsonResolvers);
    }

    private static Task InvokeJob<TJob>(IServiceProvider services, JobContext context, string? payload, CancellationToken cancellationToken)
        where TJob : class, IJob
    {
        TJob job = services.GetRequiredService<TJob>();
        return job.ExecuteAsync(context, cancellationToken);
    }

    private static Task InvokeJobWithPayload<TJob, TPayload>(IServiceProvider services, JobContext context, string? payload, CancellationToken cancellationToken)
        where TJob : class, IJob<TPayload>
    {
        KairoSerializer serializer = services.GetRequiredService<KairoSerializer>();
        TPayload value = serializer.Deserialize<TPayload>(payload);

        TJob job = services.GetRequiredService<TJob>();
        return job.ExecuteAsync(value, context, cancellationToken);
    }

    private static string? NoPayload(KairoSerializer serializer)
    {
        return null;
    }

    private void AddJobCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJob>(
        string? name,
        Action<JobOptions>? configure,
        JobInvoker invoker)
        where TJob : class
    {
        string jobName = name ?? typeof(TJob).Name;
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName, nameof(name));

        if (_jobs.ContainsKey(jobName))
        {
            throw new InvalidOperationException($"A job named '{jobName}' is already registered.");
        }

        JobDescriptor? existing = FindJob(typeof(TJob));
        if (existing is not null)
        {
            throw new InvalidOperationException($"Job type '{typeof(TJob).FullName}' is already registered as '{existing.Name}'.");
        }

        JobOptions options = new JobOptions();
        if (configure is not null)
        {
            configure(options);
        }

        ValidateJobOptions(jobName, options);

        _jobs.Add(jobName, new JobDescriptor(jobName, typeof(TJob), options, invoker));
        Services.TryAddScoped<TJob>();
    }

    private static void ValidateJobOptions(string jobName, JobOptions options)
    {
        if (options.MaxAttempts.HasValue && options.MaxAttempts.Value < 1)
        {
            throw new ArgumentException($"Job '{jobName}': MaxAttempts must be at least 1.", nameof(options));
        }

        if (options.Timeout.HasValue && options.Timeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentException($"Job '{jobName}': Timeout must be greater than zero.", nameof(options));
        }
    }

    private void AddRecurringJobCore(
        string name,
        JobDescriptor job,
        Action<RecurringSchedule> configure,
        Func<KairoSerializer, string?> payloadFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (_recurringJobs.ContainsKey(name))
        {
            throw new InvalidOperationException($"A recurring job named '{name}' is already registered.");
        }

        RecurringSchedule schedule = new RecurringSchedule();
        configure(schedule);
        if (schedule.CronExpression is null)
        {
            throw new ArgumentException($"Recurring job '{name}' has no schedule. Call Cron(...), DailyAt(...) or another schedule method.", nameof(configure));
        }

        // Fails at startup, not at the first tick, when the cron expression or time zone is wrong.
        CronSchedule.Validate(schedule.CronExpression, schedule.TimeZoneId);

        _recurringJobs.Add(name, new RecurringJobDefinition(name, job, schedule, payloadFactory));
    }

    private JobDescriptor? FindJob(Type jobType)
    {
        foreach (JobDescriptor job in _jobs.Values)
        {
            if (job.JobType == jobType)
            {
                return job;
            }
        }

        return null;
    }
}
