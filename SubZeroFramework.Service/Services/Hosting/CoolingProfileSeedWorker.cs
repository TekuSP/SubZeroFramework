using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using DynamicData;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services.Hosting;

/// <summary>
/// Writes the starting set of cooling profiles once the machine has reported its fans.
/// </summary>
/// <remarks>
/// <para>
/// It has to wait: a seeded profile names the fans it applies to, and seeding before the fan states arrive
/// would produce three profiles that mention no fans at all — permanently, because seeding only ever happens
/// once.
/// </para>
/// <para>
/// Its own worker rather than a side effect of the profile stream, so seeding does not depend on a client
/// happening to connect, and rather than a dependency inside the profile store, so that store stays
/// testable without a fan stack behind it.
/// </para>
/// </remarks>
public sealed class CoolingProfileSeedWorker : BackgroundService
{
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly FrameworkCoolingProfileStore _coolingProfileStore;
    private readonly ILogger<CoolingProfileSeedWorker> _logger;

    public CoolingProfileSeedWorker(
        FrameworkFanControlStateStore fanControlStateStore,
        FrameworkCoolingProfileStore coolingProfileStore,
        ILogger<CoolingProfileSeedWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(fanControlStateStore);
        ArgumentNullException.ThrowIfNull(coolingProfileStore);
        ArgumentNullException.ThrowIfNull(logger);

        _fanControlStateStore = fanControlStateStore;
        _coolingProfileStore = coolingProfileStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The first emission that actually contains fans. On a machine whose fans never appear this
            // simply never fires, which is the right outcome: no fans, nothing worth seeding.
            var fanIndices = await _fanControlStateStore
                .Connect()
                .Select(static _ => 0)
                .StartWith(0)
                .Select(_ => _fanControlStateStore.GetKnownFanIndices())
                .Where(static indices => indices.Length > 0)
                .FirstAsync()
                .ToTask(stoppingToken)
                .ConfigureAwait(false);

            _coolingProfileStore.SeedIfEmpty([.. fanIndices]);

            _logger.LogInformation(
                "Cooling profile seeding evaluated for {FanCount} fan(s); library now holds {ProfileCount} profile(s).",
                fanIndices.Length,
                _coolingProfileStore.Profiles.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping cooling profile seeding because the service is shutting down.");
        }
        catch (Exception exception)
        {
            // Seeding is a convenience. A machine with no starting profiles is a worse first run, not a
            // broken service, so this must never be the reason the host fails to start.
            _logger.LogError(exception, "Failed to seed the cooling profile library.");
        }
    }
}
