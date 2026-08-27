using NUnit.Framework;

using SubZeroFramework.Services.Updates;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the two version strings this app has to reconcile: its own informational version and a
/// GitHub release tag.
/// </summary>
[TestFixture]
public class AppVersionTests
{
    [TestCase("v0.1.6", "0.1.6")]
    [TestCase("0.1.6", "0.1.6")]
    [TestCase("V0.1.6", "0.1.6")]
    [TestCase("0.1.5.0", "0.1.5.0")]
    [TestCase("0.1.5+abc1234", "0.1.5")]
    public void Parse_AcceptsTheFormsBothSidesActuallyProduce(string raw, string expected)
        => Assert.That(AppVersion.Parse(raw), Is.EqualTo(Version.Parse(expected)));

    // A tag CI would never cut (build.yml rejects '-'), and anything that is not a version at all.
    [TestCase("v0.1.6-beta")]
    [TestCase("nightly")]
    [TestCase("")]
    [TestCase(null)]
    public void Parse_RefusesAnythingItCannotCompareSafely(string? raw)
        => Assert.That(AppVersion.Parse(raw), Is.Null);

    [Test]
    public void IsNewer_IsTrue_OnlyWhenTheCandidateIsStrictlyAhead()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppVersion.IsNewer(Version.Parse("0.1.6"), Version.Parse("0.1.5")), Is.True);
            Assert.That(AppVersion.IsNewer(Version.Parse("0.2.0"), Version.Parse("0.1.9")), Is.True);
            Assert.That(AppVersion.IsNewer(Version.Parse("0.1.5"), Version.Parse("0.1.5")), Is.False, "equal is not an update");
            Assert.That(AppVersion.IsNewer(Version.Parse("0.1.4"), Version.Parse("0.1.5")), Is.False, "older must never prompt");
        });
    }

    // 0.1.6 and 0.1.6.0 are the same release stamped two ways: the tag omits the fourth field, a local
    // build carries it. Treating them as different would prompt forever on the version already installed.
    [Test]
    public void IsNewer_TreatsAnOmittedFourthField_AsEqual()
        => Assert.That(AppVersion.IsNewer(Version.Parse("0.1.6"), Version.Parse("0.1.6.0")), Is.False);
}
