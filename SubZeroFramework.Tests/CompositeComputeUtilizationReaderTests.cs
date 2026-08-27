using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers how the composite combines readers that know different things about the same device.
/// </summary>
/// <remarks>
/// This matters on a Framework 16 with the NVIDIA graphics module: the Windows PDH counter set reports
/// utilisation for every adapter, while NVML reports power, temperature and throttle reasons for the NVIDIA
/// one. Neither is a superset of the other, so the composite has to combine them rather than pick a winner.
/// </remarks>
[TestFixture]
public class CompositeComputeUtilizationReaderTests
{
    private const string SharedKey = "PCI\\VEN_10DE&DEV_2D58";

    private static ComputeDeviceUtilization Device(
        string deviceKey,
        double utilizationPercent,
        double? powerWatts = null,
        double? temperatureCelsius = null,
        ComputeThrottleReasons? throttleReasons = null) => new()
        {
            DeviceKey = deviceKey,
            Kind = ComputeDeviceKind.Gpu,
            DisplayName = "NVIDIA GeForce RTX 5070 Laptop GPU",
            UtilizationPercent = utilizationPercent,
            PowerWatts = powerWatts,
            TemperatureCelsius = temperatureCelsius,
            ThrottleReasons = throttleReasons,
        };

    private static CompositeComputeUtilizationReader Create(params IReadOnlyList<ComputeDeviceUtilization>[] batches)
        => new(
            batches.Select(batch => (IComputeUtilizationReader)new StubReader(batch)),
            NullLogger<CompositeComputeUtilizationReader>.Instance);

    [Test]
    public void Sample_CombinesWhatEachReaderKnowsAboutOneDevice()
    {
        // The real Windows shape: PDH answers utilisation, NVML answers everything else.
        using var composite = Create(
            [Device(SharedKey, utilizationPercent: 63d)],
            [Device(SharedKey, utilizationPercent: 0d, powerWatts: 29.3d, temperatureCelsius: 54d, throttleReasons: ComputeThrottleReasons.PowerLimit)]);

        var devices = composite.Sample();

        Assert.That(devices, Has.Count.EqualTo(1), "One physical GPU must not be published twice.");
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].UtilizationPercent, Is.EqualTo(63d), "The first source keeps its reading.");
            Assert.That(devices[0].PowerWatts, Is.EqualTo(29.3d));
            Assert.That(devices[0].TemperatureCelsius, Is.EqualTo(54d));
            Assert.That(devices[0].ThrottleReasons, Is.EqualTo(ComputeThrottleReasons.PowerLimit));
        });
    }

    [Test]
    public void Sample_NeverOverwritesAReadingThatWasAlreadyAnswered()
    {
        using var composite = Create(
            [Device(SharedKey, utilizationPercent: 63d, powerWatts: 29.3d)],
            [Device(SharedKey, utilizationPercent: 0d, powerWatts: 5d)]);

        var devices = composite.Sample();

        // Only null fields are filled. A second opinion must not silently replace a real measurement.
        Assert.That(devices[0].PowerWatts, Is.EqualTo(29.3d));
    }

    [Test]
    public void Sample_KeepsDistinctDevicesSeparate()
    {
        using var composite = Create(
            [Device("amd-igpu", utilizationPercent: 12d), Device(SharedKey, utilizationPercent: 63d)]);

        var devices = composite.Sample();

        Assert.That(devices, Has.Count.EqualTo(2));
    }

    [Test]
    public void Sample_KeepsReportingWhenOneReaderThrows()
    {
        using var composite = new CompositeComputeUtilizationReader(
            [
                new ThrowingReader(),
                new StubReader([Device(SharedKey, utilizationPercent: 63d)]),
            ],
            NullLogger<CompositeComputeUtilizationReader>.Instance);

        var devices = composite.Sample();

        // One vendor's broken driver must not blank out the whole page.
        Assert.That(devices, Has.Count.EqualTo(1));
    }

    [Test]
    public void Sample_EnrichesEvenWhenTheFirstSourceSawTheDeviceAsIdle()
    {
        // NVML intermittently fails to report utilisation on a laptop dGPU changing power state (measured:
        // NVML_ERROR_UNKNOWN on roughly every third call). The extended fields it DID answer must still land.
        using var composite = Create(
            [Device(SharedKey, utilizationPercent: 0d)],
            [Device(SharedKey, utilizationPercent: 0d, powerWatts: 19.2d, temperatureCelsius: 55d)]);

        var devices = composite.Sample();

        Assert.Multiple(() =>
        {
            Assert.That(devices[0].PowerWatts, Is.EqualTo(19.2d));
            Assert.That(devices[0].HasExtendedTelemetry, Is.True);
        });
    }

    private sealed class StubReader(IReadOnlyList<ComputeDeviceUtilization> devices) : IComputeUtilizationReader
    {
        public bool IsAvailable => true;

        public IReadOnlyList<ComputeDeviceUtilization> Sample() => devices;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingReader : IComputeUtilizationReader
    {
        public bool IsAvailable => true;

        public IReadOnlyList<ComputeDeviceUtilization> Sample() => throw new InvalidOperationException("driver fell over");

        public void Dispose()
        {
        }
    }
}
