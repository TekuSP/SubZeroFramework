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

        if (snapshot.OperatingSystem is not null)
        {
            reply.OperatingSystem = new HardwareInfoOperatingSystemReply
            {
                Name = snapshot.OperatingSystem.Name ?? string.Empty,
                VersionString = snapshot.OperatingSystem.VersionString ?? string.Empty,
            };
        }

        if (snapshot.ComputerSystem is not null)
        {
            reply.ComputerSystem = new HardwareInfoComputerSystemReply
            {
                Vendor = snapshot.ComputerSystem.Vendor ?? string.Empty,
                Caption = snapshot.ComputerSystem.Caption ?? string.Empty,
                Description = snapshot.ComputerSystem.Description ?? string.Empty,
                Name = snapshot.ComputerSystem.Name ?? string.Empty,
                SkuNumber = snapshot.ComputerSystem.Skunumber ?? string.Empty,
                Uuid = snapshot.ComputerSystem.Uuid ?? string.Empty,
                Version = snapshot.ComputerSystem.Version ?? string.Empty,
            };
        }

        if (snapshot.Motherboard is not null)
        {
            reply.Motherboard = new HardwareInfoMotherboardReply
            {
                Manufacturer = snapshot.Motherboard.Manufacturer ?? string.Empty,
                Product = snapshot.Motherboard.Product ?? string.Empty,
                SerialNumber = snapshot.Motherboard.SerialNumber ?? string.Empty,
            };
        }

        if (snapshot.Bios is not null)
        {
            reply.Bios = new HardwareInfoBiosReply
            {
                Manufacturer = snapshot.Bios.Manufacturer ?? string.Empty,
                Caption = snapshot.Bios.Caption ?? string.Empty,
                Description = snapshot.Bios.Description ?? string.Empty,
                Name = snapshot.Bios.Name ?? string.Empty,
                Version = snapshot.Bios.Version ?? string.Empty,
                ReleaseDate = snapshot.Bios.ReleaseDate ?? string.Empty,
                SerialNumber = snapshot.Bios.SerialNumber ?? string.Empty,
                SoftwareElementId = snapshot.Bios.SoftwareElementId ?? string.Empty,
            };
        }

        if (snapshot.MemoryStatus is not null)
        {
            reply.MemoryStatus = new HardwareInfoMemoryStatusReply
            {
                TotalPhysical = snapshot.MemoryStatus.TotalPhysical,
                AvailablePhysical = snapshot.MemoryStatus.AvailablePhysical,
                TotalPageFile = snapshot.MemoryStatus.TotalPageFile,
                AvailablePageFile = snapshot.MemoryStatus.AvailablePageFile,
                TotalVirtual = snapshot.MemoryStatus.TotalVirtual,
                AvailableVirtual = snapshot.MemoryStatus.AvailableVirtual,
                AvailableExtendedVirtual = snapshot.MemoryStatus.AvailableExtendedVirtual,
            };
        }

        reply.Cpus.AddRange(snapshot.Cpus.Select(MapHardwareInfoCpu));
        reply.MemoryModules.AddRange(snapshot.MemoryModules.Select(MapHardwareInfoMemoryModule));
        reply.VideoControllers.AddRange(snapshot.VideoControllers.Select(MapHardwareInfoVideoController));

        return reply;
    }

    private static HardwareInfoCpuReply MapHardwareInfoCpu(HardwareInfoCpu cpu)
    {
        return new HardwareInfoCpuReply
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
            PercentProcessorTime = cpu.PercentProcessorTime,
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
