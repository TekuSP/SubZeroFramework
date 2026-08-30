using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The firmware inventory that Device Capabilities and Modules render.
/// </summary>
[TestFixture]
public class FirmwareInventorySnapshotTests
{
    [Test]
    public void FormatVersion_JoinsTheThreeDescriptorBytes()
        => Assert.That(FirmwareComponent.FormatVersion(1, 2, 3), Is.EqualTo("1.2.3"));

    /// <summary>
    /// Consumers hide the whole section on false. A firmware panel listing nothing reads as a broken
    /// feature, where an absent one reads as a machine that does not report versions.
    /// </summary>
    [Test]
    public void HasAny_IsFalseOnTheEmptySnapshot()
        => Assert.That(FirmwareInventorySnapshot.Empty.HasAny, Is.False);

    [Test]
    public void HasAny_IsTrueForAnySingleGroup()
    {
        Assert.Multiple(() =>
        {
            Assert.That((FirmwareInventorySnapshot.Empty with { Cameras = [Component(0)] }).HasAny, Is.True);
            Assert.That((FirmwareInventorySnapshot.Empty with { InputModules = [Component(1)] }).HasAny, Is.True);
            Assert.That((FirmwareInventorySnapshot.Empty with { UsbHubs = [Component(0)] }).HasAny, Is.True);
            Assert.That((FirmwareInventorySnapshot.Empty with { AudioCards = [Component(0)] }).HasAny, Is.True);
            Assert.That((FirmwareInventorySnapshot.Empty with { PowerDeliveryControllers = [Component(0)] }).HasAny, Is.True);
        });
    }

    /// <summary>
    /// The retimer is a lone string rather than a group, and it is easy to leave out of an "anything at all"
    /// check — which on a machine reporting only a retimer would hide the one thing it had to say.
    /// </summary>
    [Test]
    public void HasAny_CountsARetimerVersionOnItsOwn()
        => Assert.That((FirmwareInventorySnapshot.Empty with { RetimerVersion = "1.2.3" }).HasAny, Is.True);

    [Test]
    public void HasAny_CountsAnNvmeDriveOnItsOwn()
    {
        var snapshot = FirmwareInventorySnapshot.Empty with
        {
            NvmeDrives = [new NvmeFirmware(@"\\.\PhysicalDrive0", "WD_BLACK SN850X", "620361WD")],
        };

        Assert.That(snapshot.HasAny, Is.True);
    }

    private static FirmwareComponent Component(int slotIndex)
        => new(slotIndex, "Test module", "1.0.0", VendorId: 0x32AC, ProductId: 0x0012);
}
