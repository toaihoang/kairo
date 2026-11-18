using Kairo;
using Kairo.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class KairoServiceCollectionExtensions
{
    /// <summary>
    /// Adds Kairo: the <see cref="IJobClient"/> and a hosted server that runs jobs.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddKairo(kairo => kairo
    ///     .UseInMemoryStorage()
    ///     .AddJob&lt;SendEmailJob, EmailMessage&gt;()
    ///     .AddRecurringJob&lt;CleanupJob&gt;("cleanup", s => s.DailyAt(2, 0)));
    /// </code>
    /// </example>
    public static IServiceCollection AddKairo(this IServiceCollection services, Action<KairoBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(JobRegistry)))
        {
            throw new InvalidOperationException("AddKairo was already called. Register all jobs in one AddKairo call.");
        }

        KairoBuilder builder = new KairoBuilder(services);
        configure(builder);

        if (!builder.HasStorage)
        {
            throw new InvalidOperationException("Kairo has no storage. Call UseInMemoryStorage() or another storage provider inside AddKairo(...).");
        }

        services.AddOptions<KairoOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KairoOptions>, KairoOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(builder.BuildRegistry());
        services.AddSingleton(builder.BuildSerializer());
        services.AddSingleton<JobFactory>();
        services.AddSingleton<JobSignal>();
        services.AddSingleton<RunningJobs>();
        services.AddSingleton<KairoServerContext>();
        services.AddSingleton<JobExecutor>();
        services.AddSingleton<LeaseRenewer>();
        services.AddSingleton<RecurringJobScheduler>();
        services.AddSingleton<IJobClient, JobClient>();
        services.AddHostedService<KairoServer>();

        return services;
    }

    /// <summary>
    /// Adds Kairo with settings bound from <paramref name="configuration"/>, usually the "Kairo" section.
    /// </summary>
    public static IServiceCollection AddKairo(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<KairoBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Uses the configuration binding source generator, so it is safe under Native AOT.
        services.Configure<KairoOptions>(configuration);
        return services.AddKairo(configure);
    }
}
