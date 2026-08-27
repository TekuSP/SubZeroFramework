using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Persists the cooling profile library into service-settings.json.
/// </summary>
/// <remarks>
/// Reads through <see cref="IOptionsMonitor{TOptions}"/> and writes through
/// <see cref="FrameworkServiceConfigurationStore"/> — the same pair the fan control state store already uses,
/// so profiles inherit the relocation and backup behaviour of that file rather than needing their own.
/// </remarks>
public sealed class ServiceOptionsCoolingProfilePersistence : ICoolingProfilePersistence
{
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;
    private readonly FrameworkServiceConfigurationStore _configurationStore;
    private readonly ILogger<ServiceOptionsCoolingProfilePersistence> _logger;

    public ServiceOptionsCoolingProfilePersistence(
        IOptionsMonitor<FrameworkServiceOptions> optionsMonitor,
        FrameworkServiceConfigurationStore configurationStore,
        ILogger<ServiceOptionsCoolingProfilePersistence> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(configurationStore);
        ArgumentNullException.ThrowIfNull(logger);

        _optionsMonitor = optionsMonitor;
        _configurationStore = configurationStore;
        _logger = logger;
    }

    public CoolingProfileLibrary Load()
    {
        var options = _optionsMonitor.CurrentValue;

        var profiles = options.CoolingProfiles
            .Where(static profile => !string.IsNullOrWhiteSpace(profile.Id))
            .Select(static profile => profile.ToProfile())
            .ToList();

        // A selection naming a profile that is no longer in the library would leave the shell tinted by
        // something the user cannot see or deselect.
        var activeProfileId = profiles.Any(profile => string.Equals(profile.Id, options.ActiveCoolingProfileId, StringComparison.Ordinal))
            ? options.ActiveCoolingProfileId
            : null;

        // A library with profiles in it has obviously been seeded, whatever the flag says. This keeps an
        // installation that predates the flag from re-seeding on top of profiles the user already has.
        var hasSeeded = options.CoolingProfilesSeeded || profiles.Count > 0;

        return new CoolingProfileLibrary(profiles, activeProfileId, hasSeeded);
    }

    public void Save(CoolingProfileLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        var payload = library.Profiles.Select(CoolingProfileOptions.From).ToList();

        // Fire and forget, matching how every other fan-control write reaches this file: the store's own
        // write queue serialises them, and blocking a gRPC handler on a disk write would make selecting a
        // profile feel slower than applying it.
        _ = PersistAsync(payload, library.ActiveProfileId, library.HasSeeded);
    }

    private async Task PersistAsync(IReadOnlyList<CoolingProfileOptions> payload, string? activeProfileId, bool hasSeeded)
    {
        try
        {
            await _configurationStore.ReplaceCoolingProfilesAsync(payload, activeProfileId, hasSeeded).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // An unobserved exception here would take the service down, and losing a profile write is worth
            // a log line rather than a restart.
            _logger.LogError(exception, "Failed to persist the cooling profile library.");
        }
    }
}
