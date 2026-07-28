using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Services.Compute;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the Intel PMU discovery: the parsing of what the kernel advertises, and the refusal to invent a
/// number when the counters are not readable.
/// </summary>
/// <remarks>
/// The values below are the verbatim formats the i915 and xe PMUs expose in sysfs. Getting them wrong is the
/// expensive failure mode for this feature, because a mis-parsed config still opens a counter — just the
/// wrong one — and reports a plausible percentage for something else entirely.
/// </remarks>
[TestFixture]
public class LinuxIntelGpuUtilizationReaderTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "szf-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void ParsesI915EventConfigs()
    {
        // i915 writes "config=0x%llx". rcs0 is legitimately config 0, so a zero result must not read as
        // "not found" — hence the nullable return rather than a 0 sentinel.
        Assert.Multiple(() =>
        {
            Assert.That(LinuxPerfEvent.ParseEventConfig("config=0x0\n"), Is.EqualTo(0UL), "rcs0-busy really is config 0");
            Assert.That(LinuxPerfEvent.ParseEventConfig("config=0x1000\n"), Is.EqualTo(0x1000UL), "bcs0-busy");
            Assert.That(LinuxPerfEvent.ParseEventConfig("config=0x2000\n"), Is.EqualTo(0x2000UL), "vcs0-busy");
            Assert.That(LinuxPerfEvent.ParseEventConfig("config=0x2010\n"), Is.EqualTo(0x2010UL), "vcs1-busy");
            Assert.That(LinuxPerfEvent.ParseEventConfig("config=0x3000\n"), Is.EqualTo(0x3000UL), "vecs0-busy");
            Assert.That(LinuxPerfEvent.ParseEventConfig("config=0x1000000000100003\n"), Is.EqualTo(0x1000000000100003UL), "rc6-residency-gt1, 64-bit");
        });
    }

    [Test]
    public void ParsesXeEventConfigs_WhichUseADifferentKey()
    {
        // xe writes "event=%#04llx" where i915 writes "config=". A parser that only knew i915's key would
        // silently find nothing on every Lunar Lake and Panther Lake machine.
        Assert.Multiple(() =>
        {
            Assert.That(LinuxPerfEvent.ParseEventConfig("event=0x02\n"), Is.EqualTo(0x02UL), "engine-active-ticks");
            Assert.That(LinuxPerfEvent.ParseEventConfig("event=0x03\n"), Is.EqualTo(0x03UL), "engine-total-ticks");
            Assert.That(LinuxPerfEvent.ParseEventConfig("event=0x01\n"), Is.EqualTo(0x01UL), "gt-c6-residency");
        });
    }

    [Test]
    public void RejectsUnparseableEventEntries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LinuxPerfEvent.ParseEventConfig(null), Is.Null);
            Assert.That(LinuxPerfEvent.ParseEventConfig(string.Empty), Is.Null);
            Assert.That(LinuxPerfEvent.ParseEventConfig("ns\n"), Is.Null, "a .unit file, not an event");
            Assert.That(LinuxPerfEvent.ParseEventConfig("something=else\n"), Is.Null);
        });
    }

    [Test]
    public void ParsesFormatShifts_TakingTheLowBitOfTheRange()
    {
        // xe's config layout is discovered from these rather than hardcoded, because it differs from i915's
        // entirely: xe packs event[11:0], instance[19:12], class[27:20], gt[63:60].
        Assert.Multiple(() =>
        {
            Assert.That(LinuxPerfEvent.ParseFormatShift("config:0-11\n"), Is.EqualTo(0), "event id");
            Assert.That(LinuxPerfEvent.ParseFormatShift("config:12-19\n"), Is.EqualTo(12), "engine instance");
            Assert.That(LinuxPerfEvent.ParseFormatShift("config:20-27\n"), Is.EqualTo(20), "engine class");
            Assert.That(LinuxPerfEvent.ParseFormatShift("config:60-63\n"), Is.EqualTo(60), "gt");
            Assert.That(LinuxPerfEvent.ParseFormatShift("config:0-20\n"), Is.EqualTo(0), "i915's single format entry");
            Assert.That(LinuxPerfEvent.ParseFormatShift("nonsense"), Is.Null);
            Assert.That(LinuxPerfEvent.ParseFormatShift(null), Is.Null);
        });
    }

    [Test]
    public void ParsesCpuMasks_AndAlwaysOffersAFallback()
    {
        Directory.CreateDirectory(Path.Combine(_root, "i915"));
        File.WriteAllText(Path.Combine(_root, "i915", "cpumask"), "0\n");
        Assert.That(LinuxPerfEvent.ReadCandidateCpus(Path.Combine(_root, "i915")), Does.Contain(0));

        File.WriteAllText(Path.Combine(_root, "i915", "cpumask"), "4-7\n");
        var range = LinuxPerfEvent.ReadCandidateCpus(Path.Combine(_root, "i915"));
        Assert.Multiple(() =>
        {
            Assert.That(range[0], Is.EqualTo(4), "the PMU's own mask comes first");
            Assert.That(range, Does.Contain(0), "CPU 0 stays as a fallback for kernels that do not enforce the mask");
        });

        // A PMU directory with no cpumask at all must still yield something to try.
        Directory.CreateDirectory(Path.Combine(_root, "bare"));
        Assert.That(LinuxPerfEvent.ReadCandidateCpus(Path.Combine(_root, "bare")), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void FindsBothIntegratedAndDiscretePmuNames()
    {
        // An integrated GPU registers as bare "i915"; a discrete one appends its bus address. Both must be
        // found, and the xe PMU (which ALWAYS carries an address) alongside them.
        Directory.CreateDirectory(Path.Combine(_root, "i915"));
        Directory.CreateDirectory(Path.Combine(_root, "i915_0000_03_00.0"));
        Directory.CreateDirectory(Path.Combine(_root, "xe_0000_00_02.0"));
        Directory.CreateDirectory(Path.Combine(_root, "cpu"));
        Directory.CreateDirectory(Path.Combine(_root, "msr"));

        Assert.Multiple(() =>
        {
            Assert.That(LinuxPerfEvent.FindPmuDirectories("i915", _root), Has.Count.EqualTo(2));
            Assert.That(LinuxPerfEvent.FindPmuDirectories("xe_", _root), Has.Count.EqualTo(1));
            Assert.That(LinuxPerfEvent.FindPmuDirectories("nothing", _root), Is.Empty);
        });
    }

    [Test]
    public void PmuThatCannotBeOpened_ReportsNothingRatherThanGuessing()
    {
        // A fully-formed i915 PMU description whose counters cannot actually be opened — which is what a
        // container with the default seccomp policy looks like, and what this fixture is on any machine.
        // The contract is silence, not a fabricated reading.
        var pmu = Path.Combine(_root, "i915");
        Directory.CreateDirectory(Path.Combine(pmu, "events"));
        File.WriteAllText(Path.Combine(pmu, "type"), "13\n");
        File.WriteAllText(Path.Combine(pmu, "cpumask"), "0\n");
        File.WriteAllText(Path.Combine(pmu, "events", "rcs0-busy"), "config=0x0\n");
        File.WriteAllText(Path.Combine(pmu, "events", "rcs0-busy.unit"), "ns\n");
        File.WriteAllText(Path.Combine(pmu, "events", "vcs0-busy"), "config=0x2000\n");

        var reader = new LinuxIntelGpuUtilizationReader(
            NullLogger<LinuxIntelGpuUtilizationReader>.Instance,
            sysfsRoot: Path.Combine(_root, "sys"),
            eventSourceRoot: _root);

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsAvailable, Is.False);
            Assert.That(reader.Sample(), Is.Empty);
        });
    }

    [Test]
    public void NoPmuAtAll_IsSilent()
    {
        var reader = new LinuxIntelGpuUtilizationReader(
            NullLogger<LinuxIntelGpuUtilizationReader>.Instance,
            sysfsRoot: Path.Combine(_root, "sys"),
            eventSourceRoot: Path.Combine(_root, "missing"));

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsAvailable, Is.False);
            Assert.That(reader.Sample(), Is.Empty);
            Assert.DoesNotThrow(reader.Dispose);
        });
    }

    [Test]
    public void RealSystemPerfInterface_IsProbedWithoutThrowing()
    {
        // Against this machine's actual /sys/bus/event_source: an Intel laptop with a live PMU, a Framework
        // 16 with none, a CI container where perf_event_open is blocked, or Windows where libc is absent.
        var reader = new LinuxIntelGpuUtilizationReader(NullLogger<LinuxIntelGpuUtilizationReader>.Instance);

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => _ = reader.IsAvailable);
            Assert.DoesNotThrow(() => reader.Sample());
            Assert.DoesNotThrow(reader.Dispose);
        });
    }
}
