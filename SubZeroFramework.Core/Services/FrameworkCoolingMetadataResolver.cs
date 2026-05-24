using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

public static class FrameworkCoolingMetadataResolver
{
    private const int DefaultMaximumFanSpeedRpm = 7500;

    public static FrameworkCoolingMetadata Resolve(FrameworkPlatform? platform, FrameworkPlatformFamily? platformFamily)
    {
        var resolvedPlatformFamily = platformFamily ?? platform switch
        {
            FrameworkPlatform.Framework12IntelGen13 => FrameworkPlatformFamily.Framework12,
            FrameworkPlatform.IntelGen11
                or FrameworkPlatform.IntelGen12
                or FrameworkPlatform.IntelGen13
                or FrameworkPlatform.IntelCoreUltra1
                or FrameworkPlatform.IntelCoreUltra3
                or FrameworkPlatform.Framework13Amd7080
                or FrameworkPlatform.Framework13AmdAi300 => FrameworkPlatformFamily.Framework13,
            FrameworkPlatform.Framework16Amd7080
                or FrameworkPlatform.Framework16AmdAi300 => FrameworkPlatformFamily.Framework16,
            FrameworkPlatform.FrameworkDesktopAmdAiMax300 => FrameworkPlatformFamily.FrameworkDesktop,
            _ => null,
        };

        return resolvedPlatformFamily switch
        {
            FrameworkPlatformFamily.Framework12 => CreateFramework12Metadata(),
            FrameworkPlatformFamily.Framework13 => CreateFramework13Metadata(platform),
            FrameworkPlatformFamily.Framework16 => CreateFramework16Metadata(platform),
            FrameworkPlatformFamily.FrameworkDesktop => CreateFrameworkDesktopMetadata(platform),
            _ => new FrameworkCoolingMetadata
            {
                MaximumSpeedRpm = DefaultMaximumFanSpeedRpm,
                CoolingDetails = null,
            },
        };
    }

    private static FrameworkCoolingMetadata CreateFramework12Metadata()
    {
        var details = new FrameworkLaptop12CoolingDetails
        {
            ProcessorSupport = "13th Gen Intel Core (i3, i5)",
            ThermalCapacity = "28W continuous / 60W turbo peak",
            HeatPipeConfiguration = "Dual 5 mm or single 8 mm variant",
            FanDimensions = CreateCircularFanDimensions(65d, 5.5d),
            ThermalInterfaceMaterial = "Shin-Etsu 8117 / Honeywell PTM7958",
            FirmwareOperatingRangeRpm = new FanSpeedRange
            {
                MinimumRpm = 1800,
                MaximumRpm = 6800,
            },
            MaximumPhysicalLimitRpm = 6800,
        };

        return new FrameworkCoolingMetadata
        {
            MaximumSpeedRpm = details.MaximumPhysicalLimitRpm,
            CoolingDetails = details,
        };
    }

    private static FrameworkCoolingMetadata CreateFramework13Metadata(FrameworkPlatform? platform)
    {
        var details = new FrameworkLaptop13CoolingDetails
        {
            ProcessorSupport = platform switch
            {
                FrameworkPlatform.IntelGen11 => "11th Gen Intel Core",
                FrameworkPlatform.IntelGen12 => "12th Gen Intel Core",
                FrameworkPlatform.IntelGen13 => "13th Gen Intel Core",
                FrameworkPlatform.IntelCoreUltra1 => "Intel Core Ultra Series 1",
                FrameworkPlatform.IntelCoreUltra3 => "Intel Core Ultra Series 3",
                FrameworkPlatform.Framework13Amd7080 => "AMD Ryzen 7040 Series",
                FrameworkPlatform.Framework13AmdAi300 => "AMD Ryzen AI 300 Series",
                _ => "Framework Laptop 13 platform",
            },
            ChassisMaterial = "Aluminum alloy",
            ApproximateFirmwareIdleSpeedRpm = 3000,
            ApproximateUserTunedIdleSpeedRpm = 1600,
            MaximumFirmwareLimitRpm = 6300,
            ApproximatePhysicalMaximumRpm = 7281,
        };

        return new FrameworkCoolingMetadata
        {
            MaximumSpeedRpm = details.ApproximatePhysicalMaximumRpm,
            CoolingDetails = details,
        };
    }

    private static FrameworkCoolingMetadata CreateFramework16Metadata(FrameworkPlatform? platform)
    {
        var details = new FrameworkLaptop16CoolingDetails
        {
            ProcessorSupport = platform switch
            {
                FrameworkPlatform.Framework16Amd7080 => "AMD Ryzen 7040 Series",
                FrameworkPlatform.Framework16AmdAi300 => "AMD Ryzen AI 300 Series",
                _ => "Framework Laptop 16 platform",
            },
            PrimaryCpuThermalInterfaceMaterial = "Liquid metal (early) / Honeywell PTM7958 (revised)",
            ShellFanDimensions = CreateRectangularFanDimensions(75d, 75d, 8.2d),
            GraphicsFanDimensions = CreateRectangularFanDimensions(75d, 75d, 11.5d),
            ExpansionBayPowerLimitWatts = 9d,
            StandardFirmwareMaximumRpm = 4900,
            ApproximateThermalStressMaximumRpm = 5300,
        };

        return new FrameworkCoolingMetadata
        {
            MaximumSpeedRpm = details.ApproximateThermalStressMaximumRpm,
            CoolingDetails = details,
        };
    }

    private static FrameworkCoolingMetadata CreateFrameworkDesktopMetadata(FrameworkPlatform? platform)
    {
        var details = new FrameworkDesktopCoolingDetails
        {
            Platform = platform switch
            {
                FrameworkPlatform.FrameworkDesktopAmdAiMax300 => "AMD AI Max 300",
                _ => "Framework Desktop",
            },
            SupportedFanOptions =
            [
                new FrameworkDesktopFanOption
                {
                    ModelName = "CM Mobius 120",
                    FanDimensions = CreateRectangularFanDimensions(120d, 120d, 25d),
                    ConnectorType = "4-pin PWM",
                    MaximumAirflowCfm = 75.2d,
                    AcousticNoiseDisplay = "30 dBA (max 34 dBA)",
                    AcousticNoiseDecibels = 30d,
                    MaximumAcousticNoiseDecibels = 34d,
                    MaximumFanSpeedRpm = 2400,
                },
                new FrameworkDesktopFanOption
                {
                    ModelName = "CM Mobius 120P ARGB",
                    FanDimensions = CreateRectangularFanDimensions(120d, 120d, 25d),
                    ConnectorType = "4-pin PWM + 3-pin ARGB",
                    MaximumAirflowCfm = 75.2d,
                    AcousticNoiseDisplay = "30 dBA (max 34 dBA)",
                    AcousticNoiseDecibels = 30d,
                    MaximumAcousticNoiseDecibels = 34d,
                    MaximumFanSpeedRpm = 2400,
                },
                new FrameworkDesktopFanOption
                {
                    ModelName = "Noctua NF-A12x25 HS-PWM",
                    FanDimensions = CreateRectangularFanDimensions(120d, 120d, 25d),
                    ConnectorType = "4-pin PWM",
                    MaximumAirflowCfm = 69.25d,
                    AlternateAirflowDisplay = "117.6 m³/h",
                    AcousticNoiseDisplay = "28.8 dBA",
                    AcousticNoiseDecibels = 28.8d,
                    MaximumFanSpeedRpm = 2400,
                },
            ],
        };

        return new FrameworkCoolingMetadata
        {
            MaximumSpeedRpm = details.SupportedFanOptions.Max(option => option.MaximumFanSpeedRpm),
            CoolingDetails = details,
        };
    }

    private static CoolingFanDimensions CreateCircularFanDimensions(double diameterMillimeters, double thicknessMillimeters)
    {
        return new CoolingFanDimensions
        {
            WidthMillimeters = diameterMillimeters,
            HeightMillimeters = diameterMillimeters,
            ThicknessMillimeters = thicknessMillimeters,
            IsCircular = true,
        };
    }

    private static CoolingFanDimensions CreateRectangularFanDimensions(double widthMillimeters, double heightMillimeters, double thicknessMillimeters)
    {
        return new CoolingFanDimensions
        {
            WidthMillimeters = widthMillimeters,
            HeightMillimeters = heightMillimeters,
            ThicknessMillimeters = thicknessMillimeters,
            IsCircular = false,
        };
    }
}
