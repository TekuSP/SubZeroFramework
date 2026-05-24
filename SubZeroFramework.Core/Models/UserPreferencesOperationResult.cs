using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Models;

public sealed record UserPreferencesOperationResult
{
    public required bool Succeeded { get; init; }

    public required string Message { get; init; }

    public required UserUnitPreferencesSnapshot Preferences { get; init; }

    public required string PreferencesPath { get; init; }
}
