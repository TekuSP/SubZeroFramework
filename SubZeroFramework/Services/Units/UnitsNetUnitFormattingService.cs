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

    public double RatioAxisMaximum => string.Equals(GetRatioSelectionKey(), "fraction", StringComparison.Ordinal)
        ? 1d
        : 100d;

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
                _ => temperature.DegreesCelsius,
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
                : ConvertFanSpeed(value).ToString($"N{ResolveDecimals(decimals, GetFanSpeedSelectionKey() == "rps" ? 1 : 0)}", CultureInfo.CurrentCulture);
        }

        public double ConvertFanSpeed(double rpm)
        {
            var speed = RotationalSpeed.FromRevolutionsPerMinute(rpm);

            return GetFanSpeedSelectionKey() switch
            {
                "rps" => speed.RevolutionsPerSecond,
                _ => speed.RevolutionsPerMinute,
            };
        }

        public string FormatFanSpeedAxisLabel(double rpmValue)
            => FormatFanSpeed(rpmValue, decimals: GetFanSpeedSelectionKey() == "rps" ? 1 : 0);

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
                : ConvertClockFrequencyMegahertz(value).ToString($"N{ResolveDecimals(decimals, GetClockFrequencySelectionKey() == "gigahertz" ? 2 : 0)}", CultureInfo.CurrentCulture);
        }

        public double ConvertClockFrequencyMegahertz(double megahertz)
        {
            var frequency = Frequency.FromMegahertz(megahertz);

            return GetClockFrequencySelectionKey() switch
            {
                "gigahertz" => frequency.Gigahertz,
                _ => frequency.Megahertz,
            };
        }

        public string FormatClockFrequencyAxisLabel(double megahertzValue)
            => FormatClockFrequencyMegahertz(megahertzValue, decimals: GetClockFrequencySelectionKey() == "gigahertz" ? 1 : 0);

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
                : ConvertRefreshRateHertz(value).ToString($"N{ResolveDecimals(decimals, GetRefreshRateSelectionKey() == "kilohertz" ? 2 : 0)}", CultureInfo.CurrentCulture);
        }

        public double ConvertRefreshRateHertz(double hertz)
        {
            var frequency = Frequency.FromHertz(hertz);

            return GetRefreshRateSelectionKey() switch
            {
                "kilohertz" => frequency.Kilohertz,
                _ => frequency.Hertz,
            };
        }

        public string FormatRefreshRateAxisLabel(double hertzValue)
            => FormatRefreshRateHertz(hertzValue, decimals: GetRefreshRateSelectionKey() == "kilohertz" ? 2 : 0);

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
            : ConvertVoltage(value).ToString($"N{ResolveDecimals(decimals, GetVoltageSelectionKey() == "millivolt" ? 0 : 1)}", CultureInfo.CurrentCulture);
    }

    public double ConvertVoltage(double volts)
    {
        var potential = ElectricPotential.FromVolts(volts);

        return GetVoltageSelectionKey() switch
        {
            "millivolt" => potential.Millivolts,
            _ => potential.Volts,
        };
    }

    public string FormatVoltageAxisLabel(double voltsValue)
        => FormatVoltage(voltsValue, decimals: GetVoltageSelectionKey() == "millivolt" ? 0 : 1);

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
            : ConvertCurrent(value).ToString($"N{ResolveDecimals(decimals, GetCurrentSelectionKey() == "milliampere" ? 0 : 2)}", CultureInfo.CurrentCulture);
    }

    public double ConvertCurrent(double amperes)
    {
        var current = ElectricCurrent.FromAmperes(amperes);

        return GetCurrentSelectionKey() switch
        {
            "milliampere" => current.Milliamperes,
            _ => current.Amperes,
        };
    }

    public string FormatCurrentAxisLabel(double amperesValue)
        => FormatCurrent(amperesValue, decimals: GetCurrentSelectionKey() == "milliampere" ? 0 : 1);

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
            : ConvertChargeCapacity(value).ToString($"N{ResolveDecimals(decimals, GetChargeCapacitySelectionKey() == "milliampere-hour" ? 0 : 1)}", CultureInfo.CurrentCulture);
    }

    public double ConvertChargeCapacity(double ampereHours)
    {
        var charge = ElectricCharge.FromAmpereHours(ampereHours);

        return GetChargeCapacitySelectionKey() switch
        {
            "milliampere-hour" => charge.MilliampereHours,
            _ => charge.AmpereHours,
        };
    }

    public string FormatChargeCapacityAxisLabel(double ampereHoursValue)
        => FormatChargeCapacity(ampereHoursValue, decimals: GetChargeCapacitySelectionKey() == "milliampere-hour" ? 0 : 1);

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

        var defaultDecimals = string.Equals(GetRatioSelectionKey(), "fraction", StringComparison.Ordinal) ? 2 : 0;
        return ConvertRatio(value).ToString($"N{ResolveDecimals(decimals, defaultDecimals)}", CultureInfo.CurrentCulture);
    }

    public double ConvertRatio(double percent)
    {
        var ratio = Ratio.FromPercent(percent);

        return GetRatioSelectionKey() switch
        {
            "fraction" => ratio.DecimalFractions,
            _ => ratio.Percent,
        };
    }

    public string FormatRatioAxisLabel(double percentValue)
        => FormatRatio(percentValue, decimals: string.Equals(GetRatioSelectionKey(), "fraction", StringComparison.Ordinal) ? 2 : 0);

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
            : ConvertLengthMillimeters(value).ToString($"N{ResolveDecimals(decimals, GetLengthSelectionKey() == "millimeter" ? 0 : 2)}", CultureInfo.CurrentCulture);
    }

    public double ConvertLengthMillimeters(double millimeters)
    {
        var length = Length.FromMillimeters(millimeters);

        return GetLengthSelectionKey() switch
        {
            "centimeter" => length.Centimeters,
            "inch" => length.Inches,
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
            : ConvertAirflowCfm(value).ToString($"N{ResolveDecimals(decimals, GetAirflowSelectionKey() == "cubic-meter-per-hour" ? 1 : 1)}", CultureInfo.CurrentCulture);
    }

    public double ConvertAirflowCfm(double cfm)
    {
        var flow = VolumeFlow.FromCubicFeetPerMinute(cfm);

        return GetAirflowSelectionKey() switch
        {
            "cubic-meter-per-hour" => flow.CubicMetersPerHour,
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

        var defaultDecimals = string.Equals(GetBitRateSelectionKey(), "auto", StringComparison.Ordinal) ? 1 : 0;
        return ConvertBitRateBitsPerSecond(value).ToString($"N{ResolveDecimals(decimals, defaultDecimals)}", CultureInfo.CurrentCulture);
    }

    public double ConvertBitRateBitsPerSecond(double bitsPerSecond)
    {
        var bitRate = BitRate.FromBitsPerSecond(bitsPerSecond);

        return GetBitRateSelectionKey() switch
        {
            "kilobit-per-second" => (double)bitRate.KilobitsPerSecond,
            "megabit-per-second" => (double)bitRate.MegabitsPerSecond,
            "gigabit-per-second" => (double)bitRate.GigabitsPerSecond,
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
            : ConvertPowerWatts(value).ToString($"N{ResolveDecimals(decimals, GetPowerSelectionKey() == "kilowatt" ? 2 : 0)}", CultureInfo.CurrentCulture);
    }

    public double ConvertPowerWatts(double watts)
    {
        var power = Power.FromWatts(watts);

        return GetPowerSelectionKey() switch
        {
            "kilowatt" => (double)power.Kilowatts,
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
            _ => "°C",
        };
    }

    private string GetFanSpeedUnitSuffix()
        => string.Equals(GetFanSpeedSelectionKey(), "rps", StringComparison.Ordinal) ? "RPS" : "RPM";

    private string GetClockFrequencyUnitSuffix()
        => string.Equals(GetClockFrequencySelectionKey(), "gigahertz", StringComparison.Ordinal) ? "GHz" : "MHz";

    private string GetRefreshRateUnitSuffix()
        => string.Equals(GetRefreshRateSelectionKey(), "kilohertz", StringComparison.Ordinal) ? "kHz" : "Hz";

    private string GetVoltageUnitSuffix()
        => string.Equals(GetVoltageSelectionKey(), "millivolt", StringComparison.Ordinal) ? "mV" : "V";

    private string GetCurrentUnitSuffix()
        => string.Equals(GetCurrentSelectionKey(), "milliampere", StringComparison.Ordinal) ? "mA" : "A";

    private string GetChargeCapacityUnitSuffix()
        => string.Equals(GetChargeCapacitySelectionKey(), "milliampere-hour", StringComparison.Ordinal) ? "mAh" : "Ah";

    private string GetRatioUnitSuffix()
        => string.Equals(GetRatioSelectionKey(), "fraction", StringComparison.Ordinal) ? "ratio" : "%";

    private string GetLengthUnitSuffix()
    {
        return GetLengthSelectionKey() switch
        {
            "centimeter" => "cm",
            "inch" => "in",
            _ => "mm",
        };
    }

    private string GetAirflowUnitSuffix()
        => string.Equals(GetAirflowSelectionKey(), "cubic-meter-per-hour", StringComparison.Ordinal) ? "m³/h" : "CFM";

    private string GetBitRateUnitSuffix()
    {
        return GetBitRateSelectionKey() switch
        {
            "kilobit-per-second" => "Kbps",
            "megabit-per-second" => "Mbps",
            "gigabit-per-second" => "Gbps",
            _ => "auto",
        };
    }

    private string GetPowerUnitSuffix()
        => string.Equals(GetPowerSelectionKey(), "kilowatt", StringComparison.Ordinal) ? "kW" : "W";

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
            "kilobit-per-second" => "Kbps",
            "megabit-per-second" => "Mbps",
            "gigabit-per-second" => "Gbps",
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
            _ => "B",
        };
    }
}
