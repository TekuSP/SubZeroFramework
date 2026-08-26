using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// The gate that decides whether a GPU may be left alone.
/// </summary>
/// <remarks>
/// Both directions are dangerous and neither is symmetric. Failing to suppress a read wakes a sleeping
/// discrete GPU — measured at roughly 19 W on the reference machine. Suppressing one wrongly reports a busy
/// GPU as producing no power at all, which an adaptive fan then feed-forwards from.
/// </remarks>
[TestFixture]
public class ComputeDeviceSleepGateTests
{
    /// <summary>
    /// An empty set is never "all asleep".
    /// </summary>
    /// <remarks>
    /// Vacuous truth would be the wrong answer here: a reader that has not yet discovered its devices would
    /// suppress the very first read and so never discover them.
    /// </remarks>
    [Test]
    public void AreAllAsleep_IsFalse_WhenNoDevicesAreKnown()
    {
        Assert.That(ComputeDeviceSleepGate.AreAllAsleep([]), Is.False);
    }

    /// <summary>
    /// A device whose power state cannot be read is sampled normally.
    /// </summary>
    /// <remarks>
    /// This is the load-bearing case for every platform the lookup does not understand — including the Linux
    /// TFM, where the interop is not even compiled in. Treating "do not know" as "asleep" would report every
    /// GPU as permanently idle there.
    /// </remarks>
    [Test]
    public void AreAllAsleep_IsFalse_WhenThePowerStateIsUnknown()
    {
        // No real device answers to this, so the lookup returns "unknown" on every platform.
        var unknown = new ComputeDeviceIdentity
        {
            DeviceKey = @"PCI\VEN_DEAD&DEV_BEEF\NOT_A_REAL_DEVICE",
            Kind = ComputeDeviceKind.Gpu,
            DisplayName = "Absent",
        };

        Assert.That(ComputeDeviceSleepGate.AreAllAsleep([unknown]), Is.False);
    }

    /// <summary>A null or blank key is an unknown state, not a sleeping device.</summary>
    [Test]
    public void AreAllAsleep_IsFalse_WhenTheDeviceKeyIsMissing()
    {
        var blank = new ComputeDeviceIdentity
        {
            DeviceKey = string.Empty,
            Kind = ComputeDeviceKind.Gpu,
            DisplayName = "Unkeyed",
        };

        Assert.That(ComputeDeviceSleepGate.AreAllAsleep([blank]), Is.False);
    }
}
