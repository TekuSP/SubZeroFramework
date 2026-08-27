using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the <c>pp_dpm_sclk</c> parser against the shapes amdgpu actually emits.
/// </summary>
/// <remarks>
/// The samples below are the kernel's format verbatim, including the lower-case "Mhz" spelling most ASICs
/// use. Getting this wrong is quiet: a maximum clock that parses as null hides the clock-versus-maximum bar,
/// and one that parses the CURRENT state instead of the highest makes "maximum" follow the load around.
/// </remarks>
[TestFixture]
public class AmdGpuClockTableTests
{
    /// <summary>The asterisk marks the CURRENT state; the maximum is the highest listed, not that one.</summary>
    [Test]
    public void ParseMaximumMegahertz_TakesTheHighestState_NotTheActiveOne()
    {
        var table = "0: 500Mhz\n1: 1150Mhz *\n2: 2200Mhz\n";

        Assert.That(AmdGpuClockTable.ParseMaximumMegahertz(table), Is.EqualTo(2200d));
    }

    /// <summary>Some ASICs spell it "MHz"; the unit check is case-insensitive for exactly this reason.</summary>
    [Test]
    public void ParseMaximumMegahertz_AcceptsEitherUnitSpelling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("0: 800MHz\n1: 2400MHz\n"), Is.EqualTo(2400d));
            Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("0: 800Mhz\n1: 2400Mhz\n"), Is.EqualTo(2400d));
        });
    }

    /// <summary>Spacing between the number and the unit varies across kernel versions.</summary>
    [Test]
    public void ParseMaximumMegahertz_ToleratesSpacingVariation()
    {
        Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("0:  500 Mhz\n1:  2200 Mhz *\n"), Is.EqualTo(2200d));
    }

    /// <summary>An APU may list a single state; that state is still the maximum.</summary>
    [Test]
    public void ParseMaximumMegahertz_HandlesASingleState()
    {
        Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("0: 2900Mhz *\n"), Is.EqualTo(2900d));
    }

    /// <summary>
    /// A line the parser cannot read costs that line only — a kernel that adds a header or a trailing note
    /// must not blank out a reading that the other lines answer perfectly well.
    /// </summary>
    [Test]
    public void ParseMaximumMegahertz_SkipsUnparseableLines()
    {
        var table = "OD_SCLK:\n0: 500Mhz\ngarbage without a colon\n2: 2200Mhz\nOD_RANGE:\n";

        Assert.That(AmdGpuClockTable.ParseMaximumMegahertz(table), Is.EqualTo(2200d));
    }

    /// <summary>
    /// A table in some other unit must be IGNORED, not read as megahertz — misreading a gigahertz table
    /// would understate the maximum a thousandfold and make every clock look like it was pegged at redline.
    /// </summary>
    [Test]
    public void ParseMaximumMegahertz_RejectsAnUnexpectedUnit()
    {
        Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("0: 0.5Ghz\n1: 2.2Ghz\n"), Is.Null);
    }

    [Test]
    public void ParseMaximumMegahertz_ReturnsNullForNothingUsable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AmdGpuClockTable.ParseMaximumMegahertz(null), Is.Null);
            Assert.That(AmdGpuClockTable.ParseMaximumMegahertz(string.Empty), Is.Null);
            Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("   \n  \n"), Is.Null);
            Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("0: Mhz\n"), Is.Null);
        });
    }

    /// <summary>A zero or negative state is not a clock the card can run at.</summary>
    [Test]
    public void ParseMaximumMegahertz_IgnoresNonPositiveStates()
    {
        Assert.That(AmdGpuClockTable.ParseMaximumMegahertz("0: 0Mhz\n1: 1800Mhz\n"), Is.EqualTo(1800d));
    }
}
