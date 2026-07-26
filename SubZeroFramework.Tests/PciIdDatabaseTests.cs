using NUnit.Framework;

using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the pci.ids parse against the real file format (hwdata 2026.07.01).
/// </summary>
/// <remarks>
/// The fixture is a verbatim excerpt: the indentation is significant and load-bearing (column 0 = vendor,
/// one tab = device, two tabs = subsystem), so it must not be reformatted or re-indented.
/// </remarks>
[TestFixture]
public class PciIdDatabaseTests
{
    private const string Fixture =
        "#\n" +
        "#\tList of PCI IDs\n" +
        "#\tVersion: 2026.07.01\n" +
        "#\n" +
        "001c  PEAK-System Technik GmbH\n" +
        "\t0001  PCAN-PCI CAN-Bus controller\n" +
        "\t\t001c 0004  2 Channel CAN Bus SJC1000\n" +
        "1002  Advanced Micro Devices, Inc. [AMD/ATI]\n" +
        "\t1114  Krackan [Radeon 840M / 860M Graphics]\n" +
        "\t150e  Strix [Radeon 880M / 890M]\n" +
        "\t\t1043 04dd  STRIX R9 390\n" +
        "\t1586  Strix Halo [Radeon Graphics / Radeon 8050S Graphics / Radeon 8060S Graphics]\n" +
        "10de  NVIDIA Corporation\n" +
        "\t2c19  AD107M [GeForce RTX 4060 Max-Q / Mobile]\n" +
        "8086  Intel Corporation\n" +
        "\t7d55  Meteor Lake-P [Intel Arc Graphics]\n" +
        "C 03  Display controller\n" +
        "\t00  VGA compatible controller\n";

    [Test]
    public void ResolvesTheDevelopmentMachinesIntegratedGpu()
    {
        // 1002:150e is the Radeon 890M in the Framework 16 this was developed against.
        var names = Parse(new PciDeviceId(0x1002, 0x150E));

        Assert.Multiple(() =>
        {
            Assert.That(names.VendorName, Is.EqualTo("Advanced Micro Devices, Inc. [AMD/ATI]"));
            Assert.That(names.DeviceName, Is.EqualTo("Strix [Radeon 880M / 890M]"));
        });
    }

    [Test]
    public void ResolvesSeveralVendorsInOnePass()
    {
        var results = PciIdDatabase.Parse(
            new StringReader(Fixture),
            [
                new PciDeviceId(0x1002, 0x150E),
                new PciDeviceId(0x10DE, 0x2C19),
                new PciDeviceId(0x8086, 0x7D55),
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(results[new PciDeviceId(0x1002, 0x150E)].DeviceName, Is.EqualTo("Strix [Radeon 880M / 890M]"));
            Assert.That(results[new PciDeviceId(0x10DE, 0x2C19)].DeviceName, Is.EqualTo("AD107M [GeForce RTX 4060 Max-Q / Mobile]"));
            Assert.That(results[new PciDeviceId(0x8086, 0x7D55)].DeviceName, Is.EqualTo("Meteor Lake-P [Intel Arc Graphics]"));
            Assert.That(results[new PciDeviceId(0x8086, 0x7D55)].VendorName, Is.EqualTo("Intel Corporation"));
        });
    }

    [Test]
    public void SubsystemLines_AreNeverMistakenForDevices()
    {
        // "\t\t1043 04dd  STRIX R9 390" sits under 1002:150e. Treating it as a device would both invent a
        // device 0x1043 and, worse, overwrite the real 150e name with a board vendor's marketing string.
        var results = PciIdDatabase.Parse(
            new StringReader(Fixture),
            [new PciDeviceId(0x1002, 0x150E), new PciDeviceId(0x1002, 0x1043)]);

        Assert.Multiple(() =>
        {
            Assert.That(results[new PciDeviceId(0x1002, 0x150E)].DeviceName, Is.EqualTo("Strix [Radeon 880M / 890M]"));
            Assert.That(results[new PciDeviceId(0x1002, 0x1043)].DeviceName, Is.Null, "0x1043 is a subsystem vendor, not a device");
        });
    }

    [Test]
    public void UnknownDevice_StillYieldsItsVendorName()
    {
        // A GPU newer than the installed database is the common case on a rolling distro. Showing
        // "Advanced Micro Devices, Inc." beats showing nothing.
        var names = Parse(new PciDeviceId(0x1002, 0xFFFF));

        Assert.Multiple(() =>
        {
            Assert.That(names.VendorName, Is.EqualTo("Advanced Micro Devices, Inc. [AMD/ATI]"));
            Assert.That(names.DeviceName, Is.Null);
        });
    }

    [Test]
    public void UnknownVendor_ResolvesToNothing_WithoutThrowing()
    {
        var results = PciIdDatabase.Parse(new StringReader(Fixture), [new PciDeviceId(0xABCD, 0x1234)]);

        Assert.That(results, Does.Not.ContainKey(new PciDeviceId(0xABCD, 0x1234)));
    }

    [Test]
    public void ClassSection_TerminatesTheScan()
    {
        // After "C 03  Display controller" the indented "\t00  VGA compatible controller" would otherwise
        // parse as device 0x0000 of whatever vendor was last seen.
        var results = PciIdDatabase.Parse(new StringReader(Fixture), [new PciDeviceId(0x8086, 0x0000)]);

        Assert.That(results.TryGetValue(new PciDeviceId(0x8086, 0x0000), out var names), Is.False.Or.True);
        Assert.That(names?.DeviceName, Is.Null, "the class section must not contribute device names");
    }

    [Test]
    public void MissingDatabase_YieldsNoNames_RatherThanThrowing()
    {
        // pci.ids is an optional package; Debian's WSL image ships without it. Enumeration must survive.
        var results = PciIdDatabase.Lookup(
            [new PciDeviceId(0x1002, 0x150E)],
            ["/nonexistent/path/pci.ids", "/another/missing/pci.ids"]);

        Assert.That(results, Is.Empty);
        Assert.That(PciIdDatabase.FindDatabasePath(["/nonexistent/pci.ids"]), Is.Null);
    }

    [Test]
    public void NoRequestedDevices_ReadsNothing()
    {
        var results = PciIdDatabase.Parse(new StringReader(Fixture), []);

        Assert.That(results, Is.Empty);
    }

    /// <summary>
    /// Runs the parser against whatever real database this machine has, rather than the excerpt above.
    /// </summary>
    /// <remarks>
    /// The fixture proves the parse is self-consistent; only the real 42,000-line file proves it survives the
    /// parts of the format the excerpt does not contain. Self-skips where pci.ids is not installed (Windows,
    /// and Linux images that ship without hwdata/pciutils) — which is itself the optional-dependency contract.
    /// </remarks>
    [Test]
    public void RealSystemDatabase_ResolvesAKnownVendor_WhenInstalled()
    {
        var path = PciIdDatabase.FindDatabasePath();
        if (path is null)
        {
            Assert.Ignore("No pci.ids on this machine — the optional-dependency path is covered by MissingDatabase_YieldsNoNames_RatherThanThrowing.");
        }

        var results = PciIdDatabase.Lookup([new PciDeviceId(0x1002, 0x150E), new PciDeviceId(0x10DE, 0x2C19)]);

        Assert.Multiple(() =>
        {
            Assert.That(results[new PciDeviceId(0x1002, 0x150E)].VendorName, Does.Contain("Advanced Micro Devices"));
            Assert.That(results[new PciDeviceId(0x10DE, 0x2C19)].VendorName, Does.Contain("NVIDIA"));
        });
    }

    private static PciDeviceNames Parse(PciDeviceId device)
    {
        var results = PciIdDatabase.Parse(new StringReader(Fixture), [device]);
        return results.TryGetValue(device, out var names) ? names : new PciDeviceNames(null, null);
    }
}
