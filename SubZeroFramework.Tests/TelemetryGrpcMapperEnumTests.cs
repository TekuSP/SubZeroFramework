using NUnit.Framework;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.GrpcContracts.Mapping;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the telemetry enum wire mapping. Two invariants: every model value must survive a roundtrip (a value
/// added on one side but not the other is how compute telemetry silently vanished in the field), and an
/// UNKNOWN wire value must be rejected — the old "default and return true" shape parsed unknowns into
/// (Thermal, TemperatureSensor, TemperatureCelsius), a valid identity that collided with a real sensor's
/// channel and injected foreign readings into the thermal cache.
/// </summary>
[TestFixture]
public class TelemetryGrpcMapperEnumTests
{
    [Test]
    public void EveryTelemetryArea_RoundTripsThroughTheWireEnum()
    {
        foreach (var area in Enum.GetValues<TelemetryArea>())
        {
            var wire = TelemetryGrpcMapper.MapChannelId(new TelemetryChannelId(area, TelemetryEntityKind.Fan, 0, TelemetryMetric.FanSpeedRpm)).Area;

            Assert.Multiple(() =>
            {
                Assert.That(wire, Is.Not.EqualTo(TelemetryAreaValue.Unspecified), $"{area} has no wire value — it would be dropped by every client.");
                Assert.That(TelemetryGrpcMapper.TryParseTelemetryArea(wire, out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(area));
            });
        }
    }

    [Test]
    public void EveryTelemetryEntityKind_RoundTripsThroughTheWireEnum()
    {
        foreach (var entityKind in Enum.GetValues<TelemetryEntityKind>())
        {
            var wire = TelemetryGrpcMapper.MapChannelId(new TelemetryChannelId(TelemetryArea.Compute, entityKind, 0, TelemetryMetric.UtilizationPercent)).EntityKind;

            Assert.Multiple(() =>
            {
                Assert.That(wire, Is.Not.EqualTo(TelemetryEntityKindValue.Unspecified), $"{entityKind} has no wire value — it would be dropped by every client.");
                Assert.That(TelemetryGrpcMapper.TryParseTelemetryEntityKind(wire, out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(entityKind));
            });
        }
    }

    [Test]
    public void EveryTelemetryMetric_RoundTripsThroughTheWireEnum()
    {
        foreach (var metric in Enum.GetValues<TelemetryMetric>())
        {
            var wire = TelemetryGrpcMapper.MapChannelId(new TelemetryChannelId(TelemetryArea.Compute, TelemetryEntityKind.Gpu, 0, metric)).Metric;

            Assert.Multiple(() =>
            {
                Assert.That(wire, Is.Not.EqualTo(TelemetryMetricValue.Unspecified), $"{metric} has no wire value — it would be dropped by every client.");
                Assert.That(TelemetryGrpcMapper.TryParseTelemetryMetric(wire, out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(metric));
            });
        }
    }

    [Test]
    public void EveryComputeDeviceKind_RoundTripsThroughTheWireEnum()
    {
        foreach (var kind in Enum.GetValues<ComputeDeviceKind>())
        {
            var wire = TelemetryWireMapper.MapComputeDeviceKind(kind);

            Assert.Multiple(() =>
            {
                Assert.That(wire, Is.Not.EqualTo(ComputeDeviceKindValue.Unspecified), $"{kind} has no wire value — the accelerator would be dropped by every client.");
                Assert.That(TelemetryWireMapper.TryParseComputeDeviceKind(wire, out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(kind));
            });
        }
    }

    [Test]
    public void UnknownWireValues_AreRejected_NeverGuessedAsTheFirstMember()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TelemetryGrpcMapper.TryParseTelemetryArea((TelemetryAreaValue)999, out _), Is.False);
            Assert.That(TelemetryGrpcMapper.TryParseTelemetryEntityKind((TelemetryEntityKindValue)999, out _), Is.False);
            Assert.That(TelemetryGrpcMapper.TryParseTelemetryMetric((TelemetryMetricValue)999, out _), Is.False);
            Assert.That(TelemetryGrpcMapper.TryParseTelemetryArea(TelemetryAreaValue.Unspecified, out _), Is.False);
            Assert.That(TelemetryGrpcMapper.TryParseTelemetryEntityKind(TelemetryEntityKindValue.Unspecified, out _), Is.False);
            Assert.That(TelemetryGrpcMapper.TryParseTelemetryMetric(TelemetryMetricValue.Unspecified, out _), Is.False);
            Assert.That(TelemetryWireMapper.TryParseComputeDeviceKind((ComputeDeviceKindValue)999, out _), Is.False);
            Assert.That(TelemetryWireMapper.TryParseComputeDeviceKind(ComputeDeviceKindValue.Unspecified, out _), Is.False);
        });
    }

    /// <summary>
    /// The service-side entry points must be the shared mapper, not a second implementation.
    /// </summary>
    /// <remarks>
    /// The bug this whole fixture exists for was two copies of the mapping drifting apart. The copies are now
    /// forwarders onto <see cref="TelemetryWireMapper"/>; this asserts they agree for every value, so a future
    /// "quick fix" that re-inlines one of them fails here rather than in the field.
    /// </remarks>
    [Test]
    public void ServiceMapperAndSharedMapper_AgreeOnEveryValue()
    {
        Assert.Multiple(() =>
        {
            foreach (var area in Enum.GetValues<TelemetryArea>())
            {
                var channelId = new TelemetryChannelId(area, TelemetryEntityKind.Fan, 0, TelemetryMetric.FanSpeedRpm);
                Assert.That(TelemetryGrpcMapper.MapChannelId(channelId).Area, Is.EqualTo(TelemetryWireMapper.MapTelemetryArea(area)));
            }

            foreach (var entityKind in Enum.GetValues<TelemetryEntityKind>())
            {
                var channelId = new TelemetryChannelId(TelemetryArea.Compute, entityKind, 0, TelemetryMetric.UtilizationPercent);
                Assert.That(TelemetryGrpcMapper.MapChannelId(channelId).EntityKind, Is.EqualTo(TelemetryWireMapper.MapTelemetryEntityKind(entityKind)));
            }

            foreach (var metric in Enum.GetValues<TelemetryMetric>())
            {
                var channelId = new TelemetryChannelId(TelemetryArea.Compute, TelemetryEntityKind.Gpu, 0, metric);
                Assert.That(TelemetryGrpcMapper.MapChannelId(channelId).Metric, Is.EqualTo(TelemetryWireMapper.MapTelemetryMetric(metric)));
            }
        });
    }
}
