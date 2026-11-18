using Kairo.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Core.Tests;

/// <summary>Registered by the source generator, through AddKairoJobs().</summary>
[Job("generated-greeting", Queue = "greetings", MaxAttempts = 2)]
public sealed class GeneratedGreetingJob : IJob<Greeting>
{
    private readonly JobProbe _probe;

    public GeneratedGreetingJob(JobProbe probe)
    {
        _probe = probe;
    }

    public Task ExecuteAsync(Greeting payload, JobContext context, CancellationToken cancellationToken)
    {
        _probe.Record(context);
        _probe.RecordPayload(payload);
        return Task.CompletedTask;
    }
}

public class GeneratedJobRegistrationTests
{
    private readonly JobProbe _probe = new JobProbe();

    [Fact]
    public async Task AddKairoJobs_RegistersTheJobsMarkedWithTheAttribute()
    {
        await using KairoTestHost host = KairoTestHost.Create(
            kairo => kairo
                .AddKairoJobs()
                .Configure(options => options.Queues["greetings"] = new QueueOptions()),
            services => services.AddSingleton(_probe));

        await host.Client.EnqueueAsync<GeneratedGreetingJob, Greeting>(new Greeting("Kairo"));
        int runs = await host.RunDueJobsAsync();

        Assert.Equal(1, runs);
        JobContext context = Assert.Single(_probe.Runs);
        Assert.Equal("generated-greeting", context.JobName);
        Assert.Equal("greetings", context.Queue);
        Assert.Equal(2, context.MaxAttempts);
        Assert.Equal(new Greeting("Kairo"), Assert.Single(_probe.Payloads));
    }
}
