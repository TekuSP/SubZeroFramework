using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Models;

/// <summary>
/// Service configuration, including the three sensor-polling tiers.
/// </summary>
/// <remarks>
/// <para>
/// The tiers exist because the data has wildly different value-per-cost. Fan control needs temperature, fan
/// speed and CPU load as fresh as they can be had; the UI needs GPU load about as often as it redraws; and
/// installed RAM, disks and the motherboard model do not change while the machine is running. Polling all of
/// it at one rate meant either starving the controller or paying inventory costs hundreds of times a minute.
/// </para>
/// <para>
/// The three interval defaults come from <see cref="PollingTiers"/> so the record, <c>appsettings.json</c>
/// and the settings page's Default buttons cannot drift apart.
/// </para>
/// </remarks>
public sealed record FrameworkServiceOptions
{
    /// <summary>PRIMARY tier — EC telemetry and the CPU signals fan control runs on.</summary>
    public TimeSpan PollingInterval { get; init; } = PollingTiers.Primary.Default;

    /// <summary>SECONDARY tier — GPU/NPU load and other live display data, sampled inside the primary loop.</summary>
    public TimeSpan SecondaryPollingInterval { get; init; } = PollingTiers.Secondary.Default;

    /// <summary>
    /// TERTIARY tier — the Hardware.Info inventory poll: installed memory, drives, motherboard, BIOS, network
    /// adapters, CPU identity.
    /// </summary>
    /// <remarks>
    /// Defaulted to 30 s rather than the 1 s it used to run at. It was fast only because CPU usage rode along
    /// with it, and that measurement cost a blocking 500 ms sleep per poll (measured: ~600 ms total). Usage
    /// now comes from the primary tier, leaving nothing here that changes second to second.
    /// </remarks>
    public TimeSpan HardwareInfoPollingInterval { get; init; } = PollingTiers.Tertiary.Default;

    /// <summary>How long each tier's samples are kept. Seeded from the same tier definitions as the intervals.</summary>
    public TimeSpan PrimaryRetention { get; init; } = PollingTiers.Primary.DefaultRetention;

    public TimeSpan SecondaryRetention { get; init; } = PollingTiers.Secondary.DefaultRetention;

    public TimeSpan TertiaryRetention { get; init; } = PollingTiers.Tertiary.DefaultRetention;

    public bool AllowFanControlCommands { get; init; }

    public FanControlStateOptions[] FanControlStates { get; init; } = [];
}
