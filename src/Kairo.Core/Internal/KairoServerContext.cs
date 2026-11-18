using Microsoft.Extensions.Options;

namespace Kairo.Internal;

/// <summary>
/// The identity of this server process. The owner id is unique per start, so a restarted
/// process never mistakes the leases of its previous run for its own.
/// </summary>
internal sealed class KairoServerContext
{
    public KairoServerContext(IOptions<KairoOptions> options)
    {
        Options = options.Value;
        Owner = $"{Options.ServerName}:{Guid.NewGuid():N}";
    }

    public KairoOptions Options { get; }

    public string Owner { get; }

    public TimeSpan LeaseRenewInterval => Options.LeaseDuration / 3;
}
