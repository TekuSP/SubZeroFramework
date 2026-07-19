namespace SubZeroFramework.Services.Units;

/// <summary>
/// Presentation metadata for the Display-units settings rows: which Material Design icon represents each
/// quantity, a short row subtitle, and the compact segment label for each unit option. Lives in Core (like
/// <c>FrameworkModuleDisplay</c>) so the icon names are testable against the Material.Icons enum.
/// </summary>
public static class UnitPreferenceDisplay
{
    /// <summary>Material icon name (a <c>MaterialIconKind</c> member name) for the quantity row.</summary>
    public static string IconName(UnitQuantityKind kind) => kind switch
    {
        UnitQuantityKind.Temperature => "Thermometer",
        UnitQuantityKind.FanSpeed => "Fan",
        UnitQuantityKind.ClockFrequency => "SineWave",
        UnitQuantityKind.RefreshRate => "Television",
        UnitQuantityKind.InformationSize => "Database",
        UnitQuantityKind.Voltage => "FlashOutline",
        UnitQuantityKind.Current => "CurrentDc",
        UnitQuantityKind.ElectricChargeCapacity => "BatteryChargingHigh",
        UnitQuantityKind.Ratio => "Percent",
        UnitQuantityKind.Length => "TapeMeasure",
        UnitQuantityKind.Airflow => "WeatherWindy",
        UnitQuantityKind.BitRate => "Ethernet",
        UnitQuantityKind.Power => "LightningBolt",
        _ => "Ruler",
    };

    /// <summary>Short subtitle describing where the quantity appears, shown before the live sample value.</summary>
    public static string ShortDescription(UnitQuantityKind kind) => kind switch
    {
        UnitQuantityKind.Temperature => "Thermal cards & history charts",
        UnitQuantityKind.FanSpeed => "Fan cards & cooling ranges",
        UnitQuantityKind.ClockFrequency => "CPU clocks & memory speed",
        UnitQuantityKind.RefreshRate => "Monitor refresh rates",
        UnitQuantityKind.InformationSize => "Storage, memory & caches",
        UnitQuantityKind.Voltage => "Battery voltage & charts",
        UnitQuantityKind.Current => "Battery charge & discharge",
        UnitQuantityKind.ElectricChargeCapacity => "Battery design & remaining capacity",
        UnitQuantityKind.Ratio => "Battery, CPU & memory usage",
        UnitQuantityKind.Length => "Fan & cooling dimensions",
        UnitQuantityKind.Airflow => "Desktop fan airflow",
        UnitQuantityKind.BitRate => "Network link speeds",
        UnitQuantityKind.Power => "Power & cooling allocation",
        _ => string.Empty,
    };

    /// <summary>Compact label for a unit option, sized for the segmented picker pills.</summary>
    public static string ShortOptionLabel(UnitQuantityKind kind, string optionKey) => (kind, optionKey) switch
    {
        (UnitQuantityKind.Temperature, "celsius") => "°C",
        (UnitQuantityKind.Temperature, "fahrenheit") => "°F",
        (UnitQuantityKind.Temperature, "kelvin") => "K",
        (UnitQuantityKind.Temperature, "rankine") => "°R",
        (UnitQuantityKind.FanSpeed, "rpm") => "RPM",
        (UnitQuantityKind.FanSpeed, "rps") => "rev/s",
        (UnitQuantityKind.FanSpeed, "radian-per-second") => "rad/s",
        (UnitQuantityKind.ClockFrequency, "hertz") => "Hz",
        (UnitQuantityKind.ClockFrequency, "kilohertz") => "kHz",
        (UnitQuantityKind.ClockFrequency, "megahertz") => "MHz",
        (UnitQuantityKind.ClockFrequency, "gigahertz") => "GHz",
        (UnitQuantityKind.ClockFrequency, "terahertz") => "THz",
        (UnitQuantityKind.RefreshRate, "hertz") => "Hz",
        (UnitQuantityKind.RefreshRate, "kilohertz") => "kHz",
        (UnitQuantityKind.RefreshRate, "megahertz") => "MHz",
        (UnitQuantityKind.InformationSize, "auto-binary") => "Auto",
        (UnitQuantityKind.InformationSize, "kibibyte") => "KiB",
        (UnitQuantityKind.InformationSize, "mebibyte") => "MiB",
        (UnitQuantityKind.InformationSize, "gibibyte") => "GiB",
        (UnitQuantityKind.InformationSize, "tebibyte") => "TiB",
        (UnitQuantityKind.InformationSize, "kilobyte") => "KB",
        (UnitQuantityKind.InformationSize, "megabyte") => "MB",
        (UnitQuantityKind.InformationSize, "gigabyte") => "GB",
        (UnitQuantityKind.InformationSize, "terabyte") => "TB",
        (UnitQuantityKind.Voltage, "volt") => "V",
        (UnitQuantityKind.Voltage, "millivolt") => "mV",
        (UnitQuantityKind.Voltage, "microvolt") => "µV",
        (UnitQuantityKind.Voltage, "kilovolt") => "kV",
        (UnitQuantityKind.Current, "ampere") => "A",
        (UnitQuantityKind.Current, "milliampere") => "mA",
        (UnitQuantityKind.Current, "microampere") => "µA",
        (UnitQuantityKind.ElectricChargeCapacity, "ampere-hour") => "Ah",
        (UnitQuantityKind.ElectricChargeCapacity, "milliampere-hour") => "mAh",
        (UnitQuantityKind.ElectricChargeCapacity, "coulomb") => "C",
        (UnitQuantityKind.Ratio, "percent") => "%",
        (UnitQuantityKind.Ratio, "fraction") => "0–1",
        (UnitQuantityKind.Ratio, "per-mille") => "‰",
        (UnitQuantityKind.Ratio, "parts-per-million") => "ppm",
        (UnitQuantityKind.Length, "millimeter") => "mm",
        (UnitQuantityKind.Length, "centimeter") => "cm",
        (UnitQuantityKind.Length, "meter") => "m",
        (UnitQuantityKind.Length, "inch") => "in",
        (UnitQuantityKind.Length, "foot") => "ft",
        (UnitQuantityKind.Airflow, "cfm") => "CFM",
        (UnitQuantityKind.Airflow, "cubic-meter-per-hour") => "m³/h",
        (UnitQuantityKind.Airflow, "liter-per-second") => "L/s",
        (UnitQuantityKind.Airflow, "liter-per-minute") => "L/min",
        (UnitQuantityKind.BitRate, "auto") => "Auto",
        (UnitQuantityKind.BitRate, "bit-per-second") => "bps",
        (UnitQuantityKind.BitRate, "kilobit-per-second") => "Kbps",
        (UnitQuantityKind.BitRate, "megabit-per-second") => "Mbps",
        (UnitQuantityKind.BitRate, "gigabit-per-second") => "Gbps",
        (UnitQuantityKind.BitRate, "terabit-per-second") => "Tbps",
        (UnitQuantityKind.Power, "watt") => "W",
        (UnitQuantityKind.Power, "milliwatt") => "mW",
        (UnitQuantityKind.Power, "kilowatt") => "kW",
        (UnitQuantityKind.Power, "mechanical-horsepower") => "hp",
        _ => optionKey,
    };
}
