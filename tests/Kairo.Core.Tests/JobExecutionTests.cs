using Kairo.Storage;
using Kairo.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Core.Tests;

public class JobExecutionTests
{
    private readonly JobProbe _probe = new JobProbe();

    [Fact]
    public async Task EnqueuedJob_RunsOnce_AndSucceeds()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<RecordingJob>());

        string jobId = await host.Client.EnqueueAsync<RecordingJob>();
        int runs = await host.RunDueJobsAsync();

        Assert.Equal(1, runs);
        JobRecord job = await host.GetJobAsync(jobId);
        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal(1, job.Attempt);
        Assert.Equal(host.Time.GetUtcNow(), job.CompletedAt);
        Assert.Null(job.LeaseOwner);

        IReadOnlyList<JobHistoryEntry> history = await host.Storage.GetHistoryAsync(jobId, CancellationToken.None);
        JobState[] expectedStates = new[] { JobState.Enqueued, JobState.Processing, JobState.Succeeded };
        Assert.Equal(expectedStates, history.Select(entry => entry.State));
    }

    [Fact]
    public async Task JobContext_DescribesTheRun()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<RecordingJob>("recording", options => options.MaxAttempts = 3));

        string jobId = await host.Client.EnqueueAsync<RecordingJob>();
        await host.RunDueJobsAsync();

        JobContext context = Assert.Single(_probe.Runs);
        Assert.Equal(jobId, context.JobId);
        Assert.Equal("recording", context.JobName);
        Assert.Equal(KairoOptions.DefaultQueue, context.Queue);
        Assert.Equal(1, context.Attempt);
        Assert.Equal(3, context.MaxAttempts);
        Assert.False(context.IsLastAttempt);
        Assert.Null(context.RecurringJobName);
    }

    [Fact]
    public async Task Payload_IsPassedToTheJob()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<GreetingJob, Greeting>());

        await host.Client.EnqueueAsync<GreetingJob, Greeting>(new Greeting("Kairo"));
        await host.RunDueJobsAsync();

        Assert.Equal(new Greeting("Kairo"), Assert.Single(_probe.Payloads));
    }

    [Fact]
    public async Task ScheduledJob_RunsOnlyWhenDue()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<RecordingJob>());

        string jobId = await host.Client.ScheduleAsync<RecordingJob>(TimeSpan.FromMinutes(10));

        Assert.Equal(JobState.Scheduled, (await host.GetJobAsync(jobId)).State);
        Assert.Equal(0, await host.RunDueJobsAsync());
        Assert.Equal(0, await host.AdvanceAsync(TimeSpan.FromMinutes(9)));
        Assert.Equal(1, await host.AdvanceAsync(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task ScheduledJob_InThePast_RunsNow()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<RecordingJob>());

        string jobId = await host.Client.ScheduleAsync<RecordingJob>(host.Time.GetUtcNow().AddHours(-1));

        Assert.Equal(JobState.Enqueued, (await host.GetJobAsync(jobId)).State);
        Assert.Equal(1, await host.RunDueJobsAsync());
    }

    [Fact]
    public async Task FailedJob_IsRetriedAfterBackoff_ThenSucceeds()
    {
        _probe.FailUntilAttempt = 2;
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<FailingJob>(configure: options =>
        {
            options.MaxAttempts = 3;
            options.Backoff = Backoff.Fixed(TimeSpan.FromMinutes(1));
        }));

        string jobId = await host.Client.EnqueueAsync<FailingJob>();

        Assert.Equal(1, await host.RunDueJobsAsync());
        JobRecord afterFirstFailure = await host.GetJobAsync(jobId);
        Assert.Equal(JobState.Scheduled, afterFirstFailure.State);
        Assert.Equal(host.Time.GetUtcNow().AddMinutes(1), afterFirstFailure.DueAt);
        Assert.NotNull(afterFirstFailure.LastError);
        Assert.Equal("boom", afterFirstFailure.LastError.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, afterFirstFailure.LastError.Type);

        Assert.Equal(1, await host.AdvanceAsync(TimeSpan.FromMinutes(1)));
        Assert.Equal(JobState.Scheduled, (await host.GetJobAsync(jobId)).State);

        Assert.Equal(1, await host.AdvanceAsync(TimeSpan.FromMinutes(1)));
        JobRecord job = await host.GetJobAsync(jobId);
        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal(3, job.Attempt);
        Assert.Equal(new[] { 1, 2, 3 }, _probe.Runs.Select(run => run.Attempt));
    }

    [Fact]
    public async Task JobFailingEveryAttempt_IsDeadLettered()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<FailingJob>(configure: options =>
        {
            options.MaxAttempts = 2;
            options.Backoff = Backoff.None;
        }));

        string jobId = await host.Client.EnqueueAsync<FailingJob>();

        Assert.Equal(2, await host.RunDueJobsAsync());
        JobRecord job = await host.GetJobAsync(jobId);
        Assert.Equal(JobState.DeadLettered, job.State);
        Assert.Equal(2, job.Attempt);
        Assert.NotNull(job.LastError);
        Assert.Equal("boom", job.LastError.Message);
        Assert.True(_probe.Runs.Last().IsLastAttempt);
    }

    [Fact]
    public async Task RetryWhen_ReturningFalse_DeadLettersOnTheFirstFailure()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<FailingJob>(configure: options =>
        {
            options.MaxAttempts = 5;
            options.RetryWhen = exception => exception is TimeoutException;
        }));

        string jobId = await host.Client.EnqueueAsync<FailingJob>();

        Assert.Equal(1, await host.RunDueJobsAsync());
        Assert.Equal(JobState.DeadLettered, (await host.GetJobAsync(jobId)).State);
    }

    [Fact]
    public async Task Timeout_CancelsTheAttempt_AndCountsAsAFailure()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<HangingJob>(configure: options =>
        {
            options.Timeout = TimeSpan.FromSeconds(30);
            options.MaxAttempts = 2;
            options.Backoff = Backoff.Fixed(TimeSpan.FromMinutes(5));
        }));

        string jobId = await host.Client.EnqueueAsync<HangingJob>();
        Task<int> run = host.RunDueJobsAsync();
        await _probe.Started;

        host.Time.Advance(TimeSpan.FromSeconds(30));
        await run;

        JobRecord job = await host.GetJobAsync(jobId);
        Assert.Equal(JobState.Scheduled, job.State);
        Assert.NotNull(job.LastError);
        Assert.Equal(typeof(TimeoutException).FullName, job.LastError.Type);
    }

    [Fact]
    public async Task IdempotencyKey_ReturnsTheExistingJob()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<RecordingJob>());
        EnqueueOptions options = new EnqueueOptions { IdempotencyKey = "order-42" };

        string first = await host.Client.EnqueueAsync<RecordingJob>(options);
        string second = await host.Client.EnqueueAsync<RecordingJob>(options);

        Assert.Equal(first, second);
        Assert.Equal(1, await host.RunDueJobsAsync());
    }

    [Fact]
    public async Task Enqueue_UnregisteredJobType_Throws()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<RecordingJob>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Client.EnqueueAsync<FailingJob>());
    }

    [Fact]
    public async Task JobOnAQueueThisServerDoesNotProcess_DoesNotRun()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo.AddJob<RecordingJob>());

        string jobId = await host.Client.EnqueueAsync<RecordingJob>(new EnqueueOptions { Queue = "emails" });

        Assert.Equal(0, await host.RunDueJobsAsync());
        Assert.Equal(JobState.Enqueued, (await host.GetJobAsync(jobId)).State);
    }

    [Fact]
    public async Task JobOnAConfiguredQueue_Runs()
    {
        await using KairoTestHost host = CreateHost(kairo => kairo
            .AddJob<RecordingJob>(configure: options => options.Queue = "emails")
            .Configure(options => options.Queues["emails"] = new QueueOptions()));

        await host.Client.EnqueueAsync<RecordingJob>();

        Assert.Equal(1, await host.RunDueJobsAsync());
        Assert.Equal("emails", Assert.Single(_probe.Runs).Queue);
    }

    [Fact]
    public void AddJob_SameNameTwice_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateHost(kairo => kairo
            .AddJob<RecordingJob>("same")
            .AddJob<FailingJob>("same")));
    }

    private KairoTestHost CreateHost(Action<KairoBuilder> configure) =>
        KairoTestHost.Create(configure, services => services.AddSingleton(_probe));
}
