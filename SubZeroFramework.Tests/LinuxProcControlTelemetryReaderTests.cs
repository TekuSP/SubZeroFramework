using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the Linux control-telemetry reader against a synthetic <c>/proc</c> and <c>/sys</c> tree, so the
/// counter arithmetic is tested on every platform rather than only where the real files exist.
/// </summary>
[TestFixture]
public class LinuxProcControlTelemetryReaderTests
{
    private string _root = string.Empty;
    private long _timestamp;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"szf-control-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _timestamp = 0;
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
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private string ProcRoot => Path.Combine(_root, "proc");

    private string SysRoot => Path.Combine(_root, "sys");

    private LinuxProcControlTelemetryReader CreateReader()
        => new(
            NullLogger<LinuxProcControlTelemetryReader>.Instance,
            procRoot: ProcRoot,
            sysRoot: SysRoot,
            timestampProvider: () => _timestamp);

    /// <summary>Advances the injected clock so an energy delta has a known window to divide by.</summary>
    private void AdvanceClock(double seconds) => _timestamp += (long)(seconds * System.Diagnostics.Stopwatch.Frequency);

    private void WriteProcStat(long user, long idle, params (long User, long Idle)[] cores)
    {
        Directory.CreateDirectory(ProcRoot);

        var lines = new List<string>
        {
            // label user nice system idle iowait irq softirq steal
            $"cpu  {user} 0 0 {idle} 0 0 0 0 0 0",
        };

        for (var index = 0; index < cores.Length; index++)
        {
            lines.Add($"cpu{index} {cores[index].User} 0 0 {cores[index].Idle} 0 0 0 0 0 0");
        }

        lines.Add("intr 12345 0 0");
        lines.Add("ctxt 987654");

        File.WriteAllLines(Path.Combine(ProcRoot, "stat"), lines);
    }

    private void WriteCpuFrequency(string attribute, long kilohertz, long currentKilohertz)
    {
        var cpuFreq = Path.Combine(SysRoot, "devices", "system", "cpu", "cpu0", "cpufreq");
        Directory.CreateDirectory(cpuFreq);
        File.WriteAllText(Path.Combine(cpuFreq, attribute), $"{kilohertz}\n");
        File.WriteAllText(Path.Combine(cpuFreq, "scaling_cur_freq"), $"{currentKilohertz}\n");
    }

    [Test]
    public void IsAvailable_IsFalseWithoutProcStat()
    {
        using var reader = CreateReader();
        Assert.That(reader.IsAvailable, Is.False);
    }

    [Test]
    public void IsAvailable_IsTrueOnceProcStatExists()
    {
        WriteProcStat(user: 100, idle: 900);

        using var reader = CreateReader();
        Assert.That(reader.IsAvailable, Is.True);
    }

    [Test]
    public void Sample_ReportsNoUtilizationOnTheFirstTick()
    {
        WriteProcStat(user: 100, idle: 900);

        using var reader = CreateReader();
        var sample = reader.Sample();

        // Nothing to difference against yet. Reporting 0% here would look exactly like an idle machine.
        Assert.That(sample.CpuUtilizationFraction, Is.Null);
    }

    [Test]
    public void Sample_ComputesUtilizationFromTheCounterDelta()
    {
        WriteProcStat(user: 100, idle: 900);
        using var reader = CreateReader();
        reader.Sample();

        // 75 busy jiffies against 25 idle over the window — 75% busy, regardless of the absolute totals.
        WriteProcStat(user: 175, idle: 925);
        var sample = reader.Sample();

        Assert.That(sample.CpuUtilizationFraction, Is.EqualTo(0.75d).Within(1e-9));
    }

    [Test]
    public void Sample_ReportsPerCoreUtilizationIndependently()
    {
        WriteProcStat(user: 0, idle: 0, cores: [(0, 0), (0, 0)]);
        using var reader = CreateReader();
        reader.Sample();

        // One core pinned, one core idle — the point of per-core data is that these do not average away.
        WriteProcStat(user: 100, idle: 100, cores: [(100, 0), (0, 100)]);
        var sample = reader.Sample();

        Assert.Multiple(() =>
        {
            Assert.That(sample.PerCoreUtilizationFraction, Has.Length.EqualTo(2));
            Assert.That(sample.PerCoreUtilizationFraction[0], Is.EqualTo(1d).Within(1e-9));
            Assert.That(sample.PerCoreUtilizationFraction[1], Is.EqualTo(0d).Within(1e-9));
        });
    }

    [Test]
    public void Sample_DropsPerCoreDataWhenTheCoreCountChanges()
    {
        WriteProcStat(user: 0, idle: 0, cores: [(0, 0), (0, 0)]);
        using var reader = CreateReader();
        reader.Sample();

        WriteProcStat(user: 100, idle: 100, cores: [(100, 0)]);
        var sample = reader.Sample();

        // Index-to-index differencing across a changed core count would report nonsense for every core.
        Assert.That(sample.PerCoreUtilizationFraction, Is.Empty);
    }

    [Test]
    public void Sample_ReportsNothingWhenTheCountersDoNotAdvance()
    {
        WriteProcStat(user: 100, idle: 900);
        using var reader = CreateReader();
        reader.Sample();

        var sample = reader.Sample();

        // A zero-width window carries no information — which is not the same as 0% busy.
        Assert.That(sample.CpuUtilizationFraction, Is.Null);
    }

    [Test]
    public void Sample_UsesBaseFrequencyForThePerformanceRatio()
    {
        WriteProcStat(user: 100, idle: 900);
        WriteCpuFrequency("base_frequency", kilohertz: 2_000_000, currentKilohertz: 1_000_000);

        using var reader = CreateReader();
        var sample = reader.Sample();

        Assert.That(sample.CpuPerformanceRatio, Is.EqualTo(0.5d).Within(1e-9));
    }

    [Test]
    public void Sample_ReportsRatiosAboveOneForTurbo()
    {
        WriteProcStat(user: 100, idle: 900);
        WriteCpuFrequency("base_frequency", kilohertz: 2_000_000, currentKilohertz: 3_000_000);

        using var reader = CreateReader();
        var sample = reader.Sample();

        // Clamping this to 1 would erase the difference between "at rated speed" and "boosting hard".
        Assert.That(sample.CpuPerformanceRatio, Is.EqualTo(1.5d).Within(1e-9));
    }

    [Test]
    public void Sample_FallsBackToMaxFrequencyWhenBaseFrequencyIsAbsent()
    {
        WriteProcStat(user: 100, idle: 900);
        WriteCpuFrequency("cpuinfo_max_freq", kilohertz: 4_000_000, currentKilohertz: 2_000_000);

        using var reader = CreateReader();
        var sample = reader.Sample();

        Assert.That(sample.CpuPerformanceRatio, Is.EqualTo(0.5d).Within(1e-9));
    }

    [Test]
    public void Sample_ReportsNoPerformanceRatioWithoutCpufreq()
    {
        WriteProcStat(user: 100, idle: 900);

        using var reader = CreateReader();
        var sample = reader.Sample();

        Assert.That(sample.CpuPerformanceRatio, Is.Null);
    }

    // The energy arithmetic itself is covered by RaplEnergyMathTests rather than from here. A real powercap
    // zone is named "intel-rapl:0", and Directory.CreateDirectory rejects the colon on NTFS (verified — it
    // throws, and the enumeration then finds nothing), so a filesystem-shaped test of that logic could only
    // ever run on Linux. The wrap handling is the subtle part and deserves to be tested everywhere.

    [Test]
    public void Sample_ReportsNoPackagePowerWithoutAPowercapTree()
    {
        WriteProcStat(user: 100, idle: 900);
        WriteCpuFrequency("base_frequency", kilohertz: 2_000_000, currentKilohertz: 2_000_000);

        using var reader = CreateReader();
        reader.Sample();
        AdvanceClock(1d);
        var sample = reader.Sample();

        Assert.That(sample.CpuPackagePowerWatts, Is.Null);
    }

    [Test]
    public void Sample_SurvivesAnUnreadableTree()
    {
        using var reader = CreateReader();
        var sample = reader.Sample();

        // Nothing exists at all. A telemetry tick must degrade, never throw.
        Assert.Multiple(() =>
        {
            Assert.That(sample.HasAnyReading, Is.False);
            Assert.That(sample.CpuUtilizationFraction, Is.Null);
            Assert.That(sample.CpuPerformanceRatio, Is.Null);
            Assert.That(sample.CpuPackagePowerWatts, Is.Null);
        });
    }
}
