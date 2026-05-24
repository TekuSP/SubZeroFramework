using FrameworkDotnet.Enums;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

public sealed class FrameworkCoolingMetadataResolverTests
{
    [Test]
    public void Resolve_UsesFramework12MaximumSpeedAndMetadata()
    {
        var metadata = FrameworkCoolingMetadataResolver.Resolve(FrameworkPlatform.Framework12IntelGen13, FrameworkPlatformFamily.Framework12);
        var details = metadata.CoolingDetails as FrameworkLaptop12CoolingDetails;

        Assert.That(metadata.MaximumSpeedRpm, Is.EqualTo(6800));
        Assert.That(details, Is.Not.Null);
        Assert.That(details!.FirmwareOperatingRangeRpm.MinimumRpm, Is.EqualTo(1800));
        Assert.That(details.FirmwareOperatingRangeRpm.MaximumRpm, Is.EqualTo(6800));
    }

    [Test]
    public void Resolve_UsesFramework13PhysicalMaximumForFanCards()
    {
        var metadata = FrameworkCoolingMetadataResolver.Resolve(FrameworkPlatform.IntelGen11, FrameworkPlatformFamily.Framework13);
        var details = metadata.CoolingDetails as FrameworkLaptop13CoolingDetails;

        Assert.That(metadata.MaximumSpeedRpm, Is.EqualTo(7281));
        Assert.That(details, Is.Not.Null);
        Assert.That(details!.ProcessorSupport, Is.EqualTo("11th Gen Intel Core"));
        Assert.That(details.MaximumFirmwareLimitRpm, Is.EqualTo(6300));
        Assert.That(details.ApproximatePhysicalMaximumRpm, Is.EqualTo(metadata.MaximumSpeedRpm));
    }

    [Test]
    public void Resolve_UsesFramework16ThermalStressMaximumForFanCards()
    {
        var metadata = FrameworkCoolingMetadataResolver.Resolve(FrameworkPlatform.Framework16AmdAi300, FrameworkPlatformFamily.Framework16);
        var details = metadata.CoolingDetails as FrameworkLaptop16CoolingDetails;

        Assert.That(metadata.MaximumSpeedRpm, Is.EqualTo(5300));
        Assert.That(details, Is.Not.Null);
        Assert.That(details!.ProcessorSupport, Is.EqualTo("AMD Ryzen AI 300 Series"));
        Assert.That(details.StandardFirmwareMaximumRpm, Is.EqualTo(4900));
        Assert.That(details.ApproximateThermalStressMaximumRpm, Is.EqualTo(metadata.MaximumSpeedRpm));
    }

    [Test]
    public void Resolve_UsesDesktopMaximumSpeedAndVariantMetadata()
    {
        var metadata = FrameworkCoolingMetadataResolver.Resolve(FrameworkPlatform.FrameworkDesktopAmdAiMax300, FrameworkPlatformFamily.FrameworkDesktop);
        var details = metadata.CoolingDetails as FrameworkDesktopCoolingDetails;

        Assert.That(metadata.MaximumSpeedRpm, Is.EqualTo(2400));
        Assert.That(details, Is.Not.Null);
        Assert.That(details!.SupportedFanOptions.Any(item => item.ModelName.Contains("Noctua", StringComparison.Ordinal)), Is.True);
        Assert.That(details.SupportedFanOptions.All(item => item.MaximumFanSpeedRpm == metadata.MaximumSpeedRpm), Is.True);
        Assert.That(details.SupportedFanOptions.All(item => item.AcousticNoiseDecibels is > 0d), Is.True);
        Assert.That(details.SupportedFanOptions.Single(item => item.ModelName.Contains("Noctua", StringComparison.Ordinal)).MaximumAcousticNoiseDecibels, Is.Null);
        Assert.That(details.SupportedFanOptions.Where(item => item.ModelName.Contains("Mobius", StringComparison.Ordinal)).All(item => item.MaximumAcousticNoiseDecibels == 34d), Is.True);
    }

    [Test]
    public void Resolve_CanInferFamilyFromPlatformWhenFamilyIsMissing()
    {
        var metadata = FrameworkCoolingMetadataResolver.Resolve(FrameworkPlatform.Framework12IntelGen13, null);
        var details = metadata.CoolingDetails as FrameworkLaptop12CoolingDetails;

        Assert.That(metadata.MaximumSpeedRpm, Is.EqualTo(6800));
        Assert.That(details, Is.Not.Null);
        Assert.That(details!.MaximumPhysicalLimitRpm, Is.EqualTo(6800));
    }
}
