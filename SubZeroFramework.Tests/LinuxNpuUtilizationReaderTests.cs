using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Compute;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the Linux NPU readers against a synthetic <c>/sys/class/accel</c> tree.
/// </summary>
/// <remarks>
/// The two drivers are completely different interfaces — Intel accumulates busy microseconds in sysfs, AMD
/// answers an ioctl with instantaneous per-column percentages — so they share only the enumeration.
/// </remarks>
[TestFixture]
public class LinuxNpuUtilizationReaderTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "szf-npu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void IntelReader_NeedsTwoSamples_AndComputesABusyShareFromTheDelta()
    {
        // 250,000 us of busy time accrued over a ~0.5 s wall interval is 50%. The wall clock is real here, so
        // the assertion is a generous band rather than an exact figure — what is pinned is that the delta is
        // divided by elapsed time, not reported raw.
        WriteAccelDevice("accel0", "intel_vpu", "0000:00:0b.0", vendor: "8086", device: "7d1d");
        WriteBusyTime("accel0", 1_000_000);

        var reader = CreateIntelReader();

        Assert.That(reader.Sample(), Is.Empty, "the first sample only establishes a baseline");

        Thread.Sleep(500);
        WriteBusyTime("accel0", 1_250_000);

        var samples = reader.Sample();
        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(samples[0].Kind, Is.EqualTo(ComputeDeviceKind.Npu));
            Assert.That(samples[0].DeviceKey, Is.EqualTo("0000:00:0b.0"));
            Assert.That(samples[0].UtilizationPercent, Is.GreaterThan(20d).And.LessThanOrEqualTo(100d));
        });
    }

    [Test]
    public void IntelReader_IdleNpuReportsZero()
    {
        WriteAccelDevice("accel0", "intel_vpu", "0000:00:0b.0", vendor: "8086", device: "7d1d");
        WriteBusyTime("accel0", 5_000_000);

        var reader = CreateIntelReader();
        reader.Sample();

        Thread.Sleep(120);
        // Counter unchanged: no job was outstanding during the interval.
        var samples = reader.Sample();

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].UtilizationPercent, Is.Zero);
    }

    [Test]
    public void IntelReader_CounterReset_ReportsNothingRatherThanZero()
    {
        // The counter restarts on module reload. A negative delta must not be reported as an idle NPU.
        WriteAccelDevice("accel0", "intel_vpu", "0000:00:0b.0", vendor: "8086", device: "7d1d");
        WriteBusyTime("accel0", 9_000_000);

        var reader = CreateIntelReader();
        reader.Sample();

        WriteBusyTime("accel0", 12_000);

        Assert.That(reader.Sample(), Is.Empty, "a reset counter is unknown, not zero");
    }

    [Test]
    public void IntelReader_ClampsImplausibleDeltas()
    {
        // A counter jump far larger than the elapsed interval would otherwise report thousands of percent.
        WriteAccelDevice("accel0", "intel_vpu", "0000:00:0b.0", vendor: "8086", device: "7d1d");
        WriteBusyTime("accel0", 0);

        var reader = CreateIntelReader();
        reader.Sample();

        Thread.Sleep(50);
        WriteBusyTime("accel0", 60_000_000);

        Assert.That(reader.Sample()[0].UtilizationPercent, Is.EqualTo(100d));
    }

    [Test]
    public void IntelReader_IgnoresNonIvpuDevicesAndKernelsWithoutTheAttribute()
    {
        WriteAccelDevice("accel0", "amdxdna", "0000:c7:00.1", vendor: "1022", device: "17f0");
        // An ivpu device on a kernel too old to expose the counter: listed by the resolver, but unreadable.
        WriteAccelDevice("accel1", "intel_vpu", "0000:00:0b.0", vendor: "8086", device: "7d1d");

        var reader = CreateIntelReader();

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsAvailable, Is.False);
            Assert.That(reader.Sample(), Is.Empty);
        });
    }

    [Test]
    public void AmdReader_SuspendedNpuReportsZero_WithoutOpeningTheDeviceNode()
    {
        // THE load-bearing behaviour. Querying AMD's sensors takes a runtime-PM reference and RESUMES the
        // NPU, so a suspended device must be answered from sysfs alone. The fixture has no /dev/accel node at
        // all, so if the reader tried to issue the ioctl it could not produce a reading — the 0% below can
        // only come from the power-state gate.
        WriteAccelDevice("accel0", "amdxdna", "0000:c7:00.1", vendor: "1022", device: "17f0", runtimeStatus: "suspended");
        var nodeRoot = CreateDeviceNodeFor("accel0");

        var samples = CreateAmdReader(nodeRoot).Sample();

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(samples[0].UtilizationPercent, Is.Zero);
            Assert.That(samples[0].Kind, Is.EqualTo(ComputeDeviceKind.Npu));
            Assert.That(samples[0].DeviceKey, Is.EqualTo("0000:c7:00.1"));
        });
    }

    [Test]
    public void AmdReader_ActiveNpuWithoutSensorSupport_LatchesOffInsteadOfRetryingForever()
    {
        // On an active NPU the reader does issue the ioctl. Against a regular file standing in for the device
        // node it fails, which is what an older kernel or an unsupported part looks like. Because every
        // attempt resumes the NPU, a permanent failure must be asked once and then dropped.
        WriteAccelDevice("accel0", "amdxdna", "0000:c7:00.1", vendor: "1022", device: "17f0", runtimeStatus: "active");
        var nodeRoot = CreateDeviceNodeFor("accel0");

        var reader = CreateAmdReader(nodeRoot);

        Assert.Multiple(() =>
        {
            Assert.That(reader.Sample(), Is.Empty);
            Assert.That(reader.Sample(), Is.Empty, "and it stays quiet rather than probing again every tick");
        });
    }

    [Test]
    public void AmdReader_NoDeviceNode_ReportsNothing()
    {
        WriteAccelDevice("accel0", "amdxdna", "0000:c7:00.1", vendor: "1022", device: "17f0");

        var reader = CreateAmdReader(Path.Combine(_root, "dev-missing"));

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsAvailable, Is.False);
            Assert.That(reader.Sample(), Is.Empty);
        });
    }

    [Test]
    public void IdentityResolver_DescribesNpusFromSysfs()
    {
        WriteAccelDevice("accel0", "amdxdna", "0000:c7:00.1", vendor: "1022", device: "17f0");
        File.WriteAllText(Path.Combine(_root, "class", "accel", "accel0", "device", "fw_version"), "1.5.5.391\n");
        File.WriteAllText(Path.Combine(_root, "class", "accel", "accel0", "device", "vbnv"), "RyzenAI-npu4\n");

        var identities = new LinuxComputeDeviceIdentityResolver(
            NullLogger<LinuxComputeDeviceIdentityResolver>.Instance,
            _root).Enumerate();

        Assert.That(identities, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(identities[0].Kind, Is.EqualTo(ComputeDeviceKind.Npu), "GPUs are inventoried as video controllers, not here");
            Assert.That(identities[0].DeviceKey, Is.EqualTo("0000:c7:00.1"));
            Assert.That(identities[0].DriverName, Is.EqualTo("amdxdna"));
            Assert.That(identities[0].FirmwareVersion, Is.EqualTo("1.5.5.391"));
            Assert.That(identities[0].Description, Is.EqualTo("RyzenAI-npu4"));
            Assert.That(identities[0].Location, Is.EqualTo("0000:c7:00.1"));
        });
    }

    [Test]
    public void IdentityResolver_NoAccelClass_ReportsNothing()
    {
        var identities = new LinuxComputeDeviceIdentityResolver(
            NullLogger<LinuxComputeDeviceIdentityResolver>.Instance,
            Path.Combine(_root, "missing")).Enumerate();

        Assert.That(identities, Is.Empty);
    }

    [Test]
    public void RealSystemAccelClass_IsProbedWithoutThrowing()
    {
        var intel = new LinuxIntelNpuUtilizationReader(NullLogger<LinuxIntelNpuUtilizationReader>.Instance);
        var amd = new LinuxAmdXdnaNpuUtilizationReader(NullLogger<LinuxAmdXdnaNpuUtilizationReader>.Instance);
        var resolver = new LinuxComputeDeviceIdentityResolver(NullLogger<LinuxComputeDeviceIdentityResolver>.Instance);

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => intel.Sample());
            Assert.DoesNotThrow(() => amd.Sample());
            Assert.DoesNotThrow(() => resolver.Enumerate());
        });
    }

    private LinuxIntelNpuUtilizationReader CreateIntelReader() =>
        new(NullLogger<LinuxIntelNpuUtilizationReader>.Instance, _root);

    private LinuxAmdXdnaNpuUtilizationReader CreateAmdReader(string deviceNodeRoot) =>
        new(NullLogger<LinuxAmdXdnaNpuUtilizationReader>.Instance, _root, deviceNodeRoot);

    private string CreateDeviceNodeFor(string nodeName)
    {
        var nodeRoot = Path.Combine(_root, "dev", "accel");
        Directory.CreateDirectory(nodeRoot);
        File.WriteAllText(Path.Combine(nodeRoot, nodeName), string.Empty);
        return nodeRoot;
    }

    private void WriteAccelDevice(string nodeName, string driver, string pciSlot, string vendor, string device, string runtimeStatus = "active")
    {
        var devicePath = Path.Combine(_root, "class", "accel", nodeName, "device");
        Directory.CreateDirectory(devicePath);

        File.WriteAllText(
            Path.Combine(devicePath, "uevent"),
            $"DRIVER={driver}\nPCI_ID={vendor.ToUpperInvariant()}:{device.ToUpperInvariant()}\nPCI_SLOT_NAME={pciSlot}\n");
        File.WriteAllText(Path.Combine(devicePath, "vendor"), $"0x{vendor}\n");
        File.WriteAllText(Path.Combine(devicePath, "device"), $"0x{device}\n");

        var powerPath = Path.Combine(devicePath, "power");
        Directory.CreateDirectory(powerPath);
        File.WriteAllText(Path.Combine(powerPath, "runtime_status"), runtimeStatus + "\n");
    }

    private void WriteBusyTime(string nodeName, long microseconds) =>
        File.WriteAllText(
            Path.Combine(_root, "class", "accel", nodeName, "device", "npu_busy_time_us"),
            microseconds + "\n");
}
