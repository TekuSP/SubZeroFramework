using NUnit.Framework;

using SubZeroFramework.GrpcContracts;
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
        });
    }
}
