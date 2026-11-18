namespace Kairo;

/// <summary>
/// Marks a class as a Kairo job. The Kairo source generator registers every marked class
/// in the generated <c>AddKairoJobs()</c> method, and creates the job without reflection.
/// </summary>
/// <example>
/// <code>
/// [Job("send-email", Queue = "emails", MaxAttempts = 3, TimeoutSeconds = 30)]
/// public sealed class SendEmailJob : IJob&lt;Email&gt; { ... }
///
/// services.AddKairo(kairo => kairo.UseInMemoryStorage().AddKairoJobs());
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class JobAttribute : Attribute
{
    /// <summary>Uses the class name as the job name.</summary>
    public JobAttribute()
    {
    }

    /// <param name="name">The job name stored with every job. Keep it stable: renaming it orphans stored jobs.</param>
    public JobAttribute(string name)
    {
        Name = name;
    }

    public string? Name { get; }

    /// <summary>The default queue for this job.</summary>
    public string? Queue { get; set; }

    /// <summary>The total number of attempts. 0 uses <c>KairoOptions.DefaultMaxAttempts</c>.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>The timeout of one attempt in seconds. 0 means no timeout.</summary>
    public int TimeoutSeconds { get; set; }
}
