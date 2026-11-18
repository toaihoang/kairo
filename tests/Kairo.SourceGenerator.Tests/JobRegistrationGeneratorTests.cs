using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Kairo.SourceGenerator.Tests;

public class JobRegistrationGeneratorTests
{
    private const string EmailJob = """
        public sealed record Email(string Address);

        public interface IClock
        {
        }

        namespace Shop.Jobs
        {
            [Job("send-email", Queue = "emails", MaxAttempts = 3, TimeoutSeconds = 30)]
            public sealed class SendEmailJob : IJob<Email>
            {
                public SendEmailJob(ILogger<SendEmailJob> logger, IServiceProvider services, IClock? clock = null)
                {
                }

                public Task ExecuteAsync(Email payload, JobContext context, CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }
            }
        }
        """;

    [Fact]
    public void JobWithPayload_IsRegisteredWithAFactory_AndOptions()
    {
        GeneratorRun run = GeneratorHarness.RunGenerator(EmailJob);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompilationErrors);
        Assert.Contains("builder.AddJob<global::Shop.Jobs.SendEmailJob, global::Email>(", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("\"send-email\",", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(".GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::Shop.Jobs.SendEmailJob>>(services)", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(".GetService<global::IClock>(services)", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("options.Queue = \"emails\";", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("options.MaxAttempts = 3;", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("options.Timeout = global::System.TimeSpan.FromSeconds(30);", run.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void JobWithoutName_UsesTheClassName_AndNoOptions()
    {
        GeneratorRun run = GeneratorHarness.RunGenerator("""
            [Job]
            internal sealed class CleanupJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompilationErrors);
        Assert.Contains("builder.AddJob<global::CleanupJob>(", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("\"CleanupJob\",", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("services => new global::CleanupJob(),", run.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("configure: null);", run.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NoJobs_StillGeneratesAddKairoJobs()
    {
        GeneratorRun run = GeneratorHarness.RunGenerator("public sealed class NotAJob { }");

        Assert.Empty(run.CompilationErrors);
        Assert.Contains("public static global::Kairo.KairoBuilder AddKairoJobs(this global::Kairo.KairoBuilder builder)", run.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.AddJob", run.GeneratedSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[Job] public abstract class BadJob : IJob { public abstract Task ExecuteAsync(JobContext context, CancellationToken cancellationToken); }", "must not be abstract")]
    [InlineData("[Job] public sealed class BadJob { }", "must implement IJob or IJob<TPayload>")]
    [InlineData("public static class Outer { [Job] private sealed class BadJob : IJob { public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask; } }", "must be public or internal")]
    [InlineData("[Job] public sealed class BadJob : IJob, IJob<string> { public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask; public Task ExecuteAsync(string payload, JobContext context, CancellationToken cancellationToken) => Task.CompletedTask; }", "must implement only one")]
    public void InvalidJobClass_ReportsKairo001(string source, string expectedMessage)
    {
        GeneratorRun run = GeneratorHarness.RunGenerator(source);

        Diagnostic diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("KAIRO001", diagnostic.Id);
        Assert.Contains(expectedMessage, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain("BadJob", run.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateJobNames_ReportKairo002_AndKeepTheFirstJob()
    {
        GeneratorRun run = GeneratorHarness.RunGenerator("""
            [Job("same")]
            public sealed class FirstJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            [Job("same")]
            public sealed class SecondJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Diagnostic diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("KAIRO002", diagnostic.Id);
        Assert.Single(run.GeneratedSource.Split("builder.AddJob<").Skip(1));
    }

    [Fact]
    public void TwoPublicConstructors_ReportKairo004()
    {
        GeneratorRun run = GeneratorHarness.RunGenerator("""
            [Job]
            public sealed class BadJob : IJob
            {
                public BadJob() { }
                public BadJob(IServiceProvider services) { }
                public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal("KAIRO004", Assert.Single(run.GeneratorDiagnostics).Id);
    }

    [Fact]
    public void ParameterWithANonNullDefault_ReportsKairo005()
    {
        GeneratorRun run = GeneratorHarness.RunGenerator("""
            [Job]
            public sealed class BadJob : IJob
            {
                public BadJob(int batchSize = 100) { }
                public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal("KAIRO005", Assert.Single(run.GeneratorDiagnostics).Id);
    }

    [Fact]
    public void NegativeAttributeValue_ReportsKairo006()
    {
        GeneratorRun run = GeneratorHarness.RunGenerator("""
            [Job(MaxAttempts = -1)]
            public sealed class BadJob : IJob
            {
                public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal("KAIRO006", Assert.Single(run.GeneratorDiagnostics).Id);
    }
}
