using Kairo.Storage;
using Microsoft.Extensions.Logging;

namespace Kairo.Internal;

/// <summary>
/// Extends the leases of all running jobs every third of the lease duration. A job whose lease
/// could not be extended is owned by another worker now, so it is cancelled here.
/// </summary>
internal sealed class LeaseRenewer
{
    private readonly KairoServerContext _server;
    private readonly IJobStorage _storage;
    private readonly RunningJobs _runningJobs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LeaseRenewer> _logger;

    public LeaseRenewer(
        KairoServerContext server,
        IJobStorage storage,
        RunningJobs runningJobs,
        TimeProvider timeProvider,
        ILogger<LeaseRenewer> logger)
    {
        _server = server;
        _storage = storage;
        _runningJobs = runningJobs;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_server.LeaseRenewInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RenewAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task RenewAsync(CancellationToken cancellationToken)
    {
        string[] jobIds = _runningJobs.JobIds.ToArray();
        if (jobIds.Length == 0)
        {
            return;
        }

        IReadOnlyCollection<string> renewedJobIds;
        try
        {
            DateTimeOffset expiresAt = _timeProvider.GetUtcNow() + _server.Options.LeaseDuration;
            renewedJobIds = await _storage.RenewLeasesAsync(_server.Owner, jobIds, expiresAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
#pragma warning disable CA1031 // Keep the jobs running; the next renewal may succeed before the leases expire.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.LeaseRenewalFailed(_logger, ex, jobIds.Length);
            return;
        }

        HashSet<string> stillOwned = new HashSet<string>(renewedJobIds, StringComparer.Ordinal);
        foreach (string jobId in jobIds)
        {
            if (!stillOwned.Contains(jobId))
            {
                _runningJobs.SignalLeaseLost(jobId);
            }
        }
    }
}
