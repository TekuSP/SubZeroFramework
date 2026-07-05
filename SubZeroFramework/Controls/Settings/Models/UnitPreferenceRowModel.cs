using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// One Display-units row: quantity icon + name + "… · sample <c>value</c>" subtitle and a segmented unit
/// picker. Selection applies live — the page model persists it through the client-local preferences store
/// as soon as a pill is tapped.
/// </summary>
public partial class UnitPreferenceRowModel : ObservableObject
{
    private readonly Action<UnitPreferenceRowModel> _selectionChanged;
    private string _selectedKey;

    public UnitPreferenceRowModel(UnitPreferenceDefinition definition, Action<UnitPreferenceRowModel> selectionChanged)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(selectionChanged);

        _selectionChanged = selectionChanged;
        _selectedKey = definition.DefaultOptionKey;

        Kind = definition.Kind;
        DisplayName = definition.DisplayName;
        SubtitlePrefix = $"{UnitPreferenceDisplay.ShortDescription(definition.Kind)} · sample";
        IconKind = Enum.TryParse<MaterialIconKind>(UnitPreferenceDisplay.IconName(definition.Kind), out var kind)
            ? kind
            : MaterialIconKind.Ruler;
        Options = new ReadOnlyCollection<UnitPreferenceSegmentModel>(
        [
            .. definition.Options.Select(option => new UnitPreferenceSegmentModel(
                option.Key,
                UnitPreferenceDisplay.ShortOptionLabel(definition.Kind, option.Key),
                option.Description,
                this))
        ]);

        ApplySelection(definition.DefaultOptionKey);
    }

    public UnitQuantityKind Kind { get; }

    public string DisplayName { get; }

    public string SubtitlePrefix { get; }

    public MaterialIconKind IconKind { get; }

    public ReadOnlyCollection<UnitPreferenceSegmentModel> Options { get; }

    public string SelectedKey => _selectedKey;

    /// <summary>Live sample value formatted in the currently selected unit (accent-colored in the row).</summary>
    [ObservableProperty]
    public partial string SampleText { get; set; } = "—";

    /// <summary>Syncs the pills to an externally applied selection without re-raising the change callback.</summary>
    public void ApplySelection(string optionKey)
    {
        _selectedKey = optionKey;

        foreach (var option in Options)
        {
            option.IsSelected = string.Equals(option.Key, optionKey, StringComparison.Ordinal);
        }
    }

    /// <summary>Called by a tapped segment; applies the selection and notifies the owning page model.</summary>
    public void SelectOption(UnitPreferenceSegmentModel option)
    {
        if (string.Equals(option.Key, _selectedKey, StringComparison.Ordinal))
        {
            return;
        }

        ApplySelection(option.Key);
        _selectionChanged(this);
    }
}
