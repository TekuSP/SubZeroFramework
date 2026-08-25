using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services.Control;

/// <summary>What a calibration has to heat for a given fan's sensors to respond.</summary>
[Flags]
public enum ThermalLoadTarget
{
    /// <summary>Nothing identifiable — the sensors say nothing about what heats them.</summary>
    None = 0,

    /// <summary>The processor package.</summary>
    Cpu = 1,

    /// <summary>The discrete GPU.</summary>
    Gpu = 2,

    /// <summary>Both, for a fan whose sensors span the two.</summary>
    Both = Cpu | Gpu,
}

/// <summary>
/// Works out what a calibration must load from the sensors the fan is controlled by.
/// </summary>
/// <remarks>
/// <para>
/// On a Framework 16 the left fan cools the processor and the right fan cools the discrete GPU. Loading the
/// CPU while calibrating the right fan leaves its sensors sitting at idle, and the run fails several minutes
/// later for a temperature swing that was never going to happen — after heating the machine for nothing.
/// </para>
/// <para>
/// Derived from the sensors rather than the fan index, because the fan index means different things on
/// different models and the user chooses which sensors drive a fan. The sensors are the honest signal: they
/// are what the loop actually controls to, so whatever heats them is what the run has to heat.
/// </para>
/// </remarks>
public static class ThermalLoadTargetResolver
{
    /// <summary>
    /// Decides what to heat so the given sensors respond.
    /// </summary>
    /// <param name="sensorNames">The driving sensors' platform roles.</param>
    /// <returns>What must be loaded, or <see cref="ThermalLoadTarget.None"/> if the sensors do not say.</returns>
    public static ThermalLoadTarget Resolve(IEnumerable<FrameworkSensorName> sensorNames)
    {
        ArgumentNullException.ThrowIfNull(sensorNames);

        var target = ThermalLoadTarget.None;

        foreach (var name in sensorNames)
        {
            target |= name switch
            {
                // Every dGPU sensor: the core, its VRM, its memory, and the air around it. All four are
                // heated by GPU work and by nothing else the service can generate.
                FrameworkSensorName.DgpuTemp
                    or FrameworkSensorName.DgpuVr
                    or FrameworkSensorName.DgpuVram
                    or FrameworkSensorName.DgpuAmb => ThermalLoadTarget.Gpu,

                // Peci is the processor's own reading; Apu covers the package on the integrated parts.
                FrameworkSensorName.Peci or FrameworkSensorName.Apu => ThermalLoadTarget.Cpu,

                // Battery, charger IC, virtual and unnamed sensors say nothing about what heats them, so
                // they contribute nothing rather than guessing.
                _ => ThermalLoadTarget.None,
            };
        }

        return target;
    }
}
