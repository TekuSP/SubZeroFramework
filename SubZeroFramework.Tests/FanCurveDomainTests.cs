using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FanCurveDomainTests
{
    [Test]
    public void EditableBand_SitsInsideTheCurveChartAxisWindow()
    {
        // The chart windows the curve to 10–125 °C (FanCurveEditorView.xaml) so the min/max-speed anchors stay
        // hidden. A point outside that window renders off-plot and can never be grabbed again, so the editable
        // band must stay strictly inside it, with room for the point geometry.
        Assert.Multiple(() =>
        {
            Assert.That(FanCurveDomain.EditableMinTemperatureCelsius, Is.GreaterThan(10));
            Assert.That(FanCurveDomain.EditableMaxTemperatureCelsius, Is.LessThan(125));
            Assert.That(FanCurveDomain.EditableMinTemperatureCelsius, Is.GreaterThan(FanCurveDomain.MinTemperatureCelsius));
            Assert.That(FanCurveDomain.EditableMaxTemperatureCelsius, Is.LessThan(FanCurveDomain.MaxTemperatureCelsius));
        });
    }

    [Test]
    public void SpeedAnchors_RunFromIdleToFullSpeed()
    {
        // The anchors are what make every curve rise: idle when cold, full speed at the top of the domain.
        Assert.Multiple(() =>
        {
            Assert.That(FanCurveDomain.MinSpeedDutyPercent, Is.EqualTo(0d));
            Assert.That(FanCurveDomain.MaxSpeedDutyPercent, Is.EqualTo(100d));
        });
    }

    [Test]
    public void ClampTemperature_BeyondEitherEdge_SnapsToTheEditableBand()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FanCurveDomain.ClampTemperature(-40d), Is.EqualTo(FanCurveDomain.EditableMinTemperatureCelsius));
            Assert.That(FanCurveDomain.ClampTemperature(0d), Is.EqualTo(FanCurveDomain.EditableMinTemperatureCelsius));
            Assert.That(FanCurveDomain.ClampTemperature(130d), Is.EqualTo(FanCurveDomain.EditableMaxTemperatureCelsius));
            Assert.That(FanCurveDomain.ClampTemperature(999d), Is.EqualTo(FanCurveDomain.EditableMaxTemperatureCelsius));
        });
    }

    [Test]
    public void ClampTemperature_InsideTheBand_RoundsToAWholeDegree()
    {
        // The service keys curve points by integer Celsius, so a dragged point must land on one. Exact
        // midpoints are left to Math.Round's default (to-even) — half a degree of drag is not worth pinning.
        Assert.Multiple(() =>
        {
            Assert.That(FanCurveDomain.ClampTemperature(60.4d), Is.EqualTo(60));
            Assert.That(FanCurveDomain.ClampTemperature(60.6d), Is.EqualTo(61));
        });
    }

    [Test]
    public void ClampDuty_IsHeldToTheSpeedRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FanCurveDomain.ClampDuty(-12d), Is.EqualTo(0d));
            Assert.That(FanCurveDomain.ClampDuty(142d), Is.EqualTo(100d));
            Assert.That(FanCurveDomain.ClampDuty(37.5d), Is.EqualTo(37.5d));
        });
    }

    [Test]
    public void Normalize_StrandedPoints_ArePulledBackAndOrdered()
    {
        // A curve stored before the band was enforced: points parked at the 0 °C / 130 °C extremes, which is
        // what left them invisible and unmovable.
        var normalized = FanCurveDomain.Normalize([(130, 100d), (0, 0d), (60, 55d)]);

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Has.Length.EqualTo(3));
            Assert.That(normalized[0], Is.EqualTo((FanCurveDomain.EditableMinTemperatureCelsius, 0d)));
            Assert.That(normalized[1], Is.EqualTo((60, 55d)));
            Assert.That(normalized[2], Is.EqualTo((FanCurveDomain.EditableMaxTemperatureCelsius, 100d)));
        });
    }

    [Test]
    public void Normalize_PointsAlreadyInsideTheBand_AreUnchanged()
    {
        // The seeded default curve must survive normalization untouched, or opening an untouched profile
        // would read as dirty and trip the unsaved-changes guard.
        (int Temperature, double Duty)[] points = [(40, 30d), (60, 60d), (80, 100d)];

        Assert.That(FanCurveDomain.Normalize(points), Is.EqualTo(points));
    }

    [Test]
    public void Normalize_IsIdempotent()
    {
        // The draft and its applied baseline are normalized independently; if a second pass moved anything,
        // an untouched curve would compare dirty.
        var once = FanCurveDomain.Normalize([(0, 10d), (130, 90d), (55, 45d)]);

        Assert.That(FanCurveDomain.Normalize(once), Is.EqualTo(once));
    }

    [Test]
    public void InterpolateDuty_CannotBePinnedOffByAPointAtTheTopOfTheDomain()
    {
        // The reported bypass: a point at or above MaxTemperatureCelsius used to suppress the implicit
        // full-speed anchor, so this curve evaluated to 0% at EVERY temperature with nothing between the fan
        // and the firmware's critical-temperature shutdown.
        (int, double)[] pinnedOff = [(0, 0d), (1_000_000, 0d)];

        Assert.Multiple(() =>
        {
            Assert.That(FanCurveDomain.InterpolateDuty(pinnedOff, FanCurveDomain.MaxTemperatureCelsius),
                Is.EqualTo(FanCurveDomain.MaxSpeedDutyPercent),
                "the top of the domain must always be full speed");
            Assert.That(FanCurveDomain.InterpolateDuty(pinnedOff, 100d), Is.GreaterThan(0d),
                "a curve cannot hold the fan at zero as the domain top is approached");
        });
    }

    [Test]
    public void BuildAnchoredSeries_AlwaysEndsAtFullSpeedAtTheTopOfTheDomain()
    {
        // Whatever the caller supplies — including a point exactly ON the boundary, which is what made the
        // old conditional anchor drop out.
        (int, double)[][] curves =
        [
            [(0, 0d), (FanCurveDomain.MaxTemperatureCelsius, 0d)],
            [(0, 0d), (200, 5d)],
            [(40, 30d), (80, 60d)],
        ];

        foreach (var curve in curves)
        {
            var series = FanCurveDomain.BuildAnchoredSeries(curve);

            Assert.That(series[^1], Is.EqualTo((
                (double)FanCurveDomain.MaxTemperatureCelsius,
                FanCurveDomain.MaxSpeedDutyPercent)),
                $"curve [{string.Join(", ", curve)}] must close on the full-speed anchor");
        }
    }
}
