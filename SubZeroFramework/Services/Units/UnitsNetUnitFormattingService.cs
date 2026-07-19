using System.Globalization;

using UnitsNet;
using UnitsNet.Units;

using SubZeroFramework.Services;

namespace SubZeroFramework.Services.Units;

public sealed class UnitsNetUnitFormattingService : IUnitFormattingService
{
    private readonly IUserUnitPreferencesClient _userUnitPreferencesClient;

    public UnitsNetUnitFormattingService(IUserUnitPreferencesClient userUnitPreferencesClient)
    {
        _userUnitPreferencesClient = userUnitPreferencesClient;
    }

    public string TemperatureUnitSuffix => GetTemperatureUnitSuffix();

    public string FanSpeedUnitSuffix => GetFanSpeedUnitSuffix();

    public string ClockFrequencyUnitSuffix => GetClockFrequencyUnitSuffix();

    public string RefreshRateUnitSuffix => GetRefreshRateUnitSuffix();

    public string VoltageUnitSuffix => GetVoltageUnitSuffix();

    public string CurrentUnitSuffix => GetCurrentUnitSuffix();

    public string ChargeCapacityUnitSuffix => GetChargeCapacityUnitSuffix();

    public string RatioUnitSuffix => GetRatioUnitSuffix();

    public string LengthUnitSuffix => GetLengthUnitSuffix();

    public string AirflowUnitSuffix => GetAirflowUnitSuffix();

    public string BitRateUnitSuffix => GetBitRateUnitSuffix();

    public string PowerUnitSuffix => GetPowerUnitSuffix();

    public double RatioAxisMaximum => GetRatioSelectionKey() switch
    {
        "fraction" => 1d,
        "per-mille" => 1_000d,
        "parts-per-million" => 1_000_000d,
        _ => 100d,
    };

    public string FormatTemperature(double? celsius, string unavailableDisplay = "--", int decimals = 0)
    {
        if (celsius is not double value)
        {
            return string.Equals(unavailableDisplay, "--", StringComparison.Ordinal)
                ? $"{unavailableDisplay}{TemperatureUnitSuffix}"
                : unavailableDisplay;
        }

        return $"{ConvertTemperature(value).ToString($"N{Math.Max(decimals, 0)}", CultureInfo.CurrentCulture)}{TemperatureUnitSuffix}";
    }

    public string FormatTemperatureValue(double? celsius, string unavailableDisplay = "--", int decimals = 0)
    {
        return celsius is not double value
            ? unavailableDisplay
            : ConvertTemperature(value).ToString($"N{Math.Max(decimals, 0)}", CultureInfo.CurrentCulture);
    }

    public double ConvertTemperature(double celsius)
    {
        var temperature = Temperature.FromDegreesCelsius(celsius);

        return GetTemperatureSelectionKey() switch
        {
            "fahrenheit" => temperature.DegreesFahrenheit,
            "kelvin" => temperature.Kelvins,
            "rankine" => temperature.DegreesRankine,
            _ => temperature.DegreesCelsius,
        };
    }

    public double ConvertTemperatureToCelsius(double displayValue)
    {
        return GetTemperatureSelectionKey() switch
        {
            "fahrenheit" => Temperature.FromDegreesFahrenheit(displayValue).DegreesCelsius,
            "kelvin" => Temperature.FromKelvins(displayValue).DegreesCelsius,
            "rankine" => Temperature.FromDegreesRankine(displayValue).DegreesCelsius,
            _ => displayValue,
        };
    }

    public string FormatTemperatureAxisLabel(double celsiusValue)
        => FormatTemperature(celsiusValue, decimals: 0);

    public string FormatFanSpeed(double? rpm, string unavailableDisplay = "--", int decimals = -1)
    {
        return rpm is not double value
            ? unavailableDisplay
            : $"{FormatFanSpeedValue(value, unavailableDisplay, decimals)} {FanSpeedUnitSuffix}";
    }

    public string FormatFanSpeedValue(double? rpm, string unavailableDisplay = "--", int decimals = -1)
    {
        return rpm is not double value
            ? unavailableDisplay
            : ConvertFanSpeed(value).ToString($"N{ResolveDecimals(decimals, GetFanSpeedDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertFanSpeed(double rpm)
    {
        var speed = RotationalSpeed.FromRevolutionsPerMinute(rpm);

        return GetFanSpeedSelectionKey() switch
        {
            "rps" => speed.RevolutionsPerSecond,
            "radian-per-second" => speed.RadiansPerSecond,
            _ => speed.RevolutionsPerMinute,
        };
    }

    public string FormatFanSpeedAxisLabel(double rpmValue)
        => FormatFanSpeed(rpmValue, decimals: GetFanSpeedDefaultDecimals());

    public string FormatClockFrequencyMegahertz(double? megahertz, string unavailableDisplay = "--", int decimals = -1)
    {
        return megahertz is not double value
            ? unavailableDisplay
            : $"{FormatClockFrequencyValueMegahertz(value, unavailableDisplay, decimals)} {ClockFrequencyUnitSuffix}";
    }

    public string FormatClockFrequencyValueMegahertz(double? megahertz, string unavailableDisplay = "--", int decimals = -1)
    {
        return megahertz is not double value
            ? unavailableDisplay
            : ConvertClockFrequencyMegahertz(value).ToString($"N{ResolveDecimals(decimals, GetClockFrequencyDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertClockFrequencyMegahertz(double megahertz)
    {
        var frequency = Frequency.FromMegahertz(megahertz);

        return GetClockFrequencySelectionKey() switch
        {
            "hertz" => frequency.Hertz,
            "kilohertz" => frequency.Kilohertz,
            "gigahertz" => frequency.Gigahertz,
            "terahertz" => frequency.Terahertz,
            _ => frequency.Megahertz,
        };
    }

    public string FormatClockFrequencyAxisLabel(double megahertzValue)
        => FormatClockFrequencyMegahertz(megahertzValue, decimals: GetClockFrequencySelectionKey() == "gigahertz" ? 1 : GetClockFrequencyDefaultDecimals());

    public string FormatRefreshRateHertz(double? hertz, string unavailableDisplay = "--", int decimals = -1)
    {
        return hertz is not double value
            ? unavailableDisplay
            : $"{FormatRefreshRateValueHertz(value, unavailableDisplay, decimals)} {RefreshRateUnitSuffix}";
    }

    public string FormatRefreshRateValueHertz(double? hertz, string unavailableDisplay = "--", int decimals = -1)
    {
        return hertz is not double value
            ? unavailableDisplay
            : ConvertRefreshRateHertz(value).ToString($"N{ResolveDecimals(decimals, GetRefreshRateDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertRefreshRateHertz(double hertz)
    {
        var frequency = Frequency.FromHertz(hertz);

        return GetRefreshRateSelectionKey() switch
        {
            "kilohertz" => frequency.Kilohertz,
            "megahertz" => frequency.Megahertz,
            _ => frequency.Hertz,
        };
    }

    public string FormatRefreshRateAxisLabel(double hertzValue)
        => FormatRefreshRateHertz(hertzValue, decimals: GetRefreshRateDefaultDecimals());

    public string FormatInformationBytes(ulong bytes, bool treatZeroAsUnknown = false, string unavailableDisplay = "Unknown")
    {
        if (bytes == 0)
        {
            return treatZeroAsUnknown ? unavailableDisplay : FormatInformationValue(Information.FromBytes(0), GetInformationUnitSelectionForZero());
        }

        var information = Information.FromBytes((double)bytes);
        var selection = GetInformationSelectionKey();

        return selection switch
        {
            "kibibyte" => FormatInformationValue(information, InformationUnit.Kibibyte),
            "mebibyte" => FormatInformationValue(information, InformationUnit.Mebibyte),
            "gibibyte" => FormatInformationValue(information, InformationUnit.Gibibyte),
            "tebibyte" => FormatInformationValue(information, InformationUnit.Tebibyte),
            "kilobyte" => FormatInformationValue(information, InformationUnit.Kilobyte),
            "megabyte" => FormatInformationValue(information, InformationUnit.Megabyte),
            "gigabyte" => FormatInformationValue(information, InformationUnit.Gigabyte),
            "terabyte" => FormatInformationValue(information, InformationUnit.Terabyte),
            _ => FormatInformationValue(information, GetAutomaticInformationUnit(bytes)),
        };
    }

    public string FormatInformationKilobytes(int kilobytes, string unavailableDisplay = "Unavailable")
    {
        return kilobytes <= 0
            ? unavailableDisplay
            : FormatInformationBytes(checked((ulong)kilobytes * 1024UL));
    }

    public string FormatVoltage(double? volts, string unavailableDisplay = "--", int decimals = -1)
    {
        return volts is not double value
            ? unavailableDisplay
            : $"{FormatVoltageValue(value, unavailableDisplay, decimals)} {VoltageUnitSuffix}";
    }

    public string FormatVoltageValue(double? volts, string unavailableDisplay = "--", int decimals = -1)
    {
        return volts is not double value
            ? unavailableDisplay
            : ConvertVoltage(value).ToString($"N{ResolveDecimals(decimals, GetVoltageDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertVoltage(double volts)
    {
        var potential = ElectricPotential.FromVolts(volts);

        return GetVoltageSelectionKey() switch
        {
            "millivolt" => potential.Millivolts,
            "microvolt" => potential.Microvolts,
            "kilovolt" => potential.Kilovolts,
            _ => potential.Volts,
        };
    }

    public string FormatVoltageAxisLabel(double voltsValue)
        => FormatVoltage(voltsValue, decimals: GetVoltageDefaultDecimals());

    public string FormatCurrent(double? amperes, string unavailableDisplay = "--", int decimals = -1)
    {
        return amperes is not double value
            ? unavailableDisplay
            : $"{FormatCurrentValue(value, unavailableDisplay, decimals)} {CurrentUnitSuffix}";
    }

    public string FormatCurrentValue(double? amperes, string unavailableDisplay = "--", int decimals = -1)
    {
        return amperes is not double value
            ? unavailableDisplay
            : ConvertCurrent(value).ToString($"N{ResolveDecimals(decimals, GetCurrentDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertCurrent(double amperes)
    {
        var current = ElectricCurrent.FromAmperes(amperes);

        return GetCurrentSelectionKey() switch
        {
            "milliampere" => current.Milliamperes,
            "microampere" => current.Microamperes,
            _ => current.Amperes,
        };
    }

    public string FormatCurrentAxisLabel(double amperesValue)
        => FormatCurrent(amperesValue, decimals: GetCurrentSelectionKey() == "ampere" ? 1 : GetCurrentDefaultDecimals());

    public string FormatChargeCapacity(double? ampereHours, string unavailableDisplay = "--", int decimals = -1)
    {
        return ampereHours is not double value
            ? unavailableDisplay
            : $"{FormatChargeCapacityValue(value, unavailableDisplay, decimals)} {ChargeCapacityUnitSuffix}";
    }

    public string FormatChargeCapacityValue(double? ampereHours, string unavailableDisplay = "--", int decimals = -1)
    {
        return ampereHours is not double value
            ? unavailableDisplay
            : ConvertChargeCapacity(value).ToString($"N{ResolveDecimals(decimals, GetChargeCapacityDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertChargeCapacity(double ampereHours)
    {
        var charge = ElectricCharge.FromAmpereHours(ampereHours);

        return GetChargeCapacitySelectionKey() switch
        {
            "milliampere-hour" => charge.MilliampereHours,
            "coulomb" => charge.Coulombs,
            _ => charge.AmpereHours,
        };
    }

    public string FormatChargeCapacityAxisLabel(double ampereHoursValue)
        => FormatChargeCapacity(ampereHoursValue, decimals: GetChargeCapacityDefaultDecimals());

    public string FormatRatio(double? percent, string unavailableDisplay = "--", int decimals = -1)
    {
        if (percent is not double value)
        {
            return unavailableDisplay;
        }

        var formattedValue = FormatRatioValue(value, unavailableDisplay, decimals);
        return string.Equals(GetRatioSelectionKey(), "fraction", StringComparison.Ordinal)
            ? formattedValue
            : $"{formattedValue}{RatioUnitSuffix}";
    }

    public string FormatRatioValue(double? percent, string unavailableDisplay = "--", int decimals = -1)
    {
        if (percent is not double value)
        {
            return unavailableDisplay;
        }

        return ConvertRatio(value).ToString($"N{ResolveDecimals(decimals, GetRatioDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertRatio(double percent)
    {
        var ratio = Ratio.FromPercent(percent);

        return GetRatioSelectionKey() switch
        {
            "fraction" => ratio.DecimalFractions,
            "per-mille" => ratio.PartsPerThousand,
            "parts-per-million" => ratio.PartsPerMillion,
            _ => ratio.Percent,
        };
    }

    public string FormatRatioAxisLabel(double percentValue)
        => FormatRatio(percentValue, decimals: GetRatioDefaultDecimals());

    public string FormatLengthMillimeters(double? millimeters, string unavailableDisplay = "--", int decimals = -1)
    {
        return millimeters is not double value
            ? unavailableDisplay
            : $"{FormatLengthValueMillimeters(value, unavailableDisplay, decimals)} {LengthUnitSuffix}";
    }

    public string FormatLengthValueMillimeters(double? millimeters, string unavailableDisplay = "--", int decimals = -1)
    {
        return millimeters is not double value
            ? unavailableDisplay
            : ConvertLengthMillimeters(value).ToString($"N{ResolveDecimals(decimals, GetLengthDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertLengthMillimeters(double millimeters)
    {
        var length = Length.FromMillimeters(millimeters);

        return GetLengthSelectionKey() switch
        {
            "centimeter" => length.Centimeters,
            "meter" => length.Meters,
            "inch" => length.Inches,
            "foot" => length.Feet,
            _ => length.Millimeters,
        };
    }

    public string FormatAirflowCfm(double? cfm, string unavailableDisplay = "--", int decimals = -1)
    {
        return cfm is not double value
            ? unavailableDisplay
            : $"{FormatAirflowValueCfm(value, unavailableDisplay, decimals)} {AirflowUnitSuffix}";
    }

    public string FormatAirflowValueCfm(double? cfm, string unavailableDisplay = "--", int decimals = -1)
    {
        return cfm is not double value
            ? unavailableDisplay
            : ConvertAirflowCfm(value).ToString($"N{ResolveDecimals(decimals, GetAirflowDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertAirflowCfm(double cfm)
    {
        var flow = VolumeFlow.FromCubicFeetPerMinute(cfm);

        return GetAirflowSelectionKey() switch
        {
            "cubic-meter-per-hour" => flow.CubicMetersPerHour,
            "liter-per-second" => flow.LitersPerSecond,
            "liter-per-minute" => flow.LitersPerMinute,
            _ => flow.CubicFeetPerMinute,
        };
    }

    public string FormatBitRateBitsPerSecond(double? bitsPerSecond, string unavailableDisplay = "--", int decimals = -1)
    {
        return bitsPerSecond is not double value || value <= 0d
            ? unavailableDisplay
            : $"{FormatBitRateValueBitsPerSecond(value, unavailableDisplay, decimals)} {GetBitRateDisplayUnitSuffix(value)}";
    }

    public string FormatBitRateValueBitsPerSecond(double? bitsPerSecond, string unavailableDisplay = "--", int decimals = -1)
    {
        if (bitsPerSecond is not double value || value <= 0d)
        {
            return unavailableDisplay;
        }

        return ConvertBitRateBitsPerSecond(value).ToString($"N{ResolveDecimals(decimals, GetBitRateDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertBitRateBitsPerSecond(double bitsPerSecond)
    {
        var bitRate = BitRate.FromBitsPerSecond(bitsPerSecond);

        return GetBitRateSelectionKey() switch
        {
            "bit-per-second" => (double)bitRate.BitsPerSecond,
            "kilobit-per-second" => (double)bitRate.KilobitsPerSecond,
            "megabit-per-second" => (double)bitRate.MegabitsPerSecond,
            "gigabit-per-second" => (double)bitRate.GigabitsPerSecond,
            "terabit-per-second" => (double)bitRate.TerabitsPerSecond,
            _ => GetAutomaticBitRateValue(bitRate, bitsPerSecond),
        };
    }

    public string FormatPowerWatts(double? watts, string unavailableDisplay = "--", int decimals = -1)
    {
        return watts is not double value
            ? unavailableDisplay
            : $"{FormatPowerValueWatts(value, unavailableDisplay, decimals)} {PowerUnitSuffix}";
    }

    public string FormatPowerValueWatts(double? watts, string unavailableDisplay = "--", int decimals = -1)
    {
        return watts is not double value
            ? unavailableDisplay
            : ConvertPowerWatts(value).ToString($"N{ResolveDecimals(decimals, GetPowerDefaultDecimals())}", CultureInfo.CurrentCulture);
    }

    public double ConvertPowerWatts(double watts)
    {
        var power = Power.FromWatts(watts);

        return GetPowerSelectionKey() switch
        {
            "milliwatt" => (double)power.Milliwatts,
            "kilowatt" => (double)power.Kilowatts,
            "mechanical-horsepower" => (double)power.MechanicalHorsepower,
            _ => (double)power.Watts,
        };
    }

    public string FormatAcousticLevelDecibels(double? decibels, string unavailableDisplay = "--", int decimals = -1, bool includeAWeighting = true)
    {
        if (decibels is not double value)
        {
            return unavailableDisplay;
        }

        var level = Level.FromDecibels(value);
        var resolvedValue = (double)level.Decibels;
        var defaultDecimals = IsWholeNumber(resolvedValue) ? 0 : 1;
        var unitSuffix = includeAWeighting ? "dB(A)" : "dB";
        return $"{resolvedValue.ToString($"N{ResolveDecimals(decimals, defaultDecimals)}", CultureInfo.CurrentCulture)} {unitSuffix}";
    }

    private string GetTemperatureUnitSuffix()
    {
        return GetTemperatureSelectionKey() switch
        {
            "fahrenheit" => "°F",
            "kelvin" => "K",
            "rankine" => "°R",
            _ => "°C",
        };
    }

    private string GetFanSpeedUnitSuffix()
    {
        return GetFanSpeedSelectionKey() switch
        {
            "rps" => "RPS",
            "radian-per-second" => "rad/s",
            _ => "RPM",
        };
    }

    private int GetFanSpeedDefaultDecimals()
    {
        return GetFanSpeedSelectionKey() switch
        {
            "rps" => 1,
            "radian-per-second" => 1,
            _ => 0,
        };
    }

    private string GetClockFrequencyUnitSuffix()
    {
        return GetClockFrequencySelectionKey() switch
        {
            "hertz" => "Hz",
            "kilohertz" => "kHz",
            "gigahertz" => "GHz",
            "terahertz" => "THz",
            _ => "MHz",
        };
    }

    private int GetClockFrequencyDefaultDecimals()
    {
        return GetClockFrequencySelectionKey() switch
        {
            "gigahertz" => 2,
            "terahertz" => 5,
            _ => 0,
        };
    }

    private string GetRefreshRateUnitSuffix()
    {
        return GetRefreshRateSelectionKey() switch
        {
            "kilohertz" => "kHz",
            "megahertz" => "MHz",
            _ => "Hz",
        };
    }

    private int GetRefreshRateDefaultDecimals()
    {
        return GetRefreshRateSelectionKey() switch
        {
            "kilohertz" => 2,
            "megahertz" => 4,
            _ => 0,
        };
    }

    private string GetVoltageUnitSuffix()
    {
        return GetVoltageSelectionKey() switch
        {
            "millivolt" => "mV",
            "microvolt" => "µV",
            "kilovolt" => "kV",
            _ => "V",
        };
    }

    private int GetVoltageDefaultDecimals()
    {
        return GetVoltageSelectionKey() switch
        {
            "millivolt" => 0,
            "microvolt" => 0,
            "kilovolt" => 4,
            _ => 1,
        };
    }

    private string GetCurrentUnitSuffix()
    {
        return GetCurrentSelectionKey() switch
        {
            "milliampere" => "mA",
            "microampere" => "µA",
            _ => "A",
        };
    }

    private int GetCurrentDefaultDecimals()
    {
        return GetCurrentSelectionKey() switch
        {
            "milliampere" => 0,
            "microampere" => 0,
            _ => 2,
        };
    }

    private string GetChargeCapacityUnitSuffix()
    {
        return GetChargeCapacitySelectionKey() switch
        {
            "milliampere-hour" => "mAh",
            "coulomb" => "C",
            _ => "Ah",
        };
    }

    private int GetChargeCapacityDefaultDecimals()
    {
        return GetChargeCapacitySelectionKey() switch
        {
            "milliampere-hour" => 0,
            "coulomb" => 0,
            _ => 1,
        };
    }

    private string GetRatioUnitSuffix()
    {
        return GetRatioSelectionKey() switch
        {
            "fraction" => "ratio",
            "per-mille" => "‰",
            "parts-per-million" => " ppm",
            _ => "%",
        };
    }

    private int GetRatioDefaultDecimals()
    {
        return GetRatioSelectionKey() switch
        {
            "fraction" => 2,
            "parts-per-million" => 0,
            "per-mille" => 0,
            _ => 0,
        };
    }

    private string GetLengthUnitSuffix()
    {
        return GetLengthSelectionKey() switch
        {
            "centimeter" => "cm",
            "meter" => "m",
            "inch" => "in",
            "foot" => "ft",
            _ => "mm",
        };
    }

    private int GetLengthDefaultDecimals()
    {
        return GetLengthSelectionKey() switch
        {
            "centimeter" => 2,
            "meter" => 3,
            "inch" => 2,
            "foot" => 3,
            _ => 0,
        };
    }

    private string GetAirflowUnitSuffix()
    {
        return GetAirflowSelectionKey() switch
        {
            "cubic-meter-per-hour" => "m³/h",
            "liter-per-second" => "L/s",
            "liter-per-minute" => "L/min",
            _ => "CFM",
        };
    }

    private int GetAirflowDefaultDecimals()
    {
        return GetAirflowSelectionKey() switch
        {
            "liter-per-minute" => 0,
            _ => 1,
        };
    }

    private string GetBitRateUnitSuffix()
    {
        return GetBitRateSelectionKey() switch
        {
            "bit-per-second" => "bps",
            "kilobit-per-second" => "Kbps",
            "megabit-per-second" => "Mbps",
            "gigabit-per-second" => "Gbps",
            "terabit-per-second" => "Tbps",
            _ => "auto",
        };
    }

    private int GetBitRateDefaultDecimals()
    {
        return GetBitRateSelectionKey() switch
        {
            "auto" => 1,
            "terabit-per-second" => 3,
            _ => 0,
        };
    }

    private string GetPowerUnitSuffix()
    {
        return GetPowerSelectionKey() switch
        {
            "milliwatt" => "mW",
            "kilowatt" => "kW",
            "mechanical-horsepower" => "hp",
            _ => "W",
        };
    }

    private int GetPowerDefaultDecimals()
    {
        return GetPowerSelectionKey() switch
        {
            "milliwatt" => 0,
            "kilowatt" => 2,
            "mechanical-horsepower" => 2,
            _ => 0,
        };
    }

    private string GetTemperatureSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.Temperature, "celsius");

    private string GetFanSpeedSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.FanSpeed, "rpm");

    private string GetClockFrequencySelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.ClockFrequency, "megahertz");

    private string GetRefreshRateSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.RefreshRate, "hertz");

    private string GetInformationSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.InformationSize, "auto-binary");

    private string GetVoltageSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.Voltage, "volt");

    private string GetCurrentSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.Current, "ampere");

    private string GetChargeCapacitySelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.ElectricChargeCapacity, "ampere-hour");

    private string GetRatioSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.Ratio, "percent");

    private string GetLengthSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.Length, "millimeter");

    private string GetAirflowSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.Airflow, "cfm");

    private string GetBitRateSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.BitRate, "auto");

    private string GetPowerSelectionKey()
        => _userUnitPreferencesClient.CurrentPreferences.GetOptionKey(UnitQuantityKind.Power, "watt");

    private static InformationUnit GetAutomaticInformationUnit(ulong bytes)
    {
        if (bytes >= 1024UL * 1024UL * 1024UL * 1024UL)
        {
            return InformationUnit.Tebibyte;
        }

        if (bytes >= 1024UL * 1024UL * 1024UL)
        {
            return InformationUnit.Gibibyte;
        }

        if (bytes >= 1024UL * 1024UL)
        {
            return InformationUnit.Mebibyte;
        }

        if (bytes >= 1024UL)
        {
            return InformationUnit.Kibibyte;
        }

        return InformationUnit.Byte;
    }

    private static InformationUnit GetInformationUnitSelectionForZero()
        => InformationUnit.Byte;

    private static int ResolveDecimals(int requestedDecimals, int defaultDecimals)
        => Math.Max(requestedDecimals < 0 ? defaultDecimals : requestedDecimals, 0);

    private static bool IsWholeNumber(double value)
        => Math.Abs(value - Math.Round(value, 0, MidpointRounding.AwayFromZero)) < 0.000_001d;

    private static double GetAutomaticBitRateValue(BitRate bitRate, double bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000_000d)
        {
            return (double)bitRate.GigabitsPerSecond;
        }

        if (bitsPerSecond >= 1_000_000d)
        {
            return (double)bitRate.MegabitsPerSecond;
        }

        if (bitsPerSecond >= 1_000d)
        {
            return (double)bitRate.KilobitsPerSecond;
        }

        return (double)bitRate.BitsPerSecond;
    }

    private string GetBitRateDisplayUnitSuffix(double bitsPerSecond)
    {
        return GetBitRateSelectionKey() switch
        {
            "bit-per-second" => "bps",
            "kilobit-per-second" => "Kbps",
            "megabit-per-second" => "Mbps",
            "gigabit-per-second" => "Gbps",
            "terabit-per-second" => "Tbps",
            _ => bitsPerSecond switch
            {
                >= 1_000_000_000d => "Gbps",
                >= 1_000_000d => "Mbps",
                >= 1_000d => "Kbps",
                _ => "bps",
            },
        };
    }

    private static string FormatInformationValue(Information information, InformationUnit unit)
    {
        var value = unit switch
        {
            InformationUnit.Kibibyte => information.Kibibytes,
            InformationUnit.Mebibyte => information.Mebibytes,
            InformationUnit.Gibibyte => information.Gibibytes,
            InformationUnit.Tebibyte => information.Tebibytes,
            InformationUnit.Kilobyte => information.Kilobytes,
            InformationUnit.Megabyte => information.Megabytes,
            InformationUnit.Gigabyte => information.Gigabytes,
            InformationUnit.Terabyte => information.Terabytes,
            _ => information.Bytes,
        };

        var format = unit == InformationUnit.Byte
            ? "N0"
            : "0.##";

        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {GetInformationUnitAbbreviation(unit)}";
    }

    private static string GetInformationUnitAbbreviation(InformationUnit unit)
    {
        return unit switch
        {
            InformationUnit.Kibibyte => "KiB",
            InformationUnit.Mebibyte => "MiB",
            InformationUnit.Gibibyte => "GiB",
            InformationUnit.Tebibyte => "TiB",
            InformationUnit.Kilobyte => "KB",
            InformationUnit.Megabyte => "MB",
            InformationUnit.Gigabyte => "GB",
            InformationUnit.Terabyte => "TB",
            _ => "B",
        };
    }
}
