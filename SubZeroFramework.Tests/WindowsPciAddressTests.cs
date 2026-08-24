using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the PCI address format that joins a Windows PnP device to NVML's view of the same GPU.
/// </summary>
/// <remarks>
/// The reference case is the Framework 16's NVIDIA graphics module: Windows reports bus 194 / address 0, and
/// NVML reports <c>0000:C2:00.0</c>. If these two stop agreeing, the GPU is published twice — once with
/// utilisation and once with power and temperature — instead of once with both.
/// </remarks>
[TestFixture]
public class WindowsPciAddressTests
{
    [Test]
    public void Format_ProducesWhatNvmlReportsForTheReferenceGpu()
    {
        // Measured on the reference machine: DEVPKEY_Device_BusNumber = 194, DEVPKEY_Device_Address = 0,
        // and NVML independently reported 0000:C2:00.0 for the same RTX 5070.
        Assert.That(WindowsPciAddress.Format(194u, 0u), Is.EqualTo("0000:c2:00.0"));
    }

    [Test]
    public void Format_UnpacksTheDeviceAndFunctionFromTheAddress()
    {
        // DEVPKEY_Device_Address packs device in the high 16 bits and function in the low 16. Reading it as a
        // flat number would put every multi-function device at function 0 and collide them.
        Assert.Multiple(() =>
        {
            Assert.That(WindowsPciAddress.Format(0u, (3u << 16) | 1u), Is.EqualTo("0000:00:03.1"));
            Assert.That(WindowsPciAddress.Format(0xC7u, (0u << 16) | 1u), Is.EqualTo("0000:c7:00.1"));
        });
    }

    [Test]
    public void Format_IsLowerCaseAndZeroPaddedLikeNvml()
    {
        // NVML writes busIdLegacy in this exact shape. A differently-cased or unpadded string compares unequal
        // and the join silently never matches.
        Assert.That(WindowsPciAddress.Format(0x0Au, 0u), Is.EqualTo("0000:0a:00.0"));
    }

    [Test]
    public void Format_ReturnsNothingWhenEitherPropertyIsMissing()
    {
        // Not every PnP device is on PCI. Inventing an address for one would create a phantom join target.
        Assert.Multiple(() =>
        {
            Assert.That(WindowsPciAddress.Format(null, 0u), Is.Null);
            Assert.That(WindowsPciAddress.Format(194u, null), Is.Null);
            Assert.That(WindowsPciAddress.Format(null, null), Is.Null);
        });
    }

    [Test]
    public void Matches_IgnoresCase()
    {
        // NVML has been seen to report upper-case hex ("0000:C2:00.0") while this formats lower-case.
        Assert.That(WindowsPciAddress.Matches("0000:C2:00.0", "0000:c2:00.0"), Is.True);
    }

    [Test]
    public void Matches_RejectsMissingAddresses()
    {
        // Two devices that both lack an address are not therefore the same device.
        Assert.Multiple(() =>
        {
            Assert.That(WindowsPciAddress.Matches(null, null), Is.False);
            Assert.That(WindowsPciAddress.Matches(string.Empty, string.Empty), Is.False);
            Assert.That(WindowsPciAddress.Matches("0000:c2:00.0", null), Is.False);
        });
    }

    [Test]
    public void Matches_RejectsDifferentDevices()
    {
        Assert.That(WindowsPciAddress.Matches("0000:c2:00.0", "0000:c2:00.1"), Is.False);
    }
}
