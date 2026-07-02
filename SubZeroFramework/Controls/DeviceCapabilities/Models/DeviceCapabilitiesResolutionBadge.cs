using System;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>Resolution standard chip per the mockup (4K+ / WQXGA / QHD / FHD); empty when no mode is reported.
/// Shared by the graphics-adapter and monitor cards so both tiles use the same tier ladder.</summary>
public static class DeviceCapabilitiesResolutionBadge
{
    public static string For(uint horizontalResolution, uint verticalResolution)
    {
        var maxDimension = Math.Max(horizontalResolution, verticalResolution);
        var minDimension = Math.Min(horizontalResolution, verticalResolution);
        if (maxDimension == 0)
        {
            return string.Empty;
        }

        if (maxDimension >= 3840 || minDimension >= 2160)
        {
            return "4K+";
        }

        if (maxDimension >= 2560 && minDimension >= 1600)
        {
            return "WQXGA";
        }

        if (maxDimension >= 2560 || minDimension >= 1440)
        {
            return "QHD";
        }

        if (maxDimension >= 1920 || minDimension >= 1080)
        {
            return "FHD";
        }

        return string.Empty;
    }
}
