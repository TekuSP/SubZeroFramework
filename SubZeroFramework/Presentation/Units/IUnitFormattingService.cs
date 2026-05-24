namespace SubZeroFramework.Presentation.Units;

public interface IUnitFormattingService
{
    string TemperatureUnitSuffix { get; }

    string FanSpeedUnitSuffix { get; }

    string ClockFrequencyUnitSuffix { get; }

    string RefreshRateUnitSuffix { get; }

    string VoltageUnitSuffix { get; }

    string CurrentUnitSuffix { get; }

    string ChargeCapacityUnitSuffix { get; }

    string RatioUnitSuffix { get; }

    string LengthUnitSuffix { get; }

    string AirflowUnitSuffix { get; }

    string BitRateUnitSuffix { get; }

    string PowerUnitSuffix { get; }

    double RatioAxisMaximum { get; }

    string FormatTemperature(double? celsius, string unavailableDisplay = "--", int decimals = 0);

    string FormatTemperatureValue(double? celsius, string unavailableDisplay = "--", int decimals = 0);

    double ConvertTemperature(double celsius);

    string FormatTemperatureAxisLabel(double celsiusValue);

    string FormatFanSpeed(double? rpm, string unavailableDisplay = "--", int decimals = -1);

    string FormatFanSpeedValue(double? rpm, string unavailableDisplay = "--", int decimals = -1);

    double ConvertFanSpeed(double rpm);

    string FormatFanSpeedAxisLabel(double rpmValue);

    string FormatClockFrequencyMegahertz(double? megahertz, string unavailableDisplay = "--", int decimals = -1);

    string FormatClockFrequencyValueMegahertz(double? megahertz, string unavailableDisplay = "--", int decimals = -1);

    double ConvertClockFrequencyMegahertz(double megahertz);

    string FormatClockFrequencyAxisLabel(double megahertzValue);

    string FormatRefreshRateHertz(double? hertz, string unavailableDisplay = "--", int decimals = -1);

    string FormatRefreshRateValueHertz(double? hertz, string unavailableDisplay = "--", int decimals = -1);

    double ConvertRefreshRateHertz(double hertz);

    string FormatRefreshRateAxisLabel(double hertzValue);

    string FormatInformationBytes(ulong bytes, bool treatZeroAsUnknown = false, string unavailableDisplay = "Unknown");

    string FormatInformationKilobytes(int kilobytes, string unavailableDisplay = "Unavailable");

    string FormatVoltage(double? volts, string unavailableDisplay = "--", int decimals = -1);

    string FormatVoltageValue(double? volts, string unavailableDisplay = "--", int decimals = -1);

    double ConvertVoltage(double volts);

    string FormatVoltageAxisLabel(double voltsValue);

    string FormatCurrent(double? amperes, string unavailableDisplay = "--", int decimals = -1);

    string FormatCurrentValue(double? amperes, string unavailableDisplay = "--", int decimals = -1);

    double ConvertCurrent(double amperes);

    string FormatCurrentAxisLabel(double amperesValue);

    string FormatChargeCapacity(double? ampereHours, string unavailableDisplay = "--", int decimals = -1);

    string FormatChargeCapacityValue(double? ampereHours, string unavailableDisplay = "--", int decimals = -1);

    double ConvertChargeCapacity(double ampereHours);

    string FormatChargeCapacityAxisLabel(double ampereHoursValue);

    string FormatRatio(double? percent, string unavailableDisplay = "--", int decimals = -1);

    string FormatRatioValue(double? percent, string unavailableDisplay = "--", int decimals = -1);

    double ConvertRatio(double percent);

    string FormatRatioAxisLabel(double percentValue);

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
