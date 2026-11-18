using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kairo.SourceGenerator;

/// <summary>
/// Reads one class marked with [Job] into a <see cref="JobModel"/>, or into the diagnostics
/// that explain why it cannot be registered.
/// </summary>
internal static class JobModelReader
{
    private const string JobInterfaceName = "Kairo.IJob";
    private const string JobWithPayloadInterfaceName = "Kairo.IJob`1";

    private static readonly SymbolDisplayFormat s_fullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    public static JobReadResult Read(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        INamedTypeSymbol jobType = (INamedTypeSymbol)context.TargetSymbol;
        ClassDeclarationSyntax declaration = (ClassDeclarationSyntax)context.TargetNode;
        LocationInfo location = LocationInfo.From(declaration.Identifier.GetLocation());
        string className = jobType.Name;

        List<DiagnosticInfo> diagnostics = new List<DiagnosticInfo>();
        AddClassShapeDiagnostics(jobType, location, diagnostics);

        string? payloadTypeName;
        string? jobInterfaceProblem = FindPayloadType(jobType, context.SemanticModel.Compilation, out payloadTypeName);
        if (jobInterfaceProblem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.InvalidJobClass, location, className, jobInterfaceProblem));
        }

        AttributeData attribute = context.Attributes[0];
        string jobName = ReadJobName(attribute, className);
        string? queue = ReadNamedString(attribute, "Queue");
        int maxAttempts = ReadNamedInt(attribute, "MaxAttempts");
        int timeoutSeconds = ReadNamedInt(attribute, "TimeoutSeconds");

        if (maxAttempts < 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.InvalidAttributeValue, location, className, "MaxAttempts"));
        }

        if (timeoutSeconds < 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.InvalidAttributeValue, location, className, "TimeoutSeconds"));
        }

        // Report only the root cause: an abstract class, for example, also has no public constructor.
        if (diagnostics.Count > 0)
        {
            return new JobReadResult(null, ToEquatable(diagnostics.ToImmutableArray()));
        }

        ImmutableArray<ConstructorParameterModel> parameters = ReadConstructorParameters(jobType, location, diagnostics, cancellationToken);

        if (diagnostics.Count > 0)
        {
            return new JobReadResult(null, ToEquatable(diagnostics.ToImmutableArray()));
        }

        JobModel job = new JobModel(
            jobType.ToDisplayString(s_fullyQualified),
            payloadTypeName,
            jobName,
            queue,
            maxAttempts,
            timeoutSeconds,
            ToEquatable(parameters),
            location);

        return new JobReadResult(job, ToEquatable(ImmutableArray<DiagnosticInfo>.Empty));
    }

    /// <summary>The generated code creates the class with "new", so it must be concrete, non-generic and visible.</summary>
    private static void AddClassShapeDiagnostics(INamedTypeSymbol jobType, LocationInfo location, List<DiagnosticInfo> diagnostics)
    {
        string className = jobType.Name;

        if (jobType.IsAbstract)
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.InvalidJobClass, location, className, "must not be abstract"));
        }

        if (jobType.TypeParameters.Length > 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.InvalidJobClass, location, className, "must not be generic"));
        }

        if (!IsVisibleInAssembly(jobType))
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.InvalidJobClass, location, className, "must be public or internal, and so must the classes it is nested in"));
        }
    }

    private static bool IsVisibleInAssembly(INamedTypeSymbol type)
    {
        INamedTypeSymbol? current = type;
        while (current is not null)
        {
            bool isVisible = current.DeclaredAccessibility == Accessibility.Public
                || current.DeclaredAccessibility == Accessibility.Internal;
            if (!isVisible)
            {
                return false;
            }

            current = current.ContainingType;
        }

        return true;
    }

    /// <summary>
    /// Finds which job interface the class implements. Returns null when it implements exactly one,
    /// or a description of the problem otherwise.
    /// </summary>
    private static string? FindPayloadType(INamedTypeSymbol jobType, Compilation compilation, out string? payloadTypeName)
    {
        payloadTypeName = null;

        INamedTypeSymbol? jobInterface = compilation.GetTypeByMetadataName(JobInterfaceName);
        INamedTypeSymbol? jobWithPayloadInterface = compilation.GetTypeByMetadataName(JobWithPayloadInterfaceName);
        if (jobInterface is null || jobWithPayloadInterface is null)
        {
            return "cannot be checked because Kairo.Abstractions is not referenced";
        }

        int jobInterfaceCount = 0;
        foreach (INamedTypeSymbol implemented in jobType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, jobInterface))
            {
                jobInterfaceCount++;
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, jobWithPayloadInterface))
            {
                jobInterfaceCount++;
                payloadTypeName = implemented.TypeArguments[0].ToDisplayString(s_fullyQualified);
            }
        }

        if (jobInterfaceCount == 0)
        {
            return "must implement IJob or IJob<TPayload>";
        }

        if (jobInterfaceCount > 1)
        {
            return "must implement only one of IJob and IJob<TPayload>";
        }

        return null;
    }

    private static string ReadJobName(AttributeData attribute, string className)
    {
        if (attribute.ConstructorArguments.Length == 0)
        {
            return className;
        }

        if (attribute.ConstructorArguments[0].Value is string name && name.Length > 0)
        {
            return name;
        }

        return className;
    }

    private static string? ReadNamedString(AttributeData attribute, string propertyName)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == propertyName && argument.Value.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    private static int ReadNamedInt(AttributeData attribute, string propertyName)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == propertyName && argument.Value.Value is int value)
            {
                return value;
            }
        }

        return 0;
    }

    private static ImmutableArray<ConstructorParameterModel> ReadConstructorParameters(
        INamedTypeSymbol jobType,
        LocationInfo location,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        List<IMethodSymbol> publicConstructors = new List<IMethodSymbol>();
        foreach (IMethodSymbol constructor in jobType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public)
            {
                publicConstructors.Add(constructor);
            }
        }

        if (publicConstructors.Count != 1)
        {
            string count = publicConstructors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.InvalidConstructorCount, location, jobType.Name, count));
            return ImmutableArray<ConstructorParameterModel>.Empty;
        }

        ImmutableArray<ConstructorParameterModel>.Builder parameters = ImmutableArray.CreateBuilder<ConstructorParameterModel>();
        foreach (IParameterSymbol parameter in publicConstructors[0].Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConstructorParameterModel? model = ReadParameter(parameter, jobType, location, diagnostics);
            if (model is not null)
            {
                parameters.Add(model);
            }
        }

        return parameters.ToImmutable();
    }

    private static ConstructorParameterModel? ReadParameter(
        IParameterSymbol parameter,
        INamedTypeSymbol jobType,
        LocationInfo location,
        List<DiagnosticInfo> diagnostics)
    {
        if (parameter.RefKind != RefKind.None)
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.UnsupportedConstructorParameter, location, parameter.Name, jobType.Name, "must not be ref, out or in"));
            return null;
        }

        if (parameter.IsParams)
        {
            diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.UnsupportedConstructorParameter, location, parameter.Name, jobType.Name, "must not be params"));
            return null;
        }

        string typeName = parameter.Type.ToDisplayString(s_fullyQualified);

        if (typeName == "global::System.IServiceProvider")
        {
            return new ConstructorParameterModel(typeName, ParameterKind.ServiceProvider);
        }

        if (!parameter.HasExplicitDefaultValue)
        {
            return new ConstructorParameterModel(typeName, ParameterKind.RequiredService);
        }

        // A default of null means "optional service"; other defaults have no service to come from.
        if (parameter.ExplicitDefaultValue is null)
        {
            return new ConstructorParameterModel(typeName, ParameterKind.OptionalService);
        }

        diagnostics.Add(DiagnosticInfo.Create(KairoDiagnostics.UnsupportedConstructorParameter, location, parameter.Name, jobType.Name, "has a default value other than null, which cannot come from dependency injection"));
        return null;
    }

    private static EquatableArray<T> ToEquatable<T>(ImmutableArray<T> items)
        where T : IEquatable<T>
    {
        return new EquatableArray<T>(items);
    }
}
