using System.Collections.ObjectModel;

namespace SubZeroFramework.Services.Units;

public sealed class UnitPreferenceCatalog
{
    private static readonly UnitPreferenceDefinition[] DefinitionsArray =
    [
        new(
            UnitQuantityKind.Temperature,
            GroupName: "Thermal",
            DisplayName: "Temperature",
            Description: "Applies to thermal cards, temperature history axes, and cooling-driving temperature labels.",
            DefaultOptionKey: "celsius",
            Options:
            [
                new UnitPreferenceOption("celsius", "Celsius (°C)", "Preserves the app's current thermal display style."),
                new UnitPreferenceOption("fahrenheit", "Fahrenheit (°F)", "Converts thermal values to degrees Fahrenheit."),
                new UnitPreferenceOption("kelvin", "Kelvin (K)", "Converts thermal values to absolute temperature in kelvin."),
            ]),
        new(
            UnitQuantityKind.FanSpeed,
            GroupName: "Cooling",
            DisplayName: "Fan speed",
            Description: "Applies to dashboard fan cards, fan history charts, and cooling hardware RPM ranges.",
            DefaultOptionKey: "rpm",
            Options:
            [
                new UnitPreferenceOption("rpm", "RPM", "Displays fan speeds in revolutions per minute."),
                new UnitPreferenceOption("rps", "RPS", "Displays fan speeds in revolutions per second."),
            ]),
        new(
            UnitQuantityKind.ClockFrequency,
            GroupName: "Performance",
            DisplayName: "Clock frequency",
            Description: "Applies to CPU clocks, CPU clock charts, and memory module speed displays.",
            DefaultOptionKey: "megahertz",
            Options:
            [
                new UnitPreferenceOption("megahertz", "MHz", "Displays clock values in megahertz."),
                new UnitPreferenceOption("gigahertz", "GHz", "Displays clock values in gigahertz."),
            ]),
        new(
            UnitQuantityKind.RefreshRate,
            GroupName: "Display",
            DisplayName: "Refresh rate",
            Description: "Applies to monitor refresh-rate displays in device capabilities.",
            DefaultOptionKey: "hertz",
            Options:
            [
                new UnitPreferenceOption("hertz", "Hz", "Displays refresh rates in hertz."),
                new UnitPreferenceOption("kilohertz", "kHz", "Displays refresh rates in kilohertz."),
            ]),
        new(
            UnitQuantityKind.InformationSize,
            GroupName: "Inventory",
            DisplayName: "Information size",
            Description: "Applies to storage capacity, memory totals, module capacities, and CPU cache sizes.",
            DefaultOptionKey: "auto-binary",
            Options:
            [
                new UnitPreferenceOption("auto-binary", "Auto (KiB / MiB / GiB / TiB)", "Chooses a binary unit automatically based on magnitude."),
                new UnitPreferenceOption("kibibyte", "KiB", "Displays all information values in kibibytes."),
                new UnitPreferenceOption("mebibyte", "MiB", "Displays all information values in mebibytes."),
                new UnitPreferenceOption("gibibyte", "GiB", "Displays all information values in gibibytes."),
                new UnitPreferenceOption("tebibyte", "TiB", "Displays all information values in tebibytes."),
            ]),
        new(
            UnitQuantityKind.Voltage,
            GroupName: "Power",
            DisplayName: "Voltage",
            Description: "Applies to battery voltage values and voltage history charts.",
            DefaultOptionKey: "volt",
            Options:
            [
                new UnitPreferenceOption("volt", "V", "Displays voltage in volts."),
                new UnitPreferenceOption("millivolt", "mV", "Displays voltage in millivolts."),
            ]),
        new(
            UnitQuantityKind.Current,
            GroupName: "Power",
            DisplayName: "Current",
            Description: "Applies to battery charging and discharging current values and charts.",
            DefaultOptionKey: "ampere",
            Options:
            [
                new UnitPreferenceOption("ampere", "A", "Displays current in amperes."),
                new UnitPreferenceOption("milliampere", "mA", "Displays current in milliamperes."),
            ]),
        new(
            UnitQuantityKind.ElectricChargeCapacity,
            GroupName: "Power",
            DisplayName: "Battery charge capacity",
            Description: "Applies to battery capacity values such as design capacity, remaining capacity, and last full charge.",
            DefaultOptionKey: "ampere-hour",
            Options:
            [
                new UnitPreferenceOption("ampere-hour", "Ah", "Displays charge capacity in ampere-hours."),
                new UnitPreferenceOption("milliampere-hour", "mAh", "Displays charge capacity in milliampere-hours."),
            ]),
        new(
            UnitQuantityKind.Ratio,
            GroupName: "Usage & Charts",
            DisplayName: "Ratio / utilization",
            Description: "Applies to battery charge percentages, CPU usage, and memory usage displays and charts.",
            DefaultOptionKey: "percent",
            Options:
            [
                new UnitPreferenceOption("percent", "Percent (%)", "Displays ratios as percentages from 0 to 100."),
                new UnitPreferenceOption("fraction", "Fraction (0–1)", "Displays ratios as decimal fractions from 0 to 1."),
            ]),
        new(
            UnitQuantityKind.Length,
            GroupName: "Cooling",
            DisplayName: "Length / dimensions",
            Description: "Applies to fan dimensions and cooling hardware measurements.",
            DefaultOptionKey: "millimeter",
            Options:
            [
                new UnitPreferenceOption("millimeter", "mm", "Displays lengths in millimeters."),
                new UnitPreferenceOption("centimeter", "cm", "Displays lengths in centimeters."),
                new UnitPreferenceOption("inch", "in", "Displays lengths in inches."),
            ]),
        new(
            UnitQuantityKind.Airflow,
            GroupName: "Cooling",
            DisplayName: "Airflow",
            Description: "Applies to desktop fan airflow specifications.",
            DefaultOptionKey: "cfm",
            Options:
            [
                new UnitPreferenceOption("cfm", "CFM", "Displays airflow in cubic feet per minute."),
                new UnitPreferenceOption("cubic-meter-per-hour", "m³/h", "Displays airflow in cubic meters per hour."),
            ]),
        new(
            UnitQuantityKind.BitRate,
            GroupName: "Network",
            DisplayName: "Network link speed",
            Description: "Applies to detected network adapter link-speed displays.",
            DefaultOptionKey: "auto",
            Options:
            [
                new UnitPreferenceOption("auto", "Auto", "Chooses Kbps, Mbps, or Gbps automatically based on magnitude."),
                new UnitPreferenceOption("kilobit-per-second", "Kbps", "Displays link speeds in kilobits per second."),
                new UnitPreferenceOption("megabit-per-second", "Mbps", "Displays link speeds in megabits per second."),
                new UnitPreferenceOption("gigabit-per-second", "Gbps", "Displays link speeds in gigabits per second."),
            ]),
        new(
            UnitQuantityKind.Power,
            GroupName: "Cooling",
            DisplayName: "Power",
            Description: "Applies to cooling hardware power-allocation values when reported.",
            DefaultOptionKey: "watt",
            Options:
            [
                new UnitPreferenceOption("watt", "W", "Displays power values in watts."),
                new UnitPreferenceOption("kilowatt", "kW", "Displays power values in kilowatts."),
            ]),
    ];

    private readonly IReadOnlyDictionary<UnitQuantityKind, UnitPreferenceDefinition> _definitionsByKind =
        new ReadOnlyDictionary<UnitQuantityKind, UnitPreferenceDefinition>(
            DefinitionsArray.ToDictionary(definition => definition.Kind));

    public IReadOnlyList<UnitPreferenceDefinition> Definitions => DefinitionsArray;

    public UnitPreferenceDefinition GetDefinition(UnitQuantityKind kind)
        => _definitionsByKind[kind];

    public bool TryGetDefinition(UnitQuantityKind kind, out UnitPreferenceDefinition definition)
        => _definitionsByKind.TryGetValue(kind, out definition!);

    public bool IsValidOption(UnitQuantityKind kind, string? optionKey)
    {
        if (string.IsNullOrWhiteSpace(optionKey) || !TryGetDefinition(kind, out var definition))
        {
            return false;
        }

        return definition.Options.Any(option => string.Equals(option.Key, optionKey, StringComparison.Ordinal));
    }

    public string GetDefaultOptionKey(UnitQuantityKind kind)
        => GetDefinition(kind).DefaultOptionKey;

    public UserUnitPreferencesSnapshot CreateDefaultSnapshot()
    {
        return new UserUnitPreferencesSnapshot
        {
            SchemaVersion = UserUnitPreferencesSnapshot.CurrentSchemaVersion,
            Entries =
            [
                .. DefinitionsArray.Select(definition => new UserUnitPreferenceEntry(definition.Kind, definition.DefaultOptionKey))
            ]
        };
    }

    public UserUnitPreferencesSnapshot Normalize(UserUnitPreferencesSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return CreateDefaultSnapshot();
        }

        Dictionary<UnitQuantityKind, string> selectedOptions = [];

        foreach (var entry in snapshot.Entries)
        {
            if (IsValidOption(entry.Kind, entry.OptionKey))
            {
                selectedOptions[entry.Kind] = entry.OptionKey;
            }
        }

        return new UserUnitPreferencesSnapshot
        {
            SchemaVersion = UserUnitPreferencesSnapshot.CurrentSchemaVersion,
            Entries =
            [
                .. DefinitionsArray.Select(definition => new UserUnitPreferenceEntry(
                    definition.Kind,
                    selectedOptions.GetValueOrDefault(definition.Kind, definition.DefaultOptionKey)))
            ]
        };
    }
}
