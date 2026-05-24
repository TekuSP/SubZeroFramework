using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

public partial class UnitPreferenceItemModel : ObservableObject
{
    private readonly string _defaultOptionKey;
    private string _appliedOptionKey;

    public UnitPreferenceItemModel(UnitPreferenceDefinition definition)
    {
        Kind = definition.Kind;
        GroupName = definition.GroupName;
        DisplayName = definition.DisplayName;
        Description = definition.Description;
        _defaultOptionKey = definition.DefaultOptionKey;
        _appliedOptionKey = definition.DefaultOptionKey;

        Options = new ReadOnlyObservableCollection<UnitPreferenceOptionViewModel>(
            new ObservableCollection<UnitPreferenceOptionViewModel>(
            [
                .. definition.Options.Select(option => new UnitPreferenceOptionViewModel(option.Key, option.DisplayName, option.Description))
            ]));

        DefaultOption = FindOption(definition.DefaultOptionKey);
        SelectedOption = DefaultOption;
    }

    public UnitQuantityKind Kind { get; }

    public string GroupName { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public ReadOnlyObservableCollection<UnitPreferenceOptionViewModel> Options { get; }

    public UnitPreferenceOptionViewModel DefaultOption { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(StateDescription))]
    public partial UnitPreferenceOptionViewModel SelectedOption { get; set; }

    public bool HasChanges => !string.Equals(SelectedOption?.Key, _appliedOptionKey, StringComparison.Ordinal);

    public string StateDescription => HasChanges
        ? $"Default: {DefaultOption.DisplayName} • Unsaved changes"
        : $"Default: {DefaultOption.DisplayName}";

    public void ApplySnapshotSelection(string optionKey)
    {
        var option = FindOption(optionKey);
        _appliedOptionKey = option.Key;
        SelectedOption = option;
    }

    public void ResetDraftSelection()
    {
        SelectedOption = FindOption(_appliedOptionKey);
    }

    public UserUnitPreferenceEntry ToEntry()
        => new(Kind, SelectedOption?.Key ?? _appliedOptionKey);

    // ItemsRepeater recycles the ComboBox template; the TwoWay binding can momentarily push null
    // back through SelectedItem before ItemsSource resolves the matching option reference.
    partial void OnSelectedOptionChanged(UnitPreferenceOptionViewModel value)
    {
        if (value is null)
        {
            SelectedOption = FindOption(_appliedOptionKey);
        }
    }

    private UnitPreferenceOptionViewModel FindOption(string optionKey)
    {
        return Options.FirstOrDefault(option => string.Equals(option.Key, optionKey, StringComparison.Ordinal))
            ?? DefaultOptionFallback();
    }

    private UnitPreferenceOptionViewModel DefaultOptionFallback()
    {
        return Options.First(option => string.Equals(option.Key, _defaultOptionKey, StringComparison.Ordinal));
    }
}
