namespace SubZeroFramework.Services.Units;

public interface IUnitFormattingService
{
    string TemperatureUnitSuffix { get; }

    string FanSpeedUnitSuffix { get; }

    string ClockFrequencyUnitSuffix { get; }

    string RefreshRateUnitSuffix { get; }

    string VoltageUnitSuffix { get; }

    string CurrentUnitSuffix { get; }

    string ChargeCapacityUnitSuffix { get; }

    string EnergyUnitSuffix { get; }

    string RatioUnitSuffix { get; }

    string LengthUnitSuffix { get; }

    string AirflowUnitSuffix { get; }

    string BitRateUnitSuffix { get; }

    string PowerUnitSuffix { get; }

    double RatioAxisMaximum { get; }

    string FormatTemperature(double? celsius, string unavailableDisplay = "--", int decimals = 0);

    string FormatTemperatureValue(double? celsius, string unavailableDisplay = "--", int decimals = 0);

    double ConvertTemperature(double celsius);

    /// <summary>Converts a value in the user's chosen temperature unit back to canonical Celsius (the inverse of <see cref="ConvertTemperature"/>).</summary>
    double ConvertTemperatureToCelsius(double displayValue);

    /// <summary>
    /// Converts a temperature DIFFERENCE — an axis step, a band width, a tolerance — to the user's unit.
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="ConvertTemperature"/>, and the difference is a real bug generator: that one
    /// carries the scale's offset, so it turns a 10 °C step into 50 °F instead of the correct 18 °F. Every
    /// other quantity here scales without an offset, which is why only temperature needs this.
    /// </remarks>
    double ConvertTemperatureDelta(double celsiusDelta);

    // ----- Axis TICK formatters -----
    //
    // Every chart in this app plots a series that has ALREADY been converted to the user's unit, so its axis
    // ticks arrive in display units too and a Labeler must format them WITHOUT converting again. There was
    // once a parallel Format*AxisLabel family taking canonical values; it was deleted because every chart
    // here is display-space, so the only way to reach for it was by mistake — and doing so was invisible on
    // the default unit and silently wrong on any other. Add a sibling here for a new quantity rather than
    // reintroducing a converting labeler.

    /// <summary>Formats a temperature axis tick that is ALREADY in the user's unit.</summary>
    string FormatTemperatureAxisTick(double displayValue);

    /// <summary>Formats a fan-speed axis tick that is ALREADY in the user's unit.</summary>
    string FormatFanSpeedAxisTick(double displayValue);

    /// <summary>Formats a ratio axis tick that is ALREADY in the user's unit.</summary>
    string FormatRatioAxisTick(double displayValue);

    /// <summary>Formats a clock-frequency axis tick that is ALREADY in the user's unit.</summary>
    string FormatClockFrequencyAxisTick(double displayValue);

    /// <summary>Formats a voltage axis tick that is ALREADY in the user's unit.</summary>
    string FormatVoltageAxisTick(double displayValue);

    /// <summary>Formats a current axis tick that is ALREADY in the user's unit.</summary>
    string FormatCurrentAxisTick(double displayValue);

    string FormatFanSpeed(double? rpm, string unavailableDisplay = "--", int decimals = -1);

    string FormatFanSpeedValue(double? rpm, string unavailableDisplay = "--", int decimals = -1);

    double ConvertFanSpeed(double rpm);

    string FormatClockFrequencyMegahertz(double? megahertz, string unavailableDisplay = "--", int decimals = -1);

    string FormatClockFrequencyValueMegahertz(double? megahertz, string unavailableDisplay = "--", int decimals = -1);

    double ConvertClockFrequencyMegahertz(double megahertz);

    string FormatRefreshRateHertz(double? hertz, string unavailableDisplay = "--", int decimals = -1);

    string FormatRefreshRateValueHertz(double? hertz, string unavailableDisplay = "--", int decimals = -1);

    double ConvertRefreshRateHertz(double hertz);

    string FormatInformationBytes(ulong bytes, bool treatZeroAsUnknown = false, string unavailableDisplay = "Unknown");

    string FormatInformationKilobytes(int kilobytes, string unavailableDisplay = "Unavailable");

    string FormatVoltage(double? volts, string unavailableDisplay = "--", int decimals = -1);

    string FormatVoltageValue(double? volts, string unavailableDisplay = "--", int decimals = -1);

    double ConvertVoltage(double volts);

    string FormatCurrent(double? amperes, string unavailableDisplay = "--", int decimals = -1);

    string FormatCurrentValue(double? amperes, string unavailableDisplay = "--", int decimals = -1);

    double ConvertCurrent(double amperes);

    string FormatChargeCapacity(double? ampereHours, string unavailableDisplay = "--", int decimals = -1);

    string FormatChargeCapacityValue(double? ampereHours, string unavailableDisplay = "--", int decimals = -1);

    double ConvertChargeCapacity(double ampereHours);

    string FormatEnergyWattHours(double? wattHours, string unavailableDisplay = "--", int decimals = -1);

    string FormatEnergyValueWattHours(double? wattHours, string unavailableDisplay = "--", int decimals = -1);

    double ConvertEnergyWattHours(double wattHours);

    string FormatRatio(double? percent, string unavailableDisplay = "--", int decimals = -1);

    string FormatRatioValue(double? percent, string unavailableDisplay = "--", int decimals = -1);

    double ConvertRatio(double percent);

    /// <summary>Converts a value in the user's chosen ratio unit back to canonical percent (the inverse of <see cref="ConvertRatio"/>).</summary>
    double ConvertRatioToPercent(double displayValue);

    string FormatLengthMillimeters(double? millimeters, string unavailableDisplay = "--", int decimals = -1);

    string FormatLengthValueMillimeters(double? millimeters, string unavailableDisplay = "--", int decimals = -1);

    double ConvertLengthMillimeters(double millimeters);

    string FormatAirflowCfm(double? cfm, string unavailableDisplay = "--", int decimals = -1);

    string FormatAirflowValueCfm(double? cfm, string unavailableDisplay = "--", int decimals = -1);

    double ConvertAirflowCfm(double cfm);

    string FormatBitRateBitsPerSecond(double? bitsPerSecond, string unavailableDisplay = "--", int decimals = -1);

    string FormatBitRateValueBitsPerSecond(double? bitsPerSecond, string unavailableDisplay = "--", int decimals = -1);

    double ConvertBitRateBitsPerSecond(double bitsPerSecond);

    string FormatPowerWatts(double? watts, string unavailableDisplay = "--", int decimals = -1);

    string FormatPowerValueWatts(double? watts, string unavailableDisplay = "--", int decimals = -1);

    double ConvertPowerWatts(double watts);

    string FormatAcousticLevelDecibels(double? decibels, string unavailableDisplay = "--", int decimals = -1, bool includeAWeighting = true);
}
