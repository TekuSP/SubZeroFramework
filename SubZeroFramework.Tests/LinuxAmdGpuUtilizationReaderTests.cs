using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the amdgpu reader against a synthetic sysfs tree — the hardware that actually matters here, since
/// every Framework product uses AMD or Intel integrated graphics and the Framework 16 graphics module is AMD.
/// </summary>
/// <remarks>
/// The attribute names, units and label values below are the kernel amdgpu hwmon ABI verbatim. Units are the
/// expensive thing to get wrong: power is microwatts and temperature is millidegrees, so an unconverted
/// reading is off by a factor of a million or a thousand and still looks like a plausible number.
/// </remarks>
[TestFixture]
public class LinuxAmdGpuUtilizationReaderTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "szf-amdgpu-" + Guid.NewGuid().ToString("N"));
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
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private string SysfsRoot => Path.Combine(_root, "sys");

    private LinuxAmdGpuUtilizationReader CreateReader()
        => new(NullLogger<LinuxAmdGpuUtilizationReader>.Instance, SysfsRoot);

    /// <summary>Builds one amdgpu card and returns its device directory.</summary>
    private string CreateCard(string cardName = "card0", string runtimeStatus = "active", int busyPercent = 42)
    {
        var devicePath = Path.Combine(SysfsRoot, "class", "drm", cardName, "device");
        Directory.CreateDirectory(devicePath);

        File.WriteAllText(
            Path.Combine(devicePath, "uevent"),
            "DRIVER=amdgpu\nPCI_ID=1002:7480\nPCI_SLOT_NAME=0000:c1:00.0\n");
        File.WriteAllText(Path.Combine(devicePath, "gpu_busy_percent"), busyPercent + "\n");

        Directory.CreateDirectory(Path.Combine(devicePath, "power"));
        File.WriteAllText(Path.Combine(devicePath, "power", "runtime_status"), runtimeStatus + "\n");

        return devicePath;
    }

    /// <summary>
    /// Adds a hwmon directory. The suffix is deliberately not "hwmon0": the kernel assigns it at probe time
    /// based on which drivers loaded first, so a reader that hardcodes a number works only by luck.
    /// </summary>
    private static string CreateHwmon(string devicePath, string hwmonName = "hwmon3")
    {
        var hwmonPath = Path.Combine(devicePath, "hwmon", hwmonName);
        Directory.CreateDirectory(hwmonPath);
        return hwmonPath;
    }

    private static void WriteSensor(string hwmonPath, int index, string label, long milliCelsius)
    {
        File.WriteAllText(Path.Combine(hwmonPath, $"temp{index}_label"), label + "\n");
        File.WriteAllText(Path.Combine(hwmonPath, $"temp{index}_input"), milliCelsius + "\n");
    }

    [Test]
    public void Sample_WithoutHwmon_StillReportsUtilization()
    {
        CreateCard(busyPercent: 42);

        using var reader = CreateReader();
        var devices = reader.Sample();

        // The extended fields are an addition, not a precondition. A card with no hwmon must keep working
        // exactly as it did before they existed.
        Assert.That(devices, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].UtilizationPercent, Is.EqualTo(42d));
            Assert.That(devices[0].HasExtendedTelemetry, Is.False);
            Assert.That(devices[0].PowerWatts, Is.Null);
        });
    }

    [Test]
    public void Sample_ConvertsPowerFromMicrowattsToWatts()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "power1_average"), "34500000\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        Assert.That(devices[0].PowerWatts, Is.EqualTo(34.5d).Within(1e-9));
    }

    [Test]
    public void Sample_PrefersAveragePowerOverInstantaneous()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "power1_average"), "30000000\n");
        File.WriteAllText(Path.Combine(hwmon, "power1_input"), "95000000\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // The instantaneous figure swings hard between samples; a feed-forward term fed from it chases noise.
        Assert.That(devices[0].PowerWatts, Is.EqualTo(30d).Within(1e-9));
    }

    [Test]
    public void Sample_FallsBackToInstantaneousPowerWhenNoAverageExists()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "power1_input"), "12000000\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        Assert.That(devices[0].PowerWatts, Is.EqualTo(12d).Within(1e-9));
    }

    [Test]
    public void Sample_ResolvesTemperaturesByLabelNotByIndex()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);

        // Deliberately out of the conventional order. A reader that assumed temp1 = edge and temp2 = junction
        // would report the memory temperature as the hotspot — and memory runs far cooler, so the controller
        // would conclude the GPU was fine while the junction was cooking.
        WriteSensor(hwmon, index: 1, label: "mem", milliCelsius: 52_000);
        WriteSensor(hwmon, index: 2, label: "edge", milliCelsius: 61_000);
        WriteSensor(hwmon, index: 3, label: "junction", milliCelsius: 88_000);

        using var reader = CreateReader();
        var devices = reader.Sample();

        Assert.Multiple(() =>
        {
            Assert.That(devices[0].TemperatureCelsius, Is.EqualTo(61d).Within(1e-9), "edge");
            Assert.That(devices[0].HotspotTemperatureCelsius, Is.EqualTo(88d).Within(1e-9), "junction");
        });
    }

    [Test]
    public void Sample_ReportsNoHotspotWhenTheCardHasNoJunctionSensor()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        WriteSensor(hwmon, index: 1, label: "edge", milliCelsius: 55_000);

        using var reader = CreateReader();
        var devices = reader.Sample();

        // Many APUs expose edge only. Null says so; borrowing another sensor would invent a hotspot.
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].TemperatureCelsius, Is.EqualTo(55d).Within(1e-9));
            Assert.That(devices[0].HotspotTemperatureCelsius, Is.Null);
        });
    }

    [Test]
    public void Sample_ConvertsCoreClockFromHertzToMegahertz()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "freq1_input"), "2200000000\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        Assert.That(devices[0].CoreClockMegahertz, Is.EqualTo(2200d).Within(1e-9));
    }

    [Test]
    public void Sample_NeverReportsThrottleReasonsForAmd()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "power1_average"), "20000000\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // amdgpu exposes no throttle-reason attribute. Null means "not reported"; None would claim the GPU is
        // definitely not throttling, which this reader has no way to know.
        Assert.That(devices[0].ThrottleReasons, Is.Null);
    }

    [Test]
    public void Sample_DoesNotTouchHwmonOnARuntimeSuspendedCard()
    {
        var device = CreateCard(runtimeStatus: "suspended");
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "power1_average"), "34500000\n");
        WriteSensor(hwmon, index: 1, label: "edge", milliCelsius: 61_000);

        using var reader = CreateReader();
        var devices = reader.Sample();

        // The whole reason this reader enumerates from sysfs is that querying the SMU RESUMES a sleeping
        // discrete GPU, which is how monitoring tools have historically wrecked battery life. The extended
        // reads go through the same SMU, so they must sit behind the same gate.
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].UtilizationPercent, Is.Zero, "A suspended GPU is definitionally idle.");
            Assert.That(devices[0].PowerWatts, Is.Null);
            Assert.That(devices[0].TemperatureCelsius, Is.Null);
            Assert.That(devices[0].HasExtendedTelemetry, Is.False);
        });
    }

    [Test]
    public void Sample_FindsHwmonWhateverItsNumberIs()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device, hwmonName: "hwmon7");
        File.WriteAllText(Path.Combine(hwmon, "power1_average"), "15000000\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        Assert.That(devices[0].PowerWatts, Is.EqualTo(15d).Within(1e-9));
    }

    [Test]
    public void Sample_IgnoresANegativePowerReading()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "power1_average"), "-1\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // Some ASICs write a sentinel here when the rail is unreadable. Negative watts is not a measurement.
        Assert.That(devices[0].PowerWatts, Is.Null);
    }

    [Test]
    public void Sample_ReportsZeroPowerForAnIdleButAwakeCard()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "power1_average"), "0\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // Zero is a real reading here, unlike the negative sentinel — so it must survive as 0, not become null.
        Assert.That(devices[0].PowerWatts, Is.Zero);
    }

    [Test]
    public void Sample_ReportsNoClockRatioWithoutAMaximum()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "freq1_input"), "2200000000\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // amdgpu hwmon exposes no permitted-maximum clock, so the ratio cannot be computed. Null says that;
        // assuming some nominal maximum would manufacture a throttle signal out of nothing.
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].CoreClockMegahertz, Is.EqualTo(2200d).Within(1e-9));
            Assert.That(devices[0].MaxCoreClockMegahertz, Is.Null);
            Assert.That(devices[0].CoreClockRatio, Is.Null);
        });
    }

    [Test]
    public void Sample_ReadsTheMaximumClockFromTheDpmTable()
    {
        var device = CreateCard();
        var hwmon = CreateHwmon(device);
        File.WriteAllText(Path.Combine(hwmon, "freq1_input"), "1100000000\n");
        File.WriteAllText(Path.Combine(device, "pp_dpm_sclk"), "0: 500Mhz\n1: 1100Mhz *\n2: 2200Mhz\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // 1100 of a permitted 2200 is half speed — the ratio the fan controller reads as a throttle proxy.
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].MaxCoreClockMegahertz, Is.EqualTo(2200d));
            Assert.That(devices[0].CoreClockRatio, Is.EqualTo(0.5d).Within(1e-9));
        });
    }

    [Test]
    public void Sample_ReportsVideoMemoryInBytes()
    {
        var device = CreateCard();
        File.WriteAllText(Path.Combine(device, "mem_info_vram_total"), "8589934592\n");
        File.WriteAllText(Path.Combine(device, "mem_info_vram_used"), "2147483648\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // The amdgpu attributes are already BYTES, unlike the hwmon ones — no scaling, and a reader that
        // applied any would be out by whatever factor it invented.
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].VramTotalBytes, Is.EqualTo(8_589_934_592d));
            Assert.That(devices[0].VramUsedBytes, Is.EqualTo(2_147_483_648d));
            Assert.That(devices[0].VramUtilizationPercent, Is.EqualTo(25d).Within(1e-9));
        });
    }

    [Test]
    public void Sample_ReportsZeroVideoMemoryUseForAnIdleCard()
    {
        var device = CreateCard();
        File.WriteAllText(Path.Combine(device, "mem_info_vram_total"), "8589934592\n");
        File.WriteAllText(Path.Combine(device, "mem_info_vram_used"), "0\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // Zero allocated is a real state. Mapping it to "unknown" would make the memory readout blink out
        // every time the card went quiet.
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].VramUsedBytes, Is.Zero);
            Assert.That(devices[0].VramUtilizationPercent, Is.Zero);
        });
    }

    [Test]
    public void Sample_DoesNotReadVideoMemoryFromASuspendedCard()
    {
        var device = CreateCard(runtimeStatus: "suspended");
        File.WriteAllText(Path.Combine(device, "mem_info_vram_total"), "8589934592\n");
        File.WriteAllText(Path.Combine(device, "mem_info_vram_used"), "2147483648\n");

        using var reader = CreateReader();
        var devices = reader.Sample();

        // The whole point of the runtime-PM gate: a sleeping discrete GPU is reported as idle from sysfs
        // state alone, without any attribute read that could resume it. The total is exempt because it was
        // resolved once during enumeration and is a fixed BAR-size property, not an SMU query.
        Assert.Multiple(() =>
        {
            Assert.That(devices[0].UtilizationPercent, Is.Zero);
            Assert.That(devices[0].VramUsedBytes, Is.Null);
            Assert.That(devices[0].VramTotalBytes, Is.EqualTo(8_589_934_592d));
        });
    }
}
