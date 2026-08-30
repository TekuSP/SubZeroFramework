using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The EC health snapshot that replaced an inferred performance ratio as Adaptive's throttle signal.
/// </summary>
[TestFixture]
public class EcDiagnosticsSnapshotTests
{
    /// <summary>
    /// "Nothing could be read" must not be mistaken for "everything is fine" — the difference decides
    /// whether Adaptive trusts this snapshot or falls back to the performance-ratio proxy.
    /// </summary>
    [Test]
    public void Unavailable_ReportsNothingThrottledAndNoPanic()
    {
        var snapshot = EcDiagnosticsSnapshot.Unavailable;

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.IsAvailable, Is.False);
            Assert.That(snapshot.SoftThrottled, Is.False);
            Assert.That(snapshot.HardThrottled, Is.False);
            Assert.That(snapshot.HasPanicRecord, Is.False);
            Assert.That(snapshot.ThrottleSeverity, Is.EqualTo(EcThrottleSeverity.None));
        });
    }

    /// <summary>
    /// Both bits set at once is the normal case while protecting: reporting the milder one would understate
    /// what the controller is doing, and Adaptive scales its escalation off this.
    /// </summary>
    [Test]
    public void ThrottleSeverity_PrefersHardOverSoft()
    {
        var snapshot = new EcDiagnosticsSnapshot { IsAvailable = true, SoftThrottled = true, HardThrottled = true };

        Assert.That(snapshot.ThrottleSeverity, Is.EqualTo(EcThrottleSeverity.Hard));
    }

    [Test]
    public void ThrottleSeverity_WithOnlySoftSet_IsSoft()
    {
        var snapshot = new EcDiagnosticsSnapshot { IsAvailable = true, SoftThrottled = true };

        Assert.That(snapshot.ThrottleSeverity, Is.EqualTo(EcThrottleSeverity.Soft));
    }

    [Test]
    public void ThrottleSeverity_WithNeitherFlag_IsNone()
    {
        var snapshot = new EcDiagnosticsSnapshot { IsAvailable = true };

        Assert.That(snapshot.ThrottleSeverity, Is.EqualTo(EcThrottleSeverity.None));
    }
}
