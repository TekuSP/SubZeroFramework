using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Models;

public sealed record FanControlStateOptions
{
    public int FanIndex { get; init; }

    public FanControlMode Mode { get; init; } = FanControlMode.Auto;

    // Legacy single-curve fields. Retained so existing persisted configs keep loading; on read they are
    // migrated into curve profile slot 0 when no CurveProfiles are present.
    public Dictionary<int, double> CustomCurvePoints { get; init; } = [];

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; } = TemperatureAggregationMode.Maximum;

    public int[] DrivingSensorIndices { get; init; } = [];

    /// <summary>Which curve profile slot (0-based) is active for this fan.</summary>
    public int ActiveCurveSlot { get; init; }

    /// <summary>Up to five unique curve profile slots for this fan.</summary>
    public FanCurveProfileOptions[] CurveProfiles { get; init; } = [];

    /// <summary>The fan this one is grouped under ("Applies to" link), or null when independent / a leader itself.</summary>
    public int? LinkedLeaderIndex { get; init; }

    /// <summary>Duty points added on top of the active custom curve at 100% smoothed CPU usage, or null when disabled.</summary>
    public double? CpuUsageModifierStrength { get; init; }
}

public sealed record FanCurveProfileOptions
{
    public int Slot { get; init; }

    public string? Name { get; init; }

    public Dictionary<int, double> CurvePoints { get; init; } = [];

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; } = TemperatureAggregationMode.Maximum;

    public int[] DrivingSensorIndices { get; init; } = [];

    /// <summary>When set, this slot mirrors the active curve of the given fan instead of its own points.</summary>
    public int? FollowFanIndex { get; init; }
}
