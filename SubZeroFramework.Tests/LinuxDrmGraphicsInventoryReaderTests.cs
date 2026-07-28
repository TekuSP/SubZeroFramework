using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Services.Compute;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the Linux DRM enumeration against a synthetic sysfs tree.
/// </summary>
/// <remarks>
/// The readers take their sysfs root as a constructor argument precisely so this is possible: the layout of
/// <c>/sys/class/drm</c> is stable and documented, so a fixture tree reproduces it faithfully and covers the
/// parts that would otherwise need three different laptops to test — an APU plus a discrete GPU, a suspended
/// card, a disconnected port, a missing pci.ids.
///
/// The tree below mirrors a Framework 16: an amdgpu APU (Radeon 890M, 1002:150e) driving the internal eDP
/// panel, plus an unplugged HDMI port.
/// </remarks>
[TestFixture]
public class LinuxDrmGraphicsInventoryReaderTests
{
    private const string FrameworkPanelEdidHex =
        "00ffffffffffff0009e5790d000000002a220104a5221678033d35ae5043b1250e5054" +
        "00000001010101010101010101010101010101347000a0a040a0603020360059d71000" +
        "001a000000000000000000000000000000000000000000fe00424f452043510a202020" +
        "202020000000fc004e4531363051444d2d4e5a360a012a";

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "szf-drm-" + Guid.NewGuid().ToString("N"));
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
            // A leaked temp tree is not worth failing a test over.
        }
    }

    [Test]
    public void EnumeratesAnApuAndItsInternalPanel_WithoutADisplayServer()
    {
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 536_870_912);
        WriteConnector("card0-eDP-1", status: "connected", enabled: "enabled", modes: "2560x1600\n1920x1200\n", edidHex: FrameworkPanelEdidHex);
        WriteConnector("card0-HDMI-A-1", status: "disconnected", enabled: "disabled", modes: string.Empty, edidHex: null);

        var inventory = CreateInventoryReader().Read();

        Assert.Multiple(() =>
        {
            Assert.That(inventory.VideoControllers, Has.Count.EqualTo(1));
            Assert.That(inventory.Monitors, Has.Count.EqualTo(1), "the disconnected HDMI port is not a monitor");
        });

        var monitor = inventory.Monitors[0];
        Assert.Multiple(() =>
        {
            Assert.That(monitor.UserFriendlyName, Is.EqualTo("NE160QDM-NZ6"));
            Assert.That(monitor.ManufacturerName, Is.EqualTo("BOE"));
            Assert.That(monitor.ProductCodeId, Is.EqualTo("0D79"));
            Assert.That(monitor.YearOfManufacture, Is.EqualTo(2024));
            Assert.That(monitor.WeekOfManufacture, Is.EqualTo(42));
            Assert.That(monitor.CurrentHorizontalResolution, Is.EqualTo(2560u));
            Assert.That(monitor.CurrentVerticalResolution, Is.EqualTo(1600u));
            Assert.That(monitor.MonitorType, Is.EqualTo("Internal panel"));
            Assert.That(monitor.Active, Is.True);
            // 2560 px across 345 mm ~= 188 DPI. Physical density, not the scaled desktop DPI Windows reports.
            Assert.That(monitor.PixelsPerXLogicalInch, Is.EqualTo(188u).Within(2u));
        });

        var controller = inventory.VideoControllers[0];
        Assert.Multiple(() =>
        {
            Assert.That(controller.AdapterRAM, Is.EqualTo(536_870_912uL));
            Assert.That(controller.CurrentHorizontalResolution, Is.EqualTo(2560u));
            Assert.That(controller.Description, Does.Contain("amdgpu").And.Contain("0000:c1:00.0"));
            // The connector belongs to card0 by kernel topology, so the link is exact, not a name guess.
            Assert.That(controller.LinkedMonitorDisplayNames, Has.Length.EqualTo(1));
            Assert.That(monitor.LinkedVideoControllerDisplayNames, Has.Length.EqualTo(1));
            Assert.That(monitor.LinkedVideoControllerDisplayNames[0], Is.EqualTo(controller.Name));
        });
    }

    [Test]
    public void RenderNodesAndConnectors_AreNotMistakenForCards()
    {
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0);
        WriteConnector("card0-eDP-1", "connected", "enabled", "1920x1080\n", FrameworkPanelEdidHex);
        Directory.CreateDirectory(Path.Combine(_root, "class", "drm", "renderD128"));
        Directory.CreateDirectory(Path.Combine(_root, "class", "drm", "renderD129"));

        var inventory = CreateInventoryReader().Read();

        Assert.That(inventory.VideoControllers, Has.Count.EqualTo(1), "renderD* are not GPUs");
    }

    [Test]
    public void TwoCards_EachKeepTheirOwnDisplays()
    {
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0);
        WriteConnector("card0-eDP-1", "connected", "enabled", "2560x1600\n", FrameworkPanelEdidHex);
        WriteAmdCard("card1", "0000:03:00.0", vendor: "1002", device: "7480", vramBytes: 8_589_934_592);
        WriteConnector("card1-DP-1", "connected", "enabled", "3840x2160\n", null);

        var inventory = CreateInventoryReader().Read();

        Assert.Multiple(() =>
        {
            Assert.That(inventory.VideoControllers, Has.Count.EqualTo(2));
            Assert.That(inventory.Monitors, Has.Count.EqualTo(2));
            Assert.That(inventory.VideoControllers[0].LinkedMonitorDisplayNames, Has.Length.EqualTo(1));
            Assert.That(inventory.VideoControllers[1].LinkedMonitorDisplayNames, Has.Length.EqualTo(1));
            Assert.That(inventory.VideoControllers[1].AdapterRAM, Is.EqualTo(8_589_934_592uL));
        });
    }

    [Test]
    public void ConnectedDisplayWithNoEdid_IsStillListed()
    {
        // A KVM or a long DP run can leave a connector connected but its EDID unreadable. The display exists.
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0);
        WriteConnector("card0-DP-2", "connected", "enabled", "1920x1080\n", edidHex: null);

        var inventory = CreateInventoryReader().Read();

        Assert.That(inventory.Monitors, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(inventory.Monitors[0].Name, Is.EqualTo("DP-2"), "falls back to the connector label");
            Assert.That(inventory.Monitors[0].CurrentHorizontalResolution, Is.EqualTo(1920u), "resolution still comes from modes");
            Assert.That(inventory.Monitors[0].ManufacturerName, Is.Null);
        });
    }

    [Test]
    public void NoDrmTree_ReportsUnavailableAndEmpty()
    {
        var reader = new LinuxDrmGraphicsInventoryReader(NullLogger<LinuxDrmGraphicsInventoryReader>.Instance, Path.Combine(_root, "missing"));

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsAvailable, Is.False);
            Assert.That(reader.Read().IsEmpty, Is.True);
        });
    }

    // ----- AMD utilization -----

    [Test]
    public void AmdReader_PublishesBusyPercentKeyedByPciAddress()
    {
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0, busyPercent: "37");

        var samples = CreateAmdReader().Sample();

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(samples[0].UtilizationPercent, Is.EqualTo(37d));
            Assert.That(samples[0].DeviceKey, Is.EqualTo("0000:c1:00.0"), "PCI address survives reboots; card index does not");
            Assert.That(samples[0].Kind, Is.EqualTo(SubZeroFramework.Models.ComputeDeviceKind.Gpu));
        });
    }

    [Test]
    public void AmdReader_DoesNotReadBusyPercentOfARuntimeSuspendedGpu()
    {
        // Reading gpu_busy_percent goes to the SMU and can RESUME a sleeping discrete GPU. A suspended card
        // is 0% busy by definition, so the power state answers the question without waking anything. The
        // fixture proves it by making the attribute say 99 — reading it would be visible in the result.
        WriteAmdCard("card1", "0000:03:00.0", vendor: "1002", device: "7480", vramBytes: 0, busyPercent: "99", runtimeStatus: "suspended");

        var samples = CreateAmdReader().Sample();

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].UtilizationPercent, Is.Zero, "a suspended GPU must read 0 without being woken");
    }

    [Test]
    public void AmdReader_IgnoresNonAmdCardsAndCardsWithoutTheAttribute()
    {
        WriteCard("card0", "0000:00:02.0", driver: "i915", vendor: "8086", device: "7d55");
        WriteAmdCard("card1", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0, busyPercent: null);

        var reader = CreateAmdReader();

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsAvailable, Is.False);
            Assert.That(reader.Sample(), Is.Empty);
        });
    }

    [Test]
    public void AmdReader_ClampsOutOfRangeReadings()
    {
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0, busyPercent: "120");

        Assert.That(CreateAmdReader().Sample()[0].UtilizationPercent, Is.EqualTo(100d));
    }

    [Test]
    public void CompositeReader_KeepsReportingWhenOneSourceThrows()
    {
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0, busyPercent: "12");

        var composite = new CompositeComputeUtilizationReader(
            [new ThrowingReader(), CreateAmdReader()],
            NullLogger<CompositeComputeUtilizationReader>.Instance);

        var samples = composite.Sample();

        Assert.Multiple(() =>
        {
            Assert.That(samples, Has.Count.EqualTo(1), "a broken vendor source must not blank out the others");
            Assert.That(samples[0].UtilizationPercent, Is.EqualTo(12d));
            Assert.That(composite.IsAvailable, Is.True);
        });
    }

    [Test]
    public void CompositeReader_PublishesEachDeviceOnce()
    {
        WriteAmdCard("card0", "0000:c1:00.0", vendor: "1002", device: "150e", vramBytes: 0, busyPercent: "5");

        var composite = new CompositeComputeUtilizationReader(
            [CreateAmdReader(), CreateAmdReader()],
            NullLogger<CompositeComputeUtilizationReader>.Instance);

        Assert.That(composite.Sample(), Has.Count.EqualTo(1), "the same DeviceKey from two sources is one device");
    }

    [Test]
    public void RealSystemSysfs_IsReadWithoutThrowing_WhateverThisMachineIs()
    {
        // Runs against the actual /sys of whatever this is: a Framework laptop with two GPUs, a CI container
        // with none, or Windows where the path does not exist at all. The contract is the same everywhere —
        // enumeration degrades to empty and never throws out of the inventory tier.
        var reader = new LinuxDrmGraphicsInventoryReader(NullLogger<LinuxDrmGraphicsInventoryReader>.Instance);
        var amd = new LinuxAmdGpuUtilizationReader(NullLogger<LinuxAmdGpuUtilizationReader>.Instance);

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => reader.Read());
            Assert.DoesNotThrow(() => _ = reader.IsAvailable);
            Assert.DoesNotThrow(() => amd.Sample());
            Assert.DoesNotThrow(() => _ = amd.IsAvailable);
        });

        if (!reader.IsAvailable)
        {
            Assert.Pass("No DRM tree on this machine; the empty-inventory path is what was exercised.");
        }

        // Where there IS a DRM tree, every adapter must at least come back with a non-blank name.
        foreach (var controller in reader.Read().VideoControllers)
        {
            Assert.That(controller.Name, Is.Not.Null.And.Not.Empty);
        }
    }

    private LinuxDrmGraphicsInventoryReader CreateInventoryReader() =>
        new(NullLogger<LinuxDrmGraphicsInventoryReader>.Instance, _root);

    private LinuxAmdGpuUtilizationReader CreateAmdReader() =>
        new(NullLogger<LinuxAmdGpuUtilizationReader>.Instance, _root);

    private void WriteAmdCard(string cardName, string pciSlot, string vendor, string device, long vramBytes, string? busyPercent = "0", string runtimeStatus = "active")
    {
        var devicePath = WriteCard(cardName, pciSlot, "amdgpu", vendor, device);

        if (busyPercent is not null)
        {
            File.WriteAllText(Path.Combine(devicePath, "gpu_busy_percent"), busyPercent + "\n");
        }

        if (vramBytes > 0)
        {
            File.WriteAllText(Path.Combine(devicePath, "mem_info_vram_total"), vramBytes + "\n");
        }

        var powerPath = Path.Combine(devicePath, "power");
        Directory.CreateDirectory(powerPath);
        File.WriteAllText(Path.Combine(powerPath, "runtime_status"), runtimeStatus + "\n");
    }

    private string WriteCard(string cardName, string pciSlot, string driver, string vendor, string device)
    {
        var devicePath = Path.Combine(_root, "class", "drm", cardName, "device");
        Directory.CreateDirectory(devicePath);

        // Verbatim uevent shape, including the bare (un-prefixed) hex in PCI_ID.
        File.WriteAllText(
            Path.Combine(devicePath, "uevent"),
            $"DRIVER={driver}\nPCI_CLASS=30000\nPCI_ID={vendor.ToUpperInvariant()}:{device.ToUpperInvariant()}\nPCI_SLOT_NAME={pciSlot}\nPCI_SUBSYS_ID=F111:0005\n");
        File.WriteAllText(Path.Combine(devicePath, "vendor"), $"0x{vendor}\n");
        File.WriteAllText(Path.Combine(devicePath, "device"), $"0x{device}\n");

        return devicePath;
    }

    private void WriteConnector(string connectorName, string status, string enabled, string modes, string? edidHex)
    {
        var path = Path.Combine(_root, "class", "drm", connectorName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "status"), status + "\n");
        File.WriteAllText(Path.Combine(path, "enabled"), enabled + "\n");
        File.WriteAllText(Path.Combine(path, "modes"), modes);

        // A connector with nothing plugged in exposes an EMPTY edid file, not a missing one.
        File.WriteAllBytes(
            Path.Combine(path, "edid"),
            edidHex is null ? [] : Convert.FromHexString(edidHex));
    }

    private sealed class ThrowingReader : SubZeroFramework.Models.IComputeUtilizationReader
    {
        public bool IsAvailable => throw new InvalidOperationException("vendor library exploded");

        public IReadOnlyList<SubZeroFramework.Models.ComputeDeviceUtilization> Sample() =>
            throw new InvalidOperationException("vendor library exploded");

        public void Dispose()
        {
        }
    }
}
