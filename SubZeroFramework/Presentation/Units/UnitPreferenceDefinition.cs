namespace SubZeroFramework.Presentation.Units;

public sealed record UnitPreferenceDefinition(
    UnitQuantityKind Kind,
    string GroupName,
    string DisplayName,
    string Description,
    string DefaultOptionKey,
    IReadOnlyList<UnitPreferenceOption> Options);
