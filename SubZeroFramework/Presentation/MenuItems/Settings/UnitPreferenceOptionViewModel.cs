namespace SubZeroFramework.Presentation.MenuItems.Settings;

public sealed class UnitPreferenceOptionViewModel
{
    public UnitPreferenceOptionViewModel(string key, string displayName, string description)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public override string ToString() => DisplayName;
}
