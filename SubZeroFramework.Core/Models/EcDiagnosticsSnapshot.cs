using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

/// <summary>
/// Cheap health readings taken straight from the embedded controller on the telemetry poll.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a single EC transaction, which is what makes it pollable — unlike the Smart Battery
/// pack registers or the per-sensor metadata, both of which are read on demand or once per connection.
/// </para>
/// <para>
/// Each field is populated independently, because firmware coverage is uneven: a board can answer the
/// throttle command and refuse the panic command in the same breath. An absent reading reads as its
/// harmless default rather than failing the whole snapshot, and <see cref="IsAvailable"/> is what separates
/// "the controller says everything is fine" from "nothing could be read".
/// </para>
/// </remarks>
public sealed record EcDiagnosticsSnapshot
{
    /// <summary>Nothing could be read. An unavailable controller, NOT a healthy one.</summary>
    public static EcDiagnosticsSnapshot Unavailable { get; } = new();

    /// <summary>Whether any reading in this snapshot came back.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>The processor's clocks are being trimmed.</summary>
    public bool SoftThrottled { get; init; }

    /// <summary>The controller is holding the processor back to protect the hardware.</summary>
    public bool HardThrottled { get; init; }

    /// <summary>Which firmware image is running — the read-only recovery image, or the normal one.</summary>
    public FrameworkEcCurrentImage CurrentImage { get; init; } = FrameworkEcCurrentImage.Unknown;

    /// <summary>
    /// Why the controller last restarted. A FLAGS value: several causes can hold at once.
    /// </summary>
    /// <remarks>
    /// Kept as the flags enum rather than a pre-rendered sentence, so a consumer can test for the one cause
    /// it cares about. <c>ToString</c> already spells the set out for display, which is why no separate
    /// reason string is stored beside it.
    /// </remarks>
    public FrameworkEcResetFlag ResetFlags { get; init; }

    /// <summary>The controller is holding a valid panic record: it crashed and restarted itself.</summary>
    public bool HasPanicRecord { get; init; }

    /// <summary>Lid switch state.</summary>
    public bool LidOpen { get; init; }

    /// <summary>Firmware write protection is off — expected while flashing, notable otherwise.</summary>
    public bool WriteProtectDisabled { get; init; }

    /// <summary>When these readings were taken.</summary>
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// The worst throttling currently reported.
    /// </summary>
    /// <remarks>
    /// Hard wins when both bits are set. A controller protecting the silicon is also trimming clocks, so the
    /// two are not exclusive — reporting the milder of them would understate what is happening.
    /// </remarks>
    public EcThrottleSeverity ThrottleSeverity => HardThrottled
        ? EcThrottleSeverity.Hard
        : SoftThrottled
            ? EcThrottleSeverity.Soft
            : EcThrottleSeverity.None;
}
