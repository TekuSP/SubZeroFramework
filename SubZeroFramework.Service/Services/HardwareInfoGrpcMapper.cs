using DynamicData;

using System.Linq;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

internal static class HardwareInfoGrpcMapper
{
    public static HardwareInfoReply MapHardwareInfoSnapshot(HardwareInfoSnapshot snapshot)
    {
        var reply = new HardwareInfoReply
        {
            ObservedAtUnixTimeMilliseconds = snapshot.ObservedAt.ToUnixTimeMilliseconds(),
            IsAvailable = snapshot.IsAvailable,
            LastError = snapshot.LastError ?? string.Empty,
        };

        if (snapshot.Inventory.OperatingSystem is not null)
        {
            reply.OperatingSystem = new HardwareInfoOperatingSystemReply
            {
                Name = snapshot.Inventory.OperatingSystem.Name ?? string.Empty,
                VersionString = snapshot.Inventory.OperatingSystem.VersionString ?? string.Empty,
            };
        }

        if (snapshot.Inventory.ComputerSystem is not null)
        {
            reply.ComputerSystem = new HardwareInfoComputerSystemReply
            {
                Vendor = snapshot.Inventory.ComputerSystem.Vendor ?? string.Empty,
                Caption = snapshot.Inventory.ComputerSystem.Caption ?? string.Empty,
                Description = snapshot.Inventory.ComputerSystem.Description ?? string.Empty,
                Name = snapshot.Inventory.ComputerSystem.Name ?? string.Empty,
                SkuNumber = snapshot.Inventory.ComputerSystem.Skunumber ?? string.Empty,
                Uuid = snapshot.Inventory.ComputerSystem.Uuid ?? string.Empty,
                Version = snapshot.Inventory.ComputerSystem.Version ?? string.Empty,
            };
        }

        if (snapshot.Inventory.Motherboard is not null)
        {
            reply.Motherboard = new HardwareInfoMotherboardReply
            {
                Manufacturer = snapshot.Inventory.Motherboard.Manufacturer ?? string.Empty,
                Product = snapshot.Inventory.Motherboard.Product ?? string.Empty,
                SerialNumber = snapshot.Inventory.Motherboard.SerialNumber ?? string.Empty,
            };
        }

        if (snapshot.Inventory.Bios is not null)
        {
            reply.Bios = new HardwareInfoBiosReply
            {
                Manufacturer = snapshot.Inventory.Bios.Manufacturer ?? string.Empty,
                Caption = snapshot.Inventory.Bios.Caption ?? string.Empty,
                Description = snapshot.Inventory.Bios.Description ?? string.Empty,
                Name = snapshot.Inventory.Bios.Name ?? string.Empty,
                Version = snapshot.Inventory.Bios.Version ?? string.Empty,
                ReleaseDate = snapshot.Inventory.Bios.ReleaseDate ?? string.Empty,
                SerialNumber = snapshot.Inventory.Bios.SerialNumber ?? string.Empty,
                SoftwareElementId = snapshot.Inventory.Bios.SoftwareElementId ?? string.Empty,
            };
        }

        if (snapshot.Runtime.MemoryStatus is not null)
        {
            reply.MemoryStatus = new HardwareInfoMemoryStatusReply
            {
                TotalPhysical = snapshot.Runtime.MemoryStatus.TotalPhysical,
                AvailablePhysical = snapshot.Runtime.MemoryStatus.AvailablePhysical,
                TotalPageFile = snapshot.Runtime.MemoryStatus.TotalPageFile,
                AvailablePageFile = snapshot.Runtime.MemoryStatus.AvailablePageFile,
                TotalVirtual = snapshot.Runtime.MemoryStatus.TotalVirtual,
                AvailableVirtual = snapshot.Runtime.MemoryStatus.AvailableVirtual,
                AvailableExtendedVirtual = snapshot.Runtime.MemoryStatus.AvailableExtendedVirtual,
            };
        }

        reply.Cpus.AddRange(snapshot.Runtime.Cpus.Select(MapHardwareInfoCpu));
        reply.MemoryModules.AddRange(snapshot.Inventory.MemoryModules.Select(MapHardwareInfoMemoryModule));
        reply.Monitors.AddRange(snapshot.Runtime.Monitors.Select(MapHardwareInfoMonitor));
        reply.VideoControllers.AddRange(snapshot.Runtime.VideoControllers.Select(MapHardwareInfoVideoController));
        reply.Drives.AddRange(snapshot.Inventory.Drives.Select(MapHardwareInfoDrive));
        reply.NetworkAdapters.AddRange(snapshot.Inventory.NetworkAdapters.Select(MapHardwareInfoNetworkAdapter));

        return reply;
    }

    public static HardwareInfoHistoryChangeReply MapHardwareInfoHistoryChange(Change<HistoricalRecord<HardwareInfoSnapshot>, long> change)
    {
        return new HardwareInfoHistoryChangeReply
        {
            ChangeKind = TelemetryGrpcMapper.MapChangeReason(change.Reason),
            SampleId = change.Key,
            ObservedAtUnixTimeMilliseconds = change.Current.ObservedAt.ToUnixTimeMilliseconds(),
            Snapshot = MapHardwareInfoSnapshot(change.Current.Value),
        };
    }

    public static HardwareInfoHistoryChangeBatchReply MapHardwareInfoHistoryBatch(IReadOnlyList<HardwareInfoHistoryChangeReply> replies)
    {
        var batch = new HardwareInfoHistoryChangeBatchReply();
        batch.Changes.AddRange(replies);
        return batch;
    }

    private static HardwareInfoCpuReply MapHardwareInfoCpu(HardwareInfoCpu cpu)
    {
        var reply = new HardwareInfoCpuReply
        {
            Name = cpu.Name ?? string.Empty,
            Caption = cpu.Caption ?? string.Empty,
            Description = cpu.Description ?? string.Empty,
            Manufacturer = cpu.Manufacturer ?? string.Empty,
            Cores = checked((uint)Math.Max(cpu.Cores, 0)),
            LogicalProcessors = checked((uint)Math.Max(cpu.LogicalProcessors, 0)),
            CurrentClockSpeedMhz = checked((uint)Math.Max(cpu.CurrentClockSpeedMHz, 0)),
            MaxClockSpeedMhz = checked((uint)Math.Max(cpu.MaxClockSpeedMHz, 0)),
            ProcessorId = cpu.ProcessorId ?? string.Empty,
            SocketDesignation = cpu.SocketDesignation ?? string.Empty,
            L1CacheSizeKb = checked((uint)Math.Max(cpu.L1CacheSizeKb, 0)),
            L2CacheSizeKb = checked((uint)Math.Max(cpu.L2CacheSizeKb, 0)),
            L3CacheSizeKb = checked((uint)Math.Max(cpu.L3CacheSizeKb, 0)),
            SecondLevelAddressTranslationExtensions = cpu.SecondLevelAddressTranslationExtensions,
            VirtualizationFirmwareEnabled = cpu.VirtualizationFirmwareEnabled,
            VmMonitorModeExtensions = cpu.VMMonitorModeExtensions,
        };

        if (cpu.PercentProcessorTime is { } percentProcessorTime)
        {
            reply.PercentProcessorTime = percentProcessorTime;
        }

        reply.CpuCores.AddRange(cpu.CpuCores.Select(MapHardwareInfoCpuCore));

        return reply;
    }

    private static HardwareInfoCpuCoreReply MapHardwareInfoCpuCore(HardwareInfoCpuCore cpuCore)
    {
        return new HardwareInfoCpuCoreReply
        {
            Name = cpuCore.Name ?? string.Empty,
            PercentProcessorTime = cpuCore.PercentProcessorTime,
        };
    }

    private static HardwareInfoMemoryModuleReply MapHardwareInfoMemoryModule(HardwareInfoMemoryModule module)
    {
        return new HardwareInfoMemoryModuleReply
        {
            BankLabel = module.BankLabel ?? string.Empty,
            CapacityBytes = module.CapacityBytes,
            DataWidth = module.DataWidth,
            MemoryType = module.MemoryType ?? string.Empty,
            FormFactor = module.FormFactor ?? string.Empty,
            SpeedMhz = module.SpeedMHz,
            MaxVoltage = module.MaxVoltage,
            MinVoltage = module.MinVoltage,
            Manufacturer = module.Manufacturer ?? string.Empty,
            PartNumber = module.PartNumber ?? string.Empty,
            SerialNumber = module.SerialNumber ?? string.Empty,
        };
    }

    private static HardwareInfoMonitorReply MapHardwareInfoMonitor(HardwareInfoMonitor monitor)
    {
        return new HardwareInfoMonitorReply
        {
            Active = monitor.Active,
            Caption = monitor.Caption ?? string.Empty,
            Description = monitor.Description ?? string.Empty,
            ManufacturerName = monitor.ManufacturerName ?? string.Empty,
            MonitorManufacturer = monitor.MonitorManufacturer ?? string.Empty,
            MonitorType = monitor.MonitorType ?? string.Empty,
            Name = monitor.Name ?? string.Empty,
            PixelsPerXLogicalInch = monitor.PixelsPerXLogicalInch,
            PixelsPerYLogicalInch = monitor.PixelsPerYLogicalInch,
            ProductCodeId = monitor.ProductCodeId ?? string.Empty,
            SerialNumberId = monitor.SerialNumberId ?? string.Empty,
            UserFriendlyName = monitor.UserFriendlyName ?? string.Empty,
            WeekOfManufacture = monitor.WeekOfManufacture,
            YearOfManufacture = monitor.YearOfManufacture,
        };
    }

    private static HardwareInfoDriveReply MapHardwareInfoDrive(HardwareInfoDrive drive)
    {
        return new HardwareInfoDriveReply
        {
            Index = drive.Index,
            Name = drive.Name ?? string.Empty,
            Model = drive.Model ?? string.Empty,
            Caption = drive.Caption ?? string.Empty,
            Description = drive.Description ?? string.Empty,
            Manufacturer = drive.Manufacturer ?? string.Empty,
            MediaType = drive.MediaType ?? string.Empty,
            SerialNumber = drive.SerialNumber ?? string.Empty,
            FirmwareRevision = drive.FirmwareRevision ?? string.Empty,
            Size = drive.Size,
            FreeSpace = drive.FreeSpace,
        };
    }

    private static HardwareInfoNetworkAdapterReply MapHardwareInfoNetworkAdapter(HardwareInfoNetworkAdapter adapter)
    {
        var reply = new HardwareInfoNetworkAdapterReply
        {
            Name = adapter.Name ?? string.Empty,
            NetConnectionId = adapter.NetConnectionId ?? string.Empty,
            ProductName = adapter.ProductName ?? string.Empty,
            Caption = adapter.Caption ?? string.Empty,
            Description = adapter.Description ?? string.Empty,
            Manufacturer = adapter.Manufacturer ?? string.Empty,
            AdapterType = adapter.AdapterType ?? string.Empty,
            MacAddress = adapter.MacAddress ?? string.Empty,
            Speed = adapter.Speed,
        };

        reply.IpAddresses.AddRange(adapter.IpAddresses);
        reply.DefaultGateways.AddRange(adapter.DefaultGateways);

        return reply;
    }

    private static HardwareInfoVideoControllerReply MapHardwareInfoVideoController(HardwareInfoVideoController videoController)
    {
        return new HardwareInfoVideoControllerReply
        {
            Name = videoController.Name ?? string.Empty,
            AdapterRam = videoController.AdapterRAM,
            Caption = videoController.Caption ?? string.Empty,
            CurrentBitsPerPixel = videoController.CurrentBitsPerPixel,
            CurrentHorizontalResolution = videoController.CurrentHorizontalResolution,
            CurrentNumberOfColors = videoController.CurrentNumberOfColors,
            CurrentRefreshRate = videoController.CurrentRefreshRate,
            CurrentVerticalResolution = videoController.CurrentVerticalResolution,
            Description = videoController.Description ?? string.Empty,
            DriverDate = videoController.DriverDate ?? string.Empty,
            DriverVersion = videoController.DriverVersion ?? string.Empty,
            Manufacturer = videoController.Manufacturer ?? string.Empty,
            MaxRefreshRate = videoController.MaxRefreshRate,
            MinRefreshRate = videoController.MinRefreshRate,
            VideoModeDescription = videoController.VideoModeDescription ?? string.Empty,
            VideoProcessor = videoController.VideoProcessor ?? string.Empty,
        };
    }
}
