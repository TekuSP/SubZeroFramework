namespace SubZeroFramework.Services.Units;

public sealed record UserUnitPreferenceEntry(
    UnitQuantityKind Kind,
    string OptionKey);
