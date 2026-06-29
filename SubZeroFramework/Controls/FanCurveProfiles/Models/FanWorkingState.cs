using FrameworkDotnet.Enums;

using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// The locally-staged working configuration for one fan in the redesigned Fan Control page. Edits mutate
/// this (never the hardware) until the user commits via Apply all (or live-previews). A fan is "staged"
/// when its working state differs from the applied baseline captured from the service.
/// </summary>
public sealed class FanWorkingState
{
    public FanWorkingState(int fanIndex)
    {
        FanIndex = fanIndex;
    }

    public int FanIndex { get; }

    public FanControlMode Mode { get; set; } = FanControlMode.Auto;

    public double ManualDutyPercent { get; set; } = 50d;

    public List<int> SelectedSensors { get; set; } = [];

    public TemperatureAggregationMode Aggregation { get; set; } = TemperatureAggregationMode.Maximum;

    public List<(int Temperature, double Duty)> CurvePoints { get; set; } =
    [
        (40, 30d),
        (60, 60d),
        (80, 100d),
    ];

    /// <summary>Fans this edit also applies to (always includes <see cref="FanIndex"/>).</summary>
    public HashSet<int> LinkedFans { get; set; } = [];

    public FanWorkingState Clone() => new(FanIndex)
    {
        Mode = Mode,
        ManualDutyPercent = ManualDutyPercent,
        SelectedSensors = [.. SelectedSensors],
        Aggregation = Aggregation,
        CurvePoints = [.. CurvePoints],
        LinkedFans = [.. LinkedFans],
    };

    /// <summary>Value comparison of the control-relevant fields (link membership is not a hardware change).</summary>
    public bool MatchesControl(FanWorkingState other)
    {
        if (Mode != other.Mode)
        {
            return false;
        }

        return Mode switch
        {
            FanControlMode.Manual => Math.Abs(ManualDutyPercent - other.ManualDutyPercent) < 0.5d,
            FanControlMode.CustomCurve => CurveMatches(other),
            _ => true,
        };
    }

    private bool CurveMatches(FanWorkingState other)
    {
        if (Aggregation != other.Aggregation)
        {
            return false;
        }

        if (!SelectedSensors.OrderBy(static i => i).SequenceEqual(other.SelectedSensors.OrderBy(static i => i)))
        {
            return false;
        }

        if (CurvePoints.Count != other.CurvePoints.Count)
        {
            return false;
        }

        var left = CurvePoints.OrderBy(static p => p.Temperature).ToArray();
        var right = other.CurvePoints.OrderBy(static p => p.Temperature).ToArray();
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Temperature != right[i].Temperature || Math.Abs(left[i].Duty - right[i].Duty) > 0.01d)
            {
                return false;
            }
        }

        return true;
    }
}
