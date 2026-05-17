using System;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoCpuCore(
    string? Name,
    double PercentProcessorTime)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? "Core"
        : Name.Contains("core", StringComparison.OrdinalIgnoreCase)
            ? Name
            : $"Core {Name}";

    public string DisplayLoad => $"{PercentProcessorTime:0.##} %";
}