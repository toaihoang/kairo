# Kairo

Kairo is a background job scheduler for .NET 10. Its name comes from the Greek *kairos*, "the right moment".

> Status: **early development**. Phase 1 is done: the core engine with in-memory storage. Durable storage providers come next.

## Quick start
```csharp
builder.Services.AddKairo(builder.Configuration.GetSection("Kairo"), kairo => kairo
    .UseInMemoryStorage()
    .AddJsonTypeInfoResolver(AppJsonContext.Default)          // payload metadata, required for Native AOT
    .AddJob<SendEmailJob, Email>(configure: options =>
    {
        options.MaxAttempts = 3;
        options.Backoff = Backoff.Exponential(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10));
        options.Timeout = TimeSpan.FromMinutes(1);
    })
    .AddRecurringJob<CleanupJob>("cleanup", schedule => schedule
        .DailyAt(2, 30)
        .InTimeZone("Asia/Ho_Chi_Minh")
        .PreventOverlap()));

// Anywhere with DI:
await jobs.EnqueueAsync<SendEmailJob, Email>(new Email("a@b.c"));
await jobs.ScheduleAsync<SendEmailJob, Email>(new Email("a@b.c"), TimeSpan.FromMinutes(5));
```

A job is a class from dependency injection; a new scope is created for every run:

```csharp
public sealed class SendEmailJob(IMailer mailer) : IJob<Email>
{
    public Task ExecuteAsync(Email email, JobContext context, CancellationToken cancellationToken) =>
        mailer.SendAsync(email, cancellationToken);
}
```

Delivery is at-least-once: after a crash, a job can run again, so make jobs idempotent.

## Testing your jobs
`Kairo.Testing` runs jobs one by one on a fake clock, without a hosted server:

```csharp
await using KairoTestHost host = KairoTestHost.Create(kairo => kairo.AddJob<SendEmailJob, Email>());
await host.Client.ScheduleAsync<SendEmailJob, Email>(email, TimeSpan.FromMinutes(5));
int runs = await host.AdvanceAsync(TimeSpan.FromMinutes(5));   // the job runs here
```

## Goals
- **Durable jobs:** fire-and-forget, delayed, recurring (cron or fluent API) and continuation jobs. Jobs get retries and a dead-letter state.
- **Reliable in a cluster:**
  - Delivery is at-least-once.
  - Jobs are owned through leases, so a crashed node's jobs are picked up again.
  - One leader schedules recurring jobs, so they are not duplicated.
- **Reflection-free:** a source generator registers jobs and payload serializers, so apps can publish with Native AOT.
- **Pluggable storage:** InMemory, PostgreSQL, SQL Server, Redis and JSON file.
- **Observable:** OpenTelemetry traces and metrics, health checks and an embedded dashboard.

## Repository layout
| Folder | Content |
|---|---|
| `src/` | Library packages (`Kairo.*`), all trim/AOT compatible |
| `tests/` | Unit and conformance tests |
| `samples/` | Sample apps; the Minimal API sample is published with Native AOT in CI |

## Build
Requires the .NET SDK pinned in `global.json`: 10.0.403 or later, the first SDK with the fix for the Brotli advisory GHSA-p3jp-9v83-76w9.

```bash
dotnet build Kairo.slnx
dotnet test Kairo.slnx
dotnet publish samples/Kairo.Samples.MinimalApi -r win-x64   # Native AOT check
```

Native AOT publish needs the platform toolchain: the C++ build tools on Windows, or clang on Linux.

## Code style
Code style is defined in `.editorconfig`, and the build enforces it (`EnforceCodeStyleInBuild` with warnings as errors). Run `dotnet format Kairo.slnx` to apply the fixable rules.

## License
[MIT](LICENSE)
