using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Kairo.Internal;

/// <summary>
/// Serializes job payloads with System.Text.Json without reflection when the app provides
/// source-generated metadata (a JsonSerializerContext). Reflection is used only as a fallback,
/// and only when the app allows it (it does not under Native AOT).
/// </summary>
internal sealed class KairoSerializer
{
    private readonly JsonSerializerOptions _options;

    public KairoSerializer(IEnumerable<IJsonTypeInfoResolver> resolvers)
    {
        List<IJsonTypeInfoResolver> resolverChain = new List<IJsonTypeInfoResolver>(resolvers);
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            resolverChain.Add(CreateReflectionResolver());
        }

        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _options.TypeInfoResolver = JsonTypeInfoResolver.Combine(resolverChain.ToArray());
        _options.MakeReadOnly();
    }

    public string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, GetTypeInfo<T>());
    }

    /// <summary>
    /// Returns the payload. It is null only when null was enqueued, which the job's
    /// payload type then allows, so the null-forgiving operators below are safe.
    /// </summary>
    public T Deserialize<T>(string? json)
    {
        if (json is null)
        {
            return default!;
        }

        return JsonSerializer.Deserialize(json, GetTypeInfo<T>())!;
    }

    private JsonTypeInfo<T> GetTypeInfo<T>()
    {
        try
        {
            return (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T));
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(
                $"No JSON metadata for payload type '{typeof(T).FullName}'. Add it to a JsonSerializerContext " +
                "and register the context with KairoBuilder.AddJsonTypeInfoResolver(...).",
                ex);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only used when reflection-based serialization is enabled.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only used when reflection-based serialization is enabled.")]
    private static DefaultJsonTypeInfoResolver CreateReflectionResolver()
    {
        return new DefaultJsonTypeInfoResolver();
    }
}
