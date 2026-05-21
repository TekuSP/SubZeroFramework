using System.Collections.Immutable;

using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

[TestFixture]
public class HardwareInfoDisplayModelTests
{
    [Test]
    public void DisplayCurrentResolution_WhenMonitorReportsResolution_ReturnsFormattedResolution()
    {
        var monitor = new HardwareInfoMonitor(
            Active: true,
            Caption: "Built-in Display",
            Description: null,
            ManufacturerName: "BOE",
            MonitorManufacturer: "BOE",
            MonitorType: "Internal",
            Name: "Display 1",
            PixelsPerXLogicalInch: 120,
            PixelsPerYLogicalInch: 120,
            ProductCodeId: "1234",
            SerialNumberId: "5678",
            UserFriendlyName: "Framework Panel",
            WeekOfManufacture: 12,
            YearOfManufacture: 2024,
            CurrentHorizontalResolution: 2880,
            CurrentVerticalResolution: 1920,
            CurrentRefreshRate: 120,
            LinkedVideoControllerDisplayNames: ["Intel Arc"]);

        Assert.Multiple(() =>
        {
            Assert.That(monitor.DisplayCurrentResolution, Is.EqualTo("2,880 x 1,920"));
            Assert.That(monitor.DisplayCurrentRefreshRate, Is.EqualTo("120 Hz"));
        });
    }

    [Test]
    public void LinkedVideoControllerSummary_WhenMonitorHasLinkedControllers_ReturnsJoinedSummary()
    {
        var monitor = new HardwareInfoMonitor(
            Active: true,
            Caption: "External Display",
            Description: null,
            ManufacturerName: "Dell",
            MonitorManufacturer: "DEL",
            MonitorType: "External",
            Name: "U2720Q",
            PixelsPerXLogicalInch: 163,
            PixelsPerYLogicalInch: 163,
            ProductCodeId: "DELL1",
            SerialNumberId: "ABC123",
            UserFriendlyName: "Dell 27",
            WeekOfManufacture: 4,
            YearOfManufacture: 2025,
            CurrentHorizontalResolution: 3840,
            CurrentVerticalResolution: 2160,
            CurrentRefreshRate: 60,
            LinkedVideoControllerDisplayNames: ["Intel Arc", "Radeon 7700S"]);

        Assert.That(monitor.DisplayLinkedVideoControllerSummary, Is.EqualTo("Intel Arc, Radeon 7700S"));
    }

    [Test]
    public void LinkedMonitorSummary_WhenVideoControllerHasNoLinkedMonitors_ReturnsFallbackMessage()
    {
        var controller = new HardwareInfoVideoController(
            AdapterRAM: 8UL * 1024UL * 1024UL * 1024UL,
            Caption: "Intel Graphics",
            CurrentBitsPerPixel: 32,
            CurrentHorizontalResolution: 2880,
            CurrentNumberOfColors: 0,
            CurrentRefreshRate: 120,
            CurrentVerticalResolution: 1920,
            Description: "Integrated GPU",
            DriverDate: "20260101",
            DriverVersion: "1.2.3",
            Manufacturer: "Intel",
            MaxRefreshRate: 165,
            MinRefreshRate: 30,
            Name: "Intel Arc",
            VideoModeDescription: "2880 x 1920 x 32",
            VideoProcessor: "Intel Arc Graphics",
            LinkedMonitorDisplayNames: ImmutableArray<string>.Empty);

        Assert.That(controller.DisplayLinkedMonitorSummary, Is.EqualTo("No linked monitors reported"));
    }

    [Test]
    public void HasCpuCoreDetails_WhenCpuContainsCoreSnapshots_ReturnsTrue()
    {
        var cpu = new HardwareInfoCpu(
            Name: "AMD Ryzen",
            Caption: "AMD Ryzen",
            Description: null,
            Manufacturer: "AMD",
            Cores: 8,
            LogicalProcessors: 16,
            CurrentClockSpeedMHz: 3300,
            MaxClockSpeedMHz: 5100,
            ProcessorId: "ABC",
            SocketDesignation: "AM5",
            L1CacheSizeKb: 512,
            L2CacheSizeKb: 8192,
            L3CacheSizeKb: 32768,
            SecondLevelAddressTranslationExtensions: true,
            VirtualizationFirmwareEnabled: true,
            VMMonitorModeExtensions: true,
            PercentProcessorTime: 32d,
            CpuCores: [new HardwareInfoCpuCore("0", 14d), new HardwareInfoCpuCore("1", 22d)]);

        Assert.Multiple(() =>
        {
            Assert.That(cpu.HasCpuCoreDetails, Is.True);
            Assert.That(cpu.EffectivePercentProcessorTime, Is.EqualTo(32d));
        });
    }
}