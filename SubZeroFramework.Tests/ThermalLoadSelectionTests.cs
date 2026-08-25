using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for how a machine settles on the power reading feed-forward runs from.
/// </summary>
/// <remarks>
/// Small surface, high stakes. The chosen figure is multiplied by a gain and added straight to the fan
/// command, and the choice also fixes what the online fit's coupling term MEANS. Getting it wrong shows up
/// either immediately as a fan at the wrong speed, or days later as a model quietly built from two
/// incompatible signals.
/// </remarks>
[TestFixture]
public class ThermalLoadSelectionTests
{
    [Test]
    public void CpuAndGpu_IsChosenWhenBothAreConsistentlyAvailable()
    {
        // Linux with RAPL and a working GPU reader: essentially every watt entering the chassis.
        var policy = Settle(cpu: 28d, gpu: 40d, system: 95d);

        Assert.Multiple(() =>
        {
            Assert.That(policy.Source, Is.EqualTo(ThermalLoadSource.CpuAndGpu));
            Assert.That(policy.Resolve(Sample(cpu: 28d, gpu: 40d, system: 95d)).Watts, Is.EqualTo(68d));
        });
    }

    [Test]
    public void SystemPower_IsNotAddedToComponentPower()
    {
        // THE arithmetic trap. Charger draw ALREADY contains the CPU and GPU, so summing them would roughly
        // double the anticipated load and send the fan to a speed nothing on the machine justifies.
        var policy = Settle(cpu: 28d, gpu: 40d, system: 95d);

        Assert.That(policy.Resolve(Sample(cpu: 28d, gpu: 40d, system: 95d)).Watts, Is.EqualTo(68d));
    }

    [Test]
    public void SystemPower_OutranksAGpuOnlyReading()
    {
        // The correction that motivated this whole design. On Windows there is no package power, but NVML and
        // ADLX do report GPU watts — and taking those alone would read a compiling machine as idle, because
        // the CPU is pulling 60 W while the GPU sits near zero. Coarse but complete beats precise but partial.
        var policy = Settle(cpu: null, gpu: 12d, system: 65d);

        Assert.Multiple(() =>
        {
            Assert.That(policy.Source, Is.EqualTo(ThermalLoadSource.System));
            Assert.That(policy.Resolve(Sample(cpu: null, gpu: 12d, system: 65d)).Watts, Is.EqualTo(65d));
        });
    }

    [Test]
    public void GpuOnly_IsUsedOnlyWhenNothingElseExists()
    {
        var policy = Settle(cpu: null, gpu: 40d, system: null);

        Assert.Multiple(() =>
        {
            Assert.That(policy.Source, Is.EqualTo(ThermalLoadSource.Gpu));
            Assert.That(policy.Resolve(Sample(cpu: null, gpu: 40d, system: null)).Watts, Is.EqualTo(40d));
        });
    }

    [Test]
    public void AnIntermittentGpu_IsExcludedFromTheComposition()
    {
        // The failure this policy exists to prevent. A discrete GPU reports power under load and drops out in
        // a low-power state. Choosing per tick would flip the composition several times a minute; worse, the
        // samples that survived would all be loaded ones, biasing the fit toward one end of the range.
        var policy = new ThermalLoadPolicy();

        for (var i = 0; i < ThermalLoadPolicy.CapabilityWindowSamples; i++)
        {
            // Reporting one tick in four — nowhere near the availability threshold.
            var gpu = i % 4 == 0 ? 40d : (double?)null;
            policy.Resolve(Sample(cpu: null, gpu: gpu, system: 65d));
        }

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsSettled, Is.True);
            Assert.That(policy.Source, Is.EqualTo(ThermalLoadSource.System), "A flapping GPU must not join the composition.");
        });
    }

    [Test]
    public void ASingleDroppedRead_DoesNotExcludeAnOtherwiseReliableSource()
    {
        // A driver reloading or one transient EC failure should not permanently change what the machine
        // learns from.
        var policy = new ThermalLoadPolicy();

        for (var i = 0; i < ThermalLoadPolicy.CapabilityWindowSamples; i++)
        {
            var gpu = i == 7 ? (double?)null : 40d;
            policy.Resolve(Sample(cpu: 28d, gpu: gpu, system: 95d));
        }

        Assert.That(policy.Source, Is.EqualTo(ThermalLoadSource.CpuAndGpu));
    }

    [Test]
    public void OnceSettled_TheCompositionNeverChanges()
    {
        // The core promise. Plugging in a charger mid-session must not swap the meaning of P underneath a fit
        // that was built without it.
        var policy = Settle(cpu: 28d, gpu: null, system: null);
        Assert.That(policy.Source, Is.EqualTo(ThermalLoadSource.Cpu), "Precondition.");

        for (var i = 0; i < 200; i++)
        {
            policy.Resolve(Sample(cpu: 28d, gpu: 40d, system: 95d));
        }

        Assert.That(policy.Source, Is.EqualTo(ThermalLoadSource.Cpu));
    }

    [Test]
    public void ASettledCompositionThatCannotBeFormed_RefusesRatherThanSubstituting()
    {
        // Refusing costs one sample. Substituting a different reading corrupts the fit, with no symptom for
        // days. An absent half is emphatically not zero watts.
        var policy = Settle(cpu: 28d, gpu: 40d, system: 95d);

        var resolved = policy.Resolve(Sample(cpu: 28d, gpu: null, system: 95d));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Watts, Is.Null);
            Assert.That(resolved.Source, Is.EqualTo(ThermalLoadSource.CpuAndGpu), "The composition stands; this sample simply cannot form it.");
        });
    }

    [Test]
    public void BeforeSettling_FeedForwardStillGetsAValue()
    {
        // Withholding feed-forward for the whole capability window would leave a fan reacting late for its
        // first minute after every start, for no benefit — only LEARNING needs a stable meaning.
        var policy = new ThermalLoadPolicy();

        var resolved = policy.Resolve(Sample(cpu: 28d, gpu: 40d, system: 95d));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Watts, Is.EqualTo(68d));
            Assert.That(resolved.IsSettled, Is.False, "But it must be flagged unsettled, so nothing learns from it.");
        });
    }

    [Test]
    public void AResumedPolicy_KeepsThePreviousCompositionWithoutReDeciding()
    {
        // Re-running the capability window on every restart could land somewhere different and silently
        // invalidate a fit that took days to build.
        var policy = new ThermalLoadPolicy(ThermalLoadSource.System);

        var resolved = policy.Resolve(Sample(cpu: 28d, gpu: 40d, system: 65d));

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsSettled, Is.True);
            Assert.That(resolved.Source, Is.EqualTo(ThermalLoadSource.System));
            Assert.That(resolved.Watts, Is.EqualTo(65d), "Component readings must not hijack a resumed composition.");
        });
    }

    [Test]
    public void NonsenseReadings_AreNeverComposed()
    {
        // A driver returning NaN or a negative watt figure must not reach a multiplication that ends at an EC
        // write.
        var policy = Settle(cpu: null, gpu: null, system: 55d);

        var resolved = policy.Resolve(new ControlTelemetrySample
        {
            CpuPackagePowerWatts = double.NaN,
            GpuPowerWatts = -5d,
            SystemPowerWatts = 55d,
        });

        Assert.That(resolved.Watts, Is.EqualTo(55d));
    }

    [Test]
    public void WithNoReadingsAtAll_ReportsNone()
    {
        // Must stay distinguishable from zero watts: zero would teach the model that this machine runs its fan
        // for no reason, where None correctly leaves the loop on feedback alone.
        var policy = Settle(cpu: null, gpu: null, system: null);

        var resolved = policy.Resolve(Sample(null, null, null));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Watts, Is.Null);
            Assert.That(resolved.Source, Is.EqualTo(ThermalLoadSource.None));
        });
    }

    [Test]
    public void HasAnyReading_CountsTheNewPowerSources()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ControlTelemetrySample { SystemPowerWatts = 40d }.HasAnyReading, Is.True);
            Assert.That(new ControlTelemetrySample { GpuPowerWatts = 40d }.HasAnyReading, Is.True);
            Assert.That(ControlTelemetrySample.Unavailable.HasAnyReading, Is.False);
        });
    }

    /// <summary>Runs a full capability window of identical samples so the policy fixes its composition.</summary>
    private static ThermalLoadPolicy Settle(double? cpu, double? gpu, double? system)
    {
        var policy = new ThermalLoadPolicy();

        for (var i = 0; i < ThermalLoadPolicy.CapabilityWindowSamples; i++)
        {
            policy.Resolve(Sample(cpu, gpu, system));
        }

        return policy;
    }

    private static ControlTelemetrySample Sample(double? cpu, double? gpu, double? system)
        => new()
        {
            CpuPackagePowerWatts = cpu,
            GpuPowerWatts = gpu,
            SystemPowerWatts = system,
        };
}
