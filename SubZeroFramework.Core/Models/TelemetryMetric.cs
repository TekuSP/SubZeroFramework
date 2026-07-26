namespace SubZeroFramework.Models;

public enum TelemetryMetric
{
    TemperatureCelsius,
    FanSpeedRpm,
    BatteryChargePercent,
    BatteryPresentRateAmperes,
    BatteryPresentVoltageVolts,

    /// <summary>
    /// Share of wall-clock time the device was busy, 0–100. Busy-TIME, not capacity: a device can be 100%
    /// busy while barely loaded, so the UI must not present this as "how much of the chip is in use".
    /// </summary>
    UtilizationPercent,
}
