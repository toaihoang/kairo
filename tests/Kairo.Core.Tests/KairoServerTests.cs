using Kairo.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kairo.Core.Tests;

/// <summary>Runs the real hosted server with real time, using short intervals.</summary>
public class KairoServerTests
{
    private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(10);

    private readonly JobProbe _probe = new JobProbe();

    [Fact]
    public async Task Server_RunsAtMostWorkerCountJobsAtOnce()
    {
        using IHost host = BuildHost(kairo => kairo
            .AddJob<SlowJob>()
            .Configure(options => options.Queues[KairoOptions.DefaultQueue].WorkerCount = 2));
        await host.StartAsync();

        IJobClient client = host.Services.GetRequiredService<IJobClient>();
        for (int i = 0; i < 6; i++)
        {
            await client.EnqueueAsync<SlowJob>();
        }

        await _probe.WaitForRunsAsync(6, s_waitTimeout);
        await host.StopAsync();

        Assert.Equal(2, _probe.MaxRunning);
    }

    [Fact]
    public async Task EnqueuedJob_WakesAnIdleWorker_WithoutWaitingForThePoll()
    {
        using IHost host = BuildHost(kairo => kairo
            .AddJob<RecordingJob>()
            .Configure(options => options.PollInterval = TimeSpan.FromMinutes(10)));
        await host.StartAsync();

        // Let the worker find the queue empty and start waiting.
        await Task.Delay(100);
        await host.Services.GetRequiredService<IJobClient>().EnqueueAsync<RecordingJob>();

        await _probe.WaitForRunsAsync(1, s_waitTimeout);
        await host.StopAsync();
    }

    [Fact]
    public async Task Stop_WaitsForRunningJobs()
    {
        _probe.SlowJobDuration = TimeSpan.FromMilliseconds(300);
        using IHost host = BuildHost(kairo => kairo.AddJob<SlowJob>());
        await host.StartAsync();

        string jobId = await host.Services.GetRequiredService<IJobClient>().EnqueueAsync<SlowJob>();
        await WaitForStateAsync(host, jobId, JobState.Processing);
        await host.StopAsync();

        Assert.Equal(JobState.Succeeded, (await GetJobAsync(host, jobId)).State);
    }

    [Fact]
    public async Task Stop_AfterTheShutdownTimeout_CancelsTheJob_AndGivesItBack()
    {
        using IHost host = BuildHost(kairo => kairo
            .AddJob<HangingJob>()
            .Configure(options => options.ShutdownTimeout = TimeSpan.FromMilliseconds(200)));
        await host.StartAsync();

        string jobId = await host.Services.GetRequiredService<IJobClient>().EnqueueAsync<HangingJob>();
        await _probe.Started.WaitAsync(s_waitTimeout);
        await host.StopAsync();

        JobRecord job = await GetJobAsync(host, jobId);
        Assert.Equal(JobState.Enqueued, job.State);
        Assert.Equal(0, job.Attempt);
        Assert.Null(job.LeaseOwner);
    }

    [Fact]
    public async Task InvalidOptions_FailStartup()
    {
        using IHost host = BuildHost(kairo => kairo
            .AddJob<RecordingJob>()
            .Configure(options => options.Queues[KairoOptions.DefaultQueue].WorkerCount = 0));

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("WorkerCount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_AreBoundFromConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kairo:PollInterval"] = "00:00:03",
                ["Kairo:Queues:emails:WorkerCount"] = "4",
            })
            .Build();

        ServiceCollection services = new ServiceCollection();
        services.AddKairo(configuration.GetSection(KairoOptions.SectionName), kairo => kairo.UseInMemoryStorage());
        using ServiceProvider provider = services.BuildServiceProvider();

        KairoOptions options = provider.GetRequiredService<IOptions<KairoOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(3), options.PollInterval);
        Assert.Equal(4, options.Queues["emails"].WorkerCount);
        Assert.Equal(10, options.Queues[KairoOptions.DefaultQueue].WorkerCount);
    }

    [Fact]
    public void AddKairo_WithoutStorage_Throws()
    {
        ServiceCollection services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddKairo(kairo => kairo.AddJob<RecordingJob>()));
    }

    private IHost BuildHost(Action<KairoBuilder> configure)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_probe);
        builder.Services.AddKairo(kairo =>
        {
            kairo.UseInMemoryStorage();
            kairo.Configure(options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.ShutdownTimeout = TimeSpan.FromSeconds(5);
            });
            configure(kairo);
        });
        return builder.Build();
    }

    private static async Task<JobRecord> GetJobAsync(IHost host, string jobId)
    {
        IJobStorage storage = host.Services.GetRequiredService<IJobStorage>();
        JobRecord? job = await storage.GetAsync(jobId, CancellationToken.None);
        Assert.NotNull(job);
        return job;
    }

    private static async Task WaitForStateAsync(IHost host, string jobId, JobState state)
    {
        using CancellationTokenSource deadline = new CancellationTokenSource(s_waitTimeout);
        while ((await GetJobAsync(host, jobId)).State != state)
        {
            await Task.Delay(10, deadline.Token);
        }
    }
}
