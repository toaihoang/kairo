using Kairo;
using Kairo.Samples.MinimalApi;
using Kairo.Storage;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddKairo(builder.Configuration.GetSection(KairoOptions.SectionName), kairo => kairo
    .UseInMemoryStorage()
    .AddJsonTypeInfoResolver(SampleJsonContext.Default)

    // Jobs marked with [Job] (HeartbeatJob). Call it before AddRecurringJob for those jobs.
    .AddKairoJobs()

    // A job registered in code, because it needs options the attribute cannot express (Backoff).
    .AddJob<SendWelcomeEmailJob, WelcomeEmail>(configure: options =>
    {
        options.MaxAttempts = 3;
        options.Backoff = Backoff.Fixed(TimeSpan.FromSeconds(2));
        options.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddRecurringJob<HeartbeatJob>("heartbeat", schedule => schedule.EverySeconds(30)));

WebApplication app = builder.Build();

app.MapGet("/", () => "Kairo sample. POST /welcome?email=a@b.c, POST /welcome/later?email=a@b.c, GET /jobs/{id}.");

app.MapPost("/welcome", async (string email, IJobClient jobs, CancellationToken cancellationToken) =>
{
    string jobId = await jobs.EnqueueAsync<SendWelcomeEmailJob, WelcomeEmail>(new WelcomeEmail(email), cancellationToken: cancellationToken);
    return $"Enqueued job {jobId}";
});

app.MapPost("/welcome/later", async (string email, IJobClient jobs, CancellationToken cancellationToken) =>
{
    string jobId = await jobs.ScheduleAsync<SendWelcomeEmailJob, WelcomeEmail>(
        new WelcomeEmail(email),
        TimeSpan.FromSeconds(10),
        cancellationToken: cancellationToken);
    return $"Scheduled job {jobId} to run in 10 seconds";
});

app.MapGet("/jobs/{id}", async (string id, IJobStorage storage, CancellationToken cancellationToken) =>
{
    JobRecord? job = await storage.GetAsync(id, cancellationToken);
    if (job is null)
    {
        return Results.NotFound();
    }

    string lastError = "none";
    if (job.LastError is not null)
    {
        lastError = job.LastError.Message;
    }

    return Results.Text($"{job.Name}: {job.State}, attempt {job.Attempt} of {job.MaxAttempts}, last error: {lastError}");
});

app.Run();
