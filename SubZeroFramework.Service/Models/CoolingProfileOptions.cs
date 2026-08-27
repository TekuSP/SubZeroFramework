using System.Collections.Immutable;

using FrameworkDotnet.Enums;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Models;

/// <summary>
/// One cooling profile as it is written to service-settings.json.
/// </summary>
/// <remarks>
/// A separate shape from <see cref="CoolingProfile"/> because the configuration binder needs plain
/// collection types it can construct, while everything else in the service works with the immutable model.
/// Converting at this one boundary keeps the binder's requirements out of the domain type.
/// </remarks>
public sealed record CoolingProfileOptions
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? IconName { get; init; }

    public uint? AccentColorArgb { get; init; }

    public bool IsSeeded { get; init; }

    public CoolingProfileFanEntryOptions[] Fans { get; init; } = [];

    public static CoolingProfileOptions From(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new CoolingProfileOptions
        {
            Id = profile.Id,
            Name = profile.Name,
            IconName = profile.IconName,
            AccentColorArgb = profile.AccentColorArgb,
            IsSeeded = profile.IsSeeded,
            Fans = [.. profile.Fans.Select(CoolingProfileFanEntryOptions.From)],
        };
    }

    public CoolingProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name,
        IconName = IconName,
        AccentColorArgb = AccentColorArgb,
        IsSeeded = IsSeeded,
        Fans = [.. Fans.Select(static entry => entry.ToEntry())],
    };
}

/// <inheritdoc cref="CoolingProfileOptions" />
public sealed record CoolingProfileFanEntryOptions
{
    public int FanIndex { get; init; }

    public FanControlMode Mode { get; init; } = FanControlMode.Auto;

    public double DutyPercent { get; init; }

    public double AdaptiveTargetCelsius { get; init; } = AdaptiveFanSettings.DefaultTargetCelsius;

    public TemperatureAggregationMode Aggregation { get; init; } = TemperatureAggregationMode.Maximum;

    public Dictionary<int, double> CurvePoints { get; init; } = [];

    public static CoolingProfileFanEntryOptions From(CoolingProfileFanEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new CoolingProfileFanEntryOptions
        {
            FanIndex = entry.FanIndex,
            Mode = entry.Mode,
            DutyPercent = entry.DutyPercent,
            AdaptiveTargetCelsius = entry.AdaptiveTargetCelsius,
            Aggregation = entry.Aggregation,
            CurvePoints = new Dictionary<int, double>(entry.CurvePoints),
        };
    }

    public CoolingProfileFanEntry ToEntry() => new()
    {
        FanIndex = FanIndex,
        Mode = Mode,
        DutyPercent = DutyPercent,
        AdaptiveTargetCelsius = AdaptiveTargetCelsius,
        Aggregation = Aggregation,
        CurvePoints = CurvePoints.ToImmutableSortedDictionary(),
    };
}
