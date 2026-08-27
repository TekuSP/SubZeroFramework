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

    /// <summary>
    /// Video memory in use as a share of the total, 0–100.
    /// </summary>
    /// <remarks>
    /// Its own channel rather than a field riding on <see cref="UtilizationPercent"/>, because the service
    /// retains history PER CHANNEL — so a chart of video memory over time is only possible if it is a channel.
    /// Published only by a device that reports both a used and a total figure; an integrated GPU sharing
    /// system memory has neither, and gets no channel at all rather than a meaningless zero.
    /// </remarks>
    VramUtilizationPercent,
}
