using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kairo.SourceGenerator;

/// <summary>
/// KAIRO003: under Native AOT, reflection-based JSON serialization is disabled, so every job payload
/// type needs source-generated metadata. This analyzer finds payload types that no
/// JsonSerializerContext in the project declares, and reports them at build time instead of at run time.
/// </summary>
/// <remarks>
/// It runs only when the project publishes with Native AOT or disables reflection-based serialization.
/// It checks the types listed directly in [JsonSerializable]; a payload that is only reachable through
/// another declared type is reported too, and listing it directly fixes that.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PayloadJsonAnalyzer : DiagnosticAnalyzer
{
    private const string JobWithPayloadInterfaceName = "Kairo.IJob`1";
    private const string JsonContextName = "System.Text.Json.Serialization.JsonSerializerContext";
    private const string JsonSerializableAttributeName = "System.Text.Json.Serialization.JsonSerializableAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(KairoDiagnostics.PayloadWithoutJsonMetadata);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        if (!IsReflectionSerializationDisabled(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
        {
            return;
        }

        Compilation compilation = context.Compilation;
        INamedTypeSymbol? jobWithPayloadInterface = compilation.GetTypeByMetadataName(JobWithPayloadInterfaceName);
        INamedTypeSymbol? jsonContext = compilation.GetTypeByMetadataName(JsonContextName);
        INamedTypeSymbol? jsonSerializableAttribute = compilation.GetTypeByMetadataName(JsonSerializableAttributeName);
        if (jobWithPayloadInterface is null || jsonContext is null || jsonSerializableAttribute is null)
        {
            return;
        }

        PayloadCollector collector = new PayloadCollector(jobWithPayloadInterface, jsonContext, jsonSerializableAttribute);
        context.RegisterSymbolAction(collector.Inspect, SymbolKind.NamedType);

        // All types must be seen before deciding that a payload is missing, so the check runs at the end.
        context.RegisterCompilationEndAction(collector.ReportMissingPayloads);
    }

    private static bool IsReflectionSerializationDisabled(AnalyzerConfigOptions options)
    {
        if (options.TryGetValue("build_property.PublishAot", out string? publishAot)
            && string.Equals(publishAot, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (options.TryGetValue("build_property.JsonSerializerIsReflectionEnabledByDefault", out string? reflectionEnabled)
            && string.Equals(reflectionEnabled, "false", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>Collects payload types and declared JSON types while the compilation is analyzed.</summary>
    private sealed class PayloadCollector
    {
        private readonly INamedTypeSymbol _jobWithPayloadInterface;
        private readonly INamedTypeSymbol _jsonContext;
        private readonly INamedTypeSymbol _jsonSerializableAttribute;

        // Symbol actions run in parallel, so every access goes through this lock.
        private readonly object _lock = new object();
        private readonly HashSet<ITypeSymbol> _declaredTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        private readonly List<JobPayload> _payloads = new List<JobPayload>();

        public PayloadCollector(INamedTypeSymbol jobWithPayloadInterface, INamedTypeSymbol jsonContext, INamedTypeSymbol jsonSerializableAttribute)
        {
            _jobWithPayloadInterface = jobWithPayloadInterface;
            _jsonContext = jsonContext;
            _jsonSerializableAttribute = jsonSerializableAttribute;
        }

        public void Inspect(SymbolAnalysisContext context)
        {
            INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

            if (DerivesFrom(type, _jsonContext))
            {
                AddDeclaredTypes(type);
                return;
            }

            if (type.IsAbstract)
            {
                return;
            }

            foreach (INamedTypeSymbol implemented in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, _jobWithPayloadInterface))
                {
                    AddPayload(new JobPayload(type, implemented.TypeArguments[0]));
                }
            }
        }

        public void ReportMissingPayloads(CompilationAnalysisContext context)
        {
            lock (_lock)
            {
                foreach (JobPayload payload in _payloads)
                {
                    if (_declaredTypes.Contains(payload.PayloadType))
                    {
                        continue;
                    }

                    Diagnostic diagnostic = Diagnostic.Create(
                        KairoDiagnostics.PayloadWithoutJsonMetadata,
                        payload.JobType.Locations.FirstOrDefault(),
                        payload.PayloadType.ToDisplayString(),
                        payload.JobType.Name);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private void AddDeclaredTypes(INamedTypeSymbol jsonContextType)
        {
            foreach (AttributeData attribute in jsonContextType.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _jsonSerializableAttribute))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length == 0)
                {
                    continue;
                }

                if (attribute.ConstructorArguments[0].Value is ITypeSymbol declaredType)
                {
                    lock (_lock)
                    {
                        _declaredTypes.Add(declaredType);
                    }
                }
            }
        }

        private void AddPayload(JobPayload payload)
        {
            lock (_lock)
            {
                _payloads.Add(payload);
            }
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            INamedTypeSymbol? current = type.BaseType;
            while (current is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }
    }

    private sealed class JobPayload
    {
        public JobPayload(INamedTypeSymbol jobType, ITypeSymbol payloadType)
        {
            JobType = jobType;
            PayloadType = payloadType;
        }

        public INamedTypeSymbol JobType { get; }

        public ITypeSymbol PayloadType { get; }
    }
}
