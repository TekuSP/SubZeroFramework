using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

/// <summary>
/// Maps a temperature sensor's platform role (<see cref="FrameworkSensorName"/>, from the EC) to the short
/// human-readable location shown beneath "Sensor N" on the Thermal page (e.g. APU → "APU / SoC"). Returns
/// <see langword="null"/> when there is no meaningful location (Unknown / Generic / null), so the UI can omit the
/// location line rather than print a placeholder.
/// </summary>
public static class FrameworkSensorNameDisplay
{
    public static string? ToLocation(FrameworkSensorName? sensorName) => sensorName switch
    {
        FrameworkSensorName.F75303Local => "Mainboard",
        FrameworkSensorName.F75303Cpu => "CPU",
        FrameworkSensorName.Peci => "CPU (PECI)",
        FrameworkSensorName.F75303Ddr => "Memory",
        FrameworkSensorName.Apu => "APU / SoC",
        FrameworkSensorName.F75303Apu => "APU",
        FrameworkSensorName.F57397VccGt => "Graphics rail",
        FrameworkSensorName.DgpuVr => "GPU VR",
        FrameworkSensorName.DgpuVram => "GPU VRAM",
        FrameworkSensorName.DgpuAmb => "GPU ambient",
        FrameworkSensorName.DgpuTemp => "GPU die",
        FrameworkSensorName.Battery => "Battery",
        FrameworkSensorName.ChargerIc => "Charger",
        FrameworkSensorName.F75303Skin => "Chassis",
        FrameworkSensorName.F75303Amb => "Ambient",
        FrameworkSensorName.Virtual => "Aggregate",
        _ => null,
    };
}
