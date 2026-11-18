using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kairo.SourceGenerator;

/// <summary>
/// Generates <c>KairoBuilder.AddKairoJobs()</c>, which registers every class marked with [Job]
/// with a factory that calls its constructor directly.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class JobRegistrationGenerator : IIncrementalGenerator
{
    private const string JobAttributeName = "Kairo.JobAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ForAttributeWithMetadataName only looks at classes with this attribute, which keeps the IDE fast.
        IncrementalValuesProvider<JobReadResult> results = context.SyntaxProvider.ForAttributeWithMetadataName(
            JobAttributeName,
            IsClassDeclaration,
            JobModelReader.Read);

        IncrementalValueProvider<ImmutableArray<JobReadResult>> allResults = results.Collect();
        context.RegisterSourceOutput(allResults, Generate);
    }

    private static bool IsClassDeclaration(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax;
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<JobReadResult> results)
    {
        List<JobModel> jobs = new List<JobModel>();
        foreach (JobReadResult result in results)
        {
            foreach (DiagnosticInfo diagnostic in result.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (result.Job is not null)
            {
                jobs.Add(result.Job);
            }
        }

        List<JobModel> uniqueJobs = RemoveDuplicateNames(context, jobs);

        // Sorted, so the generated file does not change when the order of source files changes.
        uniqueJobs.Sort(CompareByName);

        string source = JobRegistrationEmitter.Emit(uniqueJobs);
        context.AddSource(JobRegistrationEmitter.HintName, source);
    }

    /// <summary>Keeps the first job with each name and reports the others.</summary>
    private static List<JobModel> RemoveDuplicateNames(SourceProductionContext context, List<JobModel> jobs)
    {
        Dictionary<string, JobModel> jobsByName = new Dictionary<string, JobModel>(StringComparer.Ordinal);
        List<JobModel> uniqueJobs = new List<JobModel>();

        foreach (JobModel job in jobs)
        {
            if (jobsByName.TryGetValue(job.JobName, out JobModel? firstJob))
            {
                Diagnostic duplicate = Diagnostic.Create(
                    KairoDiagnostics.DuplicateJobName,
                    job.Location.ToLocation(),
                    job.JobName,
                    firstJob.JobTypeName);
                context.ReportDiagnostic(duplicate);
                continue;
            }

            jobsByName.Add(job.JobName, job);
            uniqueJobs.Add(job);
        }

        return uniqueJobs;
    }

    private static int CompareByName(JobModel first, JobModel second)
    {
        return string.CompareOrdinal(first.JobName, second.JobName);
    }
}
