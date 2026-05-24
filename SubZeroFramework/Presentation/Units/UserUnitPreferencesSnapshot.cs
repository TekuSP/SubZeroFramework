namespace SubZeroFramework.Presentation.Units;

public sealed class UserUnitPreferencesSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public UserUnitPreferenceEntry[] Entries { get; init; } = [];

    public string GetOptionKey(UnitQuantityKind kind, string fallbackOptionKey)
    {
        return Entries
            .LastOrDefault(entry => entry.Kind == kind)?.OptionKey
            ?? fallbackOptionKey;
    }
}
