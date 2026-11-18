using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Kairo.SourceGenerator.Tests;

public class PayloadJsonAnalyzerTests
{
    private const string JobWithEmailPayload = """
        public sealed record Email(string Address);

        public sealed class SendEmailJob : IJob<Email>
        {
            public Task ExecuteAsync(Email payload, JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        """;

    [Fact]
    public async Task PayloadMissingFromEveryJsonContext_UnderAot_ReportsKairo003()
    {
        ImmutableArray<Diagnostic> diagnostics = await GeneratorHarness.RunPayloadAnalyzerAsync(JobWithEmailPayload, publishAot: true);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("KAIRO003", diagnostic.Id);
        Assert.Contains("'Email'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("'SendEmailJob'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadDeclaredInAJsonContext_UnderAot_ReportsNothing()
    {
        string source = JobWithEmailPayload + """
            [JsonSerializable(typeof(Email))]
            internal sealed partial class AppJsonContext : JsonSerializerContext
            {
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GeneratorHarness.RunPayloadAnalyzerAsync(source, publishAot: true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task WithoutAot_ReportsNothing_BecauseReflectionWorks()
    {
        ImmutableArray<Diagnostic> diagnostics = await GeneratorHarness.RunPayloadAnalyzerAsync(JobWithEmailPayload, publishAot: false);

        Assert.Empty(diagnostics);
    }
}
