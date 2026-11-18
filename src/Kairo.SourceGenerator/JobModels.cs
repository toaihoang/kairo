using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kairo.SourceGenerator;

// The generator's models hold only strings, numbers and other models, never Roslyn symbols,
// so they can be compared with the previous run and the output step skipped when nothing changed.

/// <summary>A valid class marked with [Job].</summary>
/// <param name="JobTypeName">The fully qualified class name, starting with "global::".</param>
/// <param name="PayloadTypeName">The fully qualified payload type of IJob&lt;T&gt;, or null for IJob.</param>
/// <param name="JobName">The job name from the attribute, or the class name.</param>
/// <param name="Queue">The queue from the attribute, or null.</param>
/// <param name="MaxAttempts">The attempts from the attribute; 0 means the server default.</param>
/// <param name="TimeoutSeconds">The timeout from the attribute; 0 means no timeout.</param>
/// <param name="ConstructorParameters">The parameters of the public constructor, in order.</param>
/// <param name="Location">Where the class is declared, for diagnostics.</param>
internal sealed record JobModel(
    string JobTypeName,
    string? PayloadTypeName,
    string JobName,
    string? Queue,
    int MaxAttempts,
    int TimeoutSeconds,
    EquatableArray<ConstructorParameterModel> ConstructorParameters,
    LocationInfo Location);

/// <param name="TypeName">The fully qualified parameter type.</param>
/// <param name="Kind">How the generated factory gets the value.</param>
internal sealed record ConstructorParameterModel(string TypeName, ParameterKind Kind);

internal enum ParameterKind
{
    /// <summary>A registered service; resolving it fails when it is missing.</summary>
    RequiredService,

    /// <summary>A parameter with the default value null; it gets null when the service is missing.</summary>
    OptionalService,

    /// <summary>The scope's IServiceProvider itself.</summary>
    ServiceProvider,
}

/// <summary>The result of reading one [Job] class: a model, or the reasons it is invalid.</summary>
internal sealed record JobReadResult(JobModel? Job, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>A diagnostic stored without Roslyn objects that cannot be compared between runs.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo Location, EquatableArray<string> MessageArguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo location, params string[] messageArguments)
    {
        return new DiagnosticInfo(descriptor, location, new EquatableArray<string>(messageArguments.ToImmutableArray()));
    }

    public Diagnostic ToDiagnostic()
    {
        object[] arguments = MessageArguments.Cast<object>().ToArray();
        return Diagnostic.Create(Descriptor, Location.ToLocation(), arguments);
    }
}

/// <summary>A source location stored as plain values.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationInfo From(Location location)
    {
        FileLinePositionSpan lineSpan = location.GetLineSpan();
        return new LocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation()
    {
        return Location.Create(FilePath, TextSpan, LineSpan);
    }
}
