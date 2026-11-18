using Kairo.Storage;
using Kairo.Storage.InMemory;

namespace Kairo.Core.Tests;

public class InMemoryJobStorageTests
{
    private static readonly DateTimeOffset s_now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan s_lease = TimeSpan.FromMinutes(1);

    private readonly InMemoryJobStorage _storage = new InMemoryJobStorage();

    [Fact]
    public async Task Acquire_TakesDueJobs_OldestFirst_UpToMaxCount()
    {
        await _storage.CreateAsync(NewJob("c", s_now.AddSeconds(-1)), CancellationToken.None);
        await _storage.CreateAsync(NewJob("a", s_now.AddSeconds(-3)), CancellationToken.None);
        await _storage.CreateAsync(NewJob("b", s_now.AddSeconds(-2)), CancellationToken.None);

        IReadOnlyList<JobRecord> jobs = await _storage.AcquireAsync(Request("worker", maxCount: 2), CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, jobs.Select(job => job.Id));
        foreach (JobRecord job in jobs)
        {
            Assert.Equal(JobState.Processing, job.State);
            Assert.Equal(1, job.Attempt);
            Assert.Equal("worker", job.LeaseOwner);
            Assert.Equal(s_now + s_lease, job.LeaseExpiresAt);
        }
    }

    [Fact]
    public async Task Acquire_SkipsFutureJobs_AndOtherQueues()
    {
        await _storage.CreateAsync(NewJob("future", s_now.AddSeconds(1), state: JobState.Scheduled), CancellationToken.None);
        await _storage.CreateAsync(NewJob("other-queue", s_now, queue: "emails"), CancellationToken.None);

        Assert.Empty(await _storage.AcquireAsync(Request("worker"), CancellationToken.None));
    }

    [Fact]
    public async Task Acquire_FromManyThreads_GivesEachJobToOneOwner()
    {
        const int JobCount = 2000;
        const int WorkerCount = 8;
        for (int i = 0; i < JobCount; i++)
        {
            await _storage.CreateAsync(NewJob("job-" + i, s_now), CancellationToken.None);
        }

        List<Task<List<string>>> workers = new List<Task<List<string>>>();
        for (int worker = 0; worker < WorkerCount; worker++)
        {
            string owner = "worker-" + worker;
            workers.Add(Task.Run(() => AcquireUntilEmptyAsync(owner)));
        }

        List<string>[] takenByEachWorker = await Task.WhenAll(workers);
        List<string> allTaken = takenByEachWorker.SelectMany(jobIds => jobIds).ToList();

        Assert.Equal(JobCount, allTaken.Count);
        Assert.Equal(JobCount, allTaken.Distinct().Count());
    }

    [Fact]
    public async Task ExpiredLease_IsRecoveredByAnotherOwner_AndTheOldOwnerLosesTheJob()
    {
        await _storage.CreateAsync(NewJob("job", s_now), CancellationToken.None);
        await _storage.AcquireAsync(Request("crashed"), CancellationToken.None);

        DateTimeOffset later = s_now + s_lease + TimeSpan.FromSeconds(1);
        IReadOnlyList<JobRecord> recoveredJobs = await _storage.AcquireAsync(Request("rescuer", now: later), CancellationToken.None);
        JobRecord recovered = Assert.Single(recoveredJobs);

        Assert.Equal("rescuer", recovered.LeaseOwner);
        Assert.Equal(2, recovered.Attempt);
        Assert.False(await _storage.TransitionAsync(Succeeded("job", "crashed", later), CancellationToken.None));
        Assert.Empty(await _storage.RenewLeasesAsync("crashed", new[] { "job" }, later + s_lease, CancellationToken.None));
        Assert.True(await _storage.TransitionAsync(Succeeded("job", "rescuer", later), CancellationToken.None));
    }

    [Fact]
    public async Task RenewedLease_IsNotRecovered()
    {
        await _storage.CreateAsync(NewJob("job", s_now), CancellationToken.None);
        await _storage.AcquireAsync(Request("worker"), CancellationToken.None);

        DateTimeOffset renewedUntil = s_now + (s_lease * 3);
        IReadOnlyCollection<string> renewed = await _storage.RenewLeasesAsync("worker", new[] { "job" }, renewedUntil, CancellationToken.None);

        Assert.Equal(new[] { "job" }, renewed);
        DateTimeOffset afterTheOriginalLease = s_now + (s_lease * 2);
        Assert.Empty(await _storage.AcquireAsync(Request("other", now: afterTheOriginalLease), CancellationToken.None));
    }

    [Fact]
    public async Task UndoAttempt_GivesTheJobBack_WithoutCountingTheAttempt()
    {
        await _storage.CreateAsync(NewJob("job", s_now), CancellationToken.None);
        await _storage.AcquireAsync(Request("worker"), CancellationToken.None);

        JobTransition release = new JobTransition
        {
            JobId = "job",
            Owner = "worker",
            NewState = JobState.Enqueued,
            Now = s_now,
            DueAt = s_now,
            UndoAttempt = true,
        };
        bool released = await _storage.TransitionAsync(release, CancellationToken.None);

        Assert.True(released);
        JobRecord? job = await _storage.GetAsync("job", CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(JobState.Enqueued, job.State);
        Assert.Equal(0, job.Attempt);

        IReadOnlyList<JobRecord> acquiredAgain = await _storage.AcquireAsync(Request("worker"), CancellationToken.None);
        Assert.Equal(1, Assert.Single(acquiredAgain).Attempt);
    }

    [Fact]
    public async Task Create_WithAnExistingIdempotencyKey_ReturnsTheExistingId()
    {
        string first = await _storage.CreateAsync(NewJob("first", s_now, idempotencyKey: "key"), CancellationToken.None);
        string second = await _storage.CreateAsync(NewJob("second", s_now, idempotencyKey: "key"), CancellationToken.None);

        Assert.Equal("first", first);
        Assert.Equal("first", second);
        Assert.Null(await _storage.GetAsync("second", CancellationToken.None));
    }

    [Fact]
    public async Task TryAdvanceRecurring_FailsWhenAnotherSchedulerAdvancedFirst()
    {
        RecurringJobRecord recurringJob = new RecurringJobRecord
        {
            Name = "tick",
            JobName = "job",
            Cron = "* * * * *",
            NextRunAt = s_now,
            UpdatedAt = s_now,
        };
        await _storage.SaveRecurringAsync(recurringJob, CancellationToken.None);

        List<JobRecord> firstJobs = new List<JobRecord> { NewJob("occurrence", s_now) };
        RecurringAdvance firstAdvance = new RecurringAdvance("tick", s_now, s_now.AddMinutes(1), firstJobs, s_now);

        // A second scheduler that read the same NextRunAt.
        List<JobRecord> secondJobs = new List<JobRecord> { NewJob("duplicate", s_now) };
        RecurringAdvance secondAdvance = new RecurringAdvance("tick", s_now, s_now.AddMinutes(1), secondJobs, s_now);

        Assert.True(await _storage.TryAdvanceRecurringAsync(firstAdvance, CancellationToken.None));
        Assert.False(await _storage.TryAdvanceRecurringAsync(secondAdvance, CancellationToken.None));
        Assert.Null(await _storage.GetAsync("duplicate", CancellationToken.None));

        RecurringJobRecord? saved = await _storage.GetRecurringAsync("tick", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("occurrence", saved.LastJobId);
    }

    private async Task<List<string>> AcquireUntilEmptyAsync(string owner)
    {
        List<string> taken = new List<string>();
        while (true)
        {
            IReadOnlyList<JobRecord> jobs = await _storage.AcquireAsync(Request(owner, maxCount: 7), CancellationToken.None);
            if (jobs.Count == 0)
            {
                return taken;
            }

            foreach (JobRecord job in jobs)
            {
                taken.Add(job.Id);
            }
        }
    }

    private static AcquireRequest Request(string owner, int maxCount = 10, DateTimeOffset? now = null)
    {
        DateTimeOffset requestTime = s_now;
        if (now.HasValue)
        {
            requestTime = now.Value;
        }

        string[] queues = new[] { KairoOptions.DefaultQueue };
        return new AcquireRequest(queues, maxCount, owner, requestTime, s_lease);
    }

    private static JobTransition Succeeded(string jobId, string owner, DateTimeOffset now)
    {
        return new JobTransition { JobId = jobId, Owner = owner, NewState = JobState.Succeeded, Now = now };
    }

    private static JobRecord NewJob(
        string id,
        DateTimeOffset dueAt,
        string queue = KairoOptions.DefaultQueue,
        JobState state = JobState.Enqueued,
        string? idempotencyKey = null)
    {
        return new JobRecord
        {
            Id = id,
            Name = "job",
            Queue = queue,
            State = state,
            CreatedAt = s_now,
            DueAt = dueAt,
            MaxAttempts = 3,
            IdempotencyKey = idempotencyKey,
        };
    }
}
