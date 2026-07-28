namespace SubZeroFramework.Models;

public enum TelemetryEntityKind
{
    TemperatureSensor,
    Fan,
    Battery,

    /// <summary>A graphics adapter, integrated or discrete.</summary>
    Gpu,

    /// <summary>A neural processing unit. Reported separately because its percentage means something different.</summary>
    Npu,
}
