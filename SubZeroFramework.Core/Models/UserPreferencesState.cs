using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Models;

public sealed record UserPreferencesState
{
    public required UserUnitPreferencesSnapshot Preferences { get; init; }

    public required string PreferencesPath { get; init; }
}
