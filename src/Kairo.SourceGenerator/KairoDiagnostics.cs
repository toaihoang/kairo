using Microsoft.CodeAnalysis;

namespace Kairo.SourceGenerator;

internal static class KairoDiagnostics
{
    private const string Category = "Kairo";

    public static readonly DiagnosticDescriptor InvalidJobClass = new DiagnosticDescriptor(
        id: "KAIRO001",
        title: "The class marked with [Job] is not a valid job",
        messageFormat: "Job class '{0}' {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateJobName = new DiagnosticDescriptor(
        id: "KAIRO002",
        title: "Two jobs have the same name",
        messageFormat: "Job name '{0}' is already used by '{1}'. Job names must be unique.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PayloadWithoutJsonMetadata = new DiagnosticDescriptor(
        id: "KAIRO003",
        title: "A job payload type has no source-generated JSON metadata",
        messageFormat: "Payload type '{0}' of job '{1}' has no source-generated JSON metadata, so it cannot be serialized under Native AOT. Add [JsonSerializable(typeof({0}))] to a JsonSerializerContext and register it with AddJsonTypeInfoResolver(...).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor InvalidConstructorCount = new DiagnosticDescriptor(
        id: "KAIRO004",
        title: "A job class must have exactly one public constructor",
        messageFormat: "Job class '{0}' has {1} public constructors. It must have exactly one, so the generated code knows which one to call.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedConstructorParameter = new DiagnosticDescriptor(
        id: "KAIRO005",
        title: "A job constructor parameter is not supported",
        messageFormat: "Parameter '{0}' of job class '{1}' {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidAttributeValue = new DiagnosticDescriptor(
        id: "KAIRO006",
        title: "A [Job] attribute value is out of range",
        messageFormat: "[Job] on '{0}': {1} must not be negative",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
