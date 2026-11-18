using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kairo.SourceGenerator.Tests;

/// <summary>The result of running the generator on a piece of source code.</summary>
/// <param name="GeneratedSource">The generated AddKairoJobs file.</param>
/// <param name="GeneratorDiagnostics">The KAIROxxx diagnostics reported by the generator.</param>
/// <param name="CompilationErrors">Compile errors of the user code plus the generated code.</param>
internal sealed record GeneratorRun(
    string GeneratedSource,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationErrors);

/// <summary>Compiles test source in memory and runs the Kairo generator or analyzer on it.</summary>
internal static class GeneratorHarness
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Text.Json.Serialization;
        using Kairo;
        using Microsoft.Extensions.Logging;

        """;

    public static GeneratorRun RunGenerator(string source)
    {
        CSharpCompilation compilation = CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JobRegistrationGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult result = driver.GetRunResult();
        string generatedSource = result.GeneratedTrees.Single().ToString();

        ImmutableArray<Diagnostic> compilationErrors = outputCompilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return new GeneratorRun(generatedSource, generatorDiagnostics, compilationErrors);
    }

    public static async Task<ImmutableArray<Diagnostic>> RunPayloadAnalyzerAsync(string source, bool publishAot)
    {
        CSharpCompilation compilation = CreateCompilation(source);

        Dictionary<string, string> globalOptions = new Dictionary<string, string>();
        if (publishAot)
        {
            globalOptions.Add("build_property.PublishAot", "true");
        }

        AnalyzerOptions options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestOptionsProvider(globalOptions));

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new PayloadJsonAnalyzer());
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers, options);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(Usings + source, path: "Jobs.cs");
        CSharpCompilationOptions options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        return CSharpCompilation.Create("TestAssembly", new[] { tree }, GetReferences(), options);
    }

    /// <summary>References every assembly the test process can load, which includes .NET and Kairo.</summary>
    private static List<MetadataReference> GetReferences()
    {
        string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        List<MetadataReference> references = new List<MetadataReference>();
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }

    private sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly TestOptions s_empty = new TestOptions(new Dictionary<string, string>());

        public TestOptionsProvider(Dictionary<string, string> globalOptions)
        {
            GlobalOptions = new TestOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return s_empty;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return s_empty;
        }
    }

    private sealed class TestOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _values;

        public TestOptions(Dictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            return _values.TryGetValue(key, out value);
        }
    }
}
