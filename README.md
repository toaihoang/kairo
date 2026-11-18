# Kairo

Kairo is a background job scheduler for .NET 10. Its name comes from the Greek *kairos*, "the right moment".

> Status: **early development**. Done: the core engine with in-memory storage (phase 1), and the source generator with Native AOT support (phase 2). Durable storage providers come next.

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
public sealed class SendEmailJob : IJob<Email>
{
    private readonly IMailer _mailer;

    public SendEmailJob(IMailer mailer)
    {
        _mailer = mailer;
    }

    public Task ExecuteAsync(Email email, JobContext context, CancellationToken cancellationToken)
    {
        return _mailer.SendAsync(email, cancellationToken);
    }
}
```

Delivery is at-least-once: after a crash, a job can run again, so make jobs idempotent.

## Registering jobs with `[Job]`
Reference `Kairo.SourceGenerator` and mark job classes with `[Job]`. The generator writes an `AddKairoJobs()` method that registers all of them, and creates each job by calling its constructor directly, with no reflection:

```csharp
[Job("send-email", Queue = "emails", MaxAttempts = 3, TimeoutSeconds = 30)]
public sealed class SendEmailJob : IJob<Email> { ... }

services.AddKairo(kairo => kairo.UseInMemoryStorage().AddKairoJobs());
```

Use `AddJob<...>(configure: ...)` instead for options the attribute cannot express, such as `Backoff` or `RetryWhen`.

The package also checks job classes at compile time:

| Rule | Severity | Meaning |
|---|---|---|
| KAIRO001 | Error | The `[Job]` class is abstract, generic, private, or does not implement exactly one of `IJob` and `IJob<T>` |
| KAIRO002 | Error | Two jobs have the same name |
| KAIRO003 | Warning | Under Native AOT, a payload type has no `[JsonSerializable]` in any `JsonSerializerContext` |
| KAIRO004 | Error | The `[Job]` class does not have exactly one public constructor |
| KAIRO005 | Error | A constructor parameter cannot come from dependency injection (`ref`/`out`, `params`, a non-null default) |
| KAIRO006 | Error | A `[Job]` value is negative |

## Native AOT
Kairo's libraries are trim- and AOT-compatible. For an AOT app:
1. Put every payload type in a `JsonSerializerContext` and register it with `AddJsonTypeInfoResolver(...)`. KAIRO003 reports the ones you missed.
2. Prefer `[Job]` + `AddKairoJobs()`, so jobs are created without reflection.

CI publishes the sample with Native AOT, fails on any trim or AOT warning, and runs the native binary.

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
