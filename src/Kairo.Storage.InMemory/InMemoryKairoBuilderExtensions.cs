using Kairo.Storage.InMemory;

namespace Kairo;

public static class InMemoryKairoBuilderExtensions
{
    /// <summary>
    /// Stores jobs in process memory. Jobs are lost when the process stops; see <see cref="InMemoryJobStorage"/>.
    /// </summary>
    public static KairoBuilder UseInMemoryStorage(this KairoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseStorage(services => new InMemoryJobStorage());
    }

    /// <summary>Uses the given in-memory storage instance, for example to share it between test hosts.</summary>
    public static KairoBuilder UseInMemoryStorage(this KairoBuilder builder, InMemoryJobStorage storage)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storage);
        return builder.UseStorage(services => storage);
    }
}
