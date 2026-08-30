using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The derived numbers on the pack snapshot — the two that turn raw registers into a diagnosis.
/// </summary>
[TestFixture]
public class SmartBatterySnapshotTests
{
    /// <summary>
    /// Cell imbalance is the early sign of a pack failing, and the number that says so is the SPREAD.
    /// </summary>
    [Test]
    public void CellImbalanceVolts_IsTheSpreadBetweenTheHighestAndLowestCell()
    {
        var snapshot = NewPack(3.95d, 3.95d, 3.72d, 3.96d);

        Assert.That(snapshot.CellImbalanceVolts, Is.EqualTo(0.24d).Within(1e-6d));
    }

    /// <summary>
    /// A three-cell pack reports zero for the fourth. Counting that as a cell would invent a 3.9 V imbalance
    /// on a perfectly healthy battery.
    /// </summary>
    [Test]
    public void CellImbalanceVolts_IgnoresCellsThatReportedNothing()
    {
        var snapshot = NewPack(3.95d, 3.90d, 3.93d, 0d);

        Assert.That(snapshot.CellImbalanceVolts, Is.EqualTo(0.05d).Within(1e-6d));
    }

    [Test]
    public void CellImbalanceVolts_WithNoCellReadings_IsNull()
        => Assert.That(NewPack(0d, 0d, 0d, 0d).CellImbalanceVolts, Is.Null);

    /// <summary>One cell is not a spread. Reporting zero would claim a perfect balance nobody measured.</summary>
    [Test]
    public void CellImbalanceVolts_WithASingleReportingCell_IsNull()
        => Assert.That(NewPack(3.95d, 0d, 0d, 0d).CellImbalanceVolts, Is.Null);

    [Test]
    public void AgeInDays_CountsFromTheManufactureDate()
    {
        var snapshot = NewPack(3.9d, 3.9d, 3.9d, 3.9d) with
        {
            ManufactureDate = new DateOnly(2024, 1, 1),
            ObservedAt = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.That(snapshot.AgeInDays, Is.EqualTo(60));
    }

    [Test]
    public void AgeInDays_WithoutAManufactureDate_IsNull()
        => Assert.That((NewPack(3.9d, 3.9d, 3.9d, 3.9d) with { ManufactureDate = null }).AgeInDays, Is.Null);

    /// <summary>
    /// A pack reporting a date in the future is reporting nonsense. A negative age would render as "-12 days
    /// old", which is worse than clamping.
    /// </summary>
    [Test]
    public void AgeInDays_WithAFutureManufactureDate_IsZeroRatherThanNegative()
    {
        var snapshot = NewPack(3.9d, 3.9d, 3.9d, 3.9d) with
        {
            ManufactureDate = new DateOnly(2030, 1, 1),
            ObservedAt = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.That(snapshot.AgeInDays, Is.Zero);
    }

    private static SmartBatterySnapshot NewPack(double cell1, double cell2, double cell3, double cell4) => new()
    {
        CellVoltageVolts1 = cell1,
        CellVoltageVolts2 = cell2,
        CellVoltageVolts3 = cell3,
        CellVoltageVolts4 = cell4,
        ObservedAt = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
