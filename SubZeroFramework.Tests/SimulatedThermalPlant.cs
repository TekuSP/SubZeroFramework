using System.Diagnostics;

using FrameworkDotnet.Enums;
using FrameworkDotnet.Snapshots;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services.Control;

using UnitsNet;

namespace SubZeroFramework.Tests;

/// <summary>
/// A laptop that responds to fan duty the way a real one does, so a calibration run can be exercised against
/// a plant whose true K, τ and dead time are known.
/// </summary>
/// <remarks>
/// <para>
/// Closed loop on purpose. The run sets a duty, the plant's temperature responds to it, and the run measures
/// the response — so a bug that commands the wrong duty, reads the wrong sensor, or steps before settling
/// shows up as a wrong fitted model rather than passing quietly.
/// </para>
/// <para>
/// <b>Temperatures are quantised to whole degrees</b>, because the EC reports them that way and that single
/// detail is what makes naive settle detection fail: a machine climbing steadily still reports the same value
/// on most consecutive samples.
/// </para>
/// </remarks>
public sealed class SimulatedThermalPlant : StubFrameworkDataProvider, ICpuLoadGenerator, IGpuLoadGenerator, IDisposable
{
    /// <summary>Degrees the temperature falls per point of duty — the K a run should recover.</summary>
    public const double ProcessGainCelsiusPerPercent = 0.42d;

    private const double AmbientCelsius = 40d;
    private const double IdleWatts = 5d;

    private readonly CancellationTokenSource _stopping = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Task _pump;
    private readonly Lock _stateLock = new();

    private double _celsius = AmbientCelsius;
    private double _runawayCelsius = AmbientCelsius;
    private double _dutyPercent;
    private int? _commandedRpm;
    private bool _cpuLoadOn;
    private bool _gpuLoadOn;
    private TimeSpan _lastTick;

    public SimulatedThermalPlant(TimeSpan? timeConstant = null, TimeSpan? pumpInterval = null)
    {
        TimeConstant = timeConstant ?? TimeSpan.FromMilliseconds(80);
        PumpInterval = pumpInterval ?? TimeSpan.FromMilliseconds(2);
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>How fast the simulated chassis responds.</summary>
    public TimeSpan TimeConstant { get; }

    /// <summary>How often a new snapshot is published.</summary>
    public TimeSpan PumpInterval { get; }

    /// <summary>How much hotter the machine runs with the load generator going.</summary>
    public double LoadRiseCelsius { get; init; } = 54d;

    /// <summary>Package power reported while loaded — what the "is this machine busy enough" gate reads.</summary>
    public double LoadedWatts { get; init; } = 45d;

    /// <summary>The duty below which the fan stops turning, so minimum-spin detection has something to find.</summary>
    public double StallDutyPercent { get; init; } = 12d;

    /// <summary>RPM produced per point of duty.</summary>
    public double RpmPerDutyPercent { get; init; } = 50d;

    /// <summary>Whether the fan honours a commanded speed, which is the cascade-versus-duty verdict.</summary>
    public bool HonoursSpeedCommands { get; init; }

    /// <summary>
    /// Where the machine's power is coming from. Settable mid-run, so a test can pull the charger out.
    /// </summary>
    /// <remarks>
    /// Defaults to AC with a battery present — the ordinary state of a plugged-in laptop, and the only one in
    /// which a calibration should proceed.
    /// </remarks>
    public FrameworkPowerSourceState PowerSourceState { get; set; } = FrameworkPowerSourceState.AcAndBattery;

    /// <summary>What the battery reports it is doing.</summary>
    /// <remarks>
    /// Independent of <see cref="PowerSourceState"/> on purpose. A plugged-in machine under heavy load can
    /// report a discharging battery — the adapter cannot carry the peak — and that must NOT read as "on
    /// battery", which is exactly the trap a discharge-only check falls into.
    /// </remarks>
    public FrameworkBatteryState BatteryState { get; set; } = FrameworkBatteryState.Idle;

    /// <summary>When false, no power snapshots are published at all, leaving the power source unknown.</summary>
    public bool ReportsPowerSource { get; init; } = true;

    /// <summary>
    /// A sensor that climbs past the safety ceiling regardless of fan duty, or null for none.
    /// </summary>
    /// <remarks>
    /// Stands in for something the fan under test does not cool — the CPU while the GPU fan is being
    /// calibrated, say. The run is not fitting against it, which is exactly why it has to be watched.
    /// </remarks>
    public int? RunawaySensorIndex { get; init; }

    /// <summary>When true, sensor 0 reports an error state, so the driving temperature cannot be read.</summary>
    public bool DrivingSensorFails { get; init; }

    /// <summary>
    /// Clock as a fraction of base when the fan is at the low pre-step duty — the throttled state.
    /// </summary>
    /// <remarks>
    /// Below 1 on purpose: the whole point of the measurement is a machine that is being held back by heat,
    /// so a calibration can show what the extra fan recovers.
    /// </remarks>
    public double CpuPerformanceRatioWhenHot { get; init; } = 0.72d;

    /// <summary>Clock as a fraction of base once the fan is at full duty.</summary>
    public double CpuPerformanceRatioWhenCool { get; init; } = 0.98d;

    /// <summary>Graphics core clock when hot, in MHz.</summary>
    public double GpuClockWhenHotMegahertz { get; init; } = 1400d;

    /// <summary>Graphics core clock once cool, in MHz.</summary>
    public double GpuClockWhenCoolMegahertz { get; init; } = 2100d;

    /// <summary>The duty above which the plant reports its cool, unthrottled speeds.</summary>
    public double CoolDutyThresholdPercent { get; init; } = 60d;

    /// <summary>When false, no clock or performance ratio is reported at all.</summary>
    public bool ReportsClock { get; init; } = true;

    /// <summary>
    /// The platform role sensor 0 reports.
    /// </summary>
    /// <remarks>
    /// This is what a calibration reads to decide whether it must heat the CPU or the GPU, so it is the knob
    /// that turns this plant into a Framework 16 right fan.
    /// </remarks>
    public FrameworkSensorName DrivingSensorName { get; init; } = FrameworkSensorName.Apu;

    /// <summary>Which load source actually heats this plant. Load from any other source does nothing.</summary>
    public ThermalLoadTarget HeatedBy { get; init; } = ThermalLoadTarget.Cpu;

    /// <summary>Whether this plant can generate GPU load, so a test can simulate a machine with no dGPU.</summary>
    public bool GpuLoadAvailable { get; init; } = true;

    /// <summary>True while any load source that actually heats THIS plant is running.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return IsHeated;
            }
        }
    }

    /// <summary>True while CPU load is running, whether or not it heats this plant.</summary>
    public bool IsCpuLoadRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _cpuLoadOn;
            }
        }
    }

    /// <summary>True while GPU load is running, whether or not it heats this plant.</summary>
    public bool IsGpuLoadRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _gpuLoadOn;
            }
        }
    }

    // Latched, because a run always stops both generators on the way out. Asserting on the live flags after
    // the run has finished would pass no matter what the run did in the middle — which is exactly how a
    // "never both at once" test can end up asserting nothing at all.

    /// <summary>True if CPU load was started at any point, even if it has since stopped.</summary>
    public bool CpuLoadWasStarted { get; private set; }

    /// <summary>True if GPU load was started at any point, even if it has since stopped.</summary>
    public bool GpuLoadWasStarted { get; private set; }

    string? IGpuLoadGenerator.AcceleratorName => GpuLoadAvailable ? "Simulated GPU (OpenCL)" : null;

    bool IGpuLoadGenerator.IsAvailable => GpuLoadAvailable;

    bool IGpuLoadGenerator.IsRunning => IsGpuLoadRunning;

    // The simulated plant reaches full load immediately. Tests are about what the RUN does with the ramp
    // signal, not about reproducing a ramp whose duration would dominate every test's wall clock.
    public double CurrentLoadFraction => IsRunning ? LoadRamp.DefaultTargetFraction : 0d;

    public bool IsAtTargetLoad => IsRunning;

    double IGpuLoadGenerator.CurrentLoadFraction => IsGpuLoadRunning ? LoadRamp.DefaultTargetFraction : 0d;

    double IGpuLoadGenerator.ObservedLoadFraction => IsGpuLoadRunning ? LoadRamp.DefaultTargetFraction : 0d;

    bool IGpuLoadGenerator.IsAtTargetLoad => IsGpuLoadRunning;

    public void Start()
    {
        lock (_stateLock)
        {
            _cpuLoadOn = true;
            CpuLoadWasStarted = true;
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _cpuLoadOn = false;
        }
    }

    bool IGpuLoadGenerator.Start()
    {
        if (!GpuLoadAvailable)
        {
            return false;
        }

        lock (_stateLock)
        {
            _gpuLoadOn = true;
            GpuLoadWasStarted = true;
        }

        return true;
    }

    void IGpuLoadGenerator.Stop()
    {
        lock (_stateLock)
        {
            _gpuLoadOn = false;
        }
    }

    /// <summary>
    /// Whether a load source that heats THIS plant is running.
    /// </summary>
    /// <remarks>
    /// The whole point of the distinction: CPU load does not heat a GPU-cooled fan's sensors. A run that
    /// starts the wrong load watches a plant that never warms up, which is exactly what would happen on a
    /// Framework 16 right fan.
    /// </remarks>
    private bool IsHeated
        => (HeatedBy.HasFlag(ThermalLoadTarget.Cpu) && _cpuLoadOn)
            || (HeatedBy.HasFlag(ThermalLoadTarget.Gpu) && _gpuLoadOn);

    public override Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            _dutyPercent = dutyPercent;

            // A duty command overrides any speed command, which is what makes the two mutually exclusive on
            // real hardware too.
            _commandedRpm = null;
        }

        return base.SetFanDutyAsync(fanIndex, dutyPercent, cancellationToken);
    }

    public override Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            _commandedRpm = HonoursSpeedCommands ? targetSpeedRpm : null;
        }

        return base.SetFanRpmAsync(fanIndex, targetSpeedRpm, cancellationToken);
    }

    public void Dispose()
    {
        _stopping.Cancel();

        try
        {
            _pump.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation surfaces here; nothing to report.
        }

        _stopping.Dispose();
        ThermalSource.Dispose();
    }

    private async Task PumpAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            Advance();

            try
            {
                await Task.Delay(PumpInterval, _stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Advance()
    {
        double celsius;
        double rpm;
        double commandedDuty;
        bool cpuLoaded;
        bool gpuLoaded;

        lock (_stateLock)
        {
            var now = _clock.Elapsed;
            var elapsed = now - _lastTick;
            _lastTick = now;

            var duty = _commandedRpm is int commanded
                ? Math.Clamp(commanded / RpmPerDutyPercent, 0d, 100d)
                : _dutyPercent;

            // First order toward the temperature this duty would eventually hold.
            var target = AmbientCelsius
                + (IsHeated ? LoadRiseCelsius : 0d)
                - (ProcessGainCelsiusPerPercent * duty);

            var share = Math.Clamp(elapsed.TotalSeconds / TimeConstant.TotalSeconds, 0d, 1d);
            _celsius += (target - _celsius) * share;

            celsius = _celsius;
            rpm = duty < StallDutyPercent ? 0d : duty * RpmPerDutyPercent;
            commandedDuty = duty;
            cpuLoaded = _cpuLoadOn;
            gpuLoaded = _gpuLoadOn;
        }

        // Speed follows the FAN, which is the relationship a calibration exists to measure: more cooling, less
        // throttling, more sustained clock.
        var cool = commandedDuty >= CoolDutyThresholdPercent;

        LatestControlTelemetry = new ObservedControlTelemetry(
            new ControlTelemetrySample
            {
                // Power is reported on the channel of whichever component is actually working. A machine
                // whose GPU is at full load does not report that draw as CPU package power, and a run that
                // read the wrong channel would see an idle processor and conclude the machine never got busy.
                CpuPackagePowerWatts = cpuLoaded ? LoadedWatts : IdleWatts,
                GpuPowerWatts = gpuLoaded ? LoadedWatts : IdleWatts,
                CpuPerformanceRatio = ReportsClock
                    ? (cool ? CpuPerformanceRatioWhenCool : CpuPerformanceRatioWhenHot)
                    : null,
                GpuCoreClockMegahertz = ReportsClock
                    ? (cool ? GpuClockWhenCoolMegahertz : GpuClockWhenHotMegahertz)
                    : null,
            },
            DateTimeOffset.UtcNow);

        // Climbs steadily from ambient and never comes down, because nothing this run controls cools it.
        _runawayCelsius = Math.Min(120d, _runawayCelsius + 2d);

        ThermalSource.OnNext(CreateSnapshot(Math.Round(celsius), rpm));

        if (ReportsPowerSource)
        {
            PowerSource.OnNext(CreatePowerSnapshot());
        }
    }

    private FrameworkPowerSnapshot CreatePowerSnapshot()
        => new(
            PowerSourceState,
            batteryCount: 1,
            new FrameworkBatterySnapshot(
                manufacturer: "Test",
                modelNumber: "Test",
                serialNumber: "Test",
                batteryType: "LION",
                presentVoltage: ElectricPotential.FromVolts(15d),
                presentRate: ElectricCurrent.FromAmperes(0d),
                remainingCapacity: ElectricCharge.FromAmpereHours(3d),
                designCapacity: ElectricCharge.FromAmpereHours(4d),
                designVoltage: ElectricPotential.FromVolts(15d),
                lastFullChargeCapacity: ElectricCharge.FromAmpereHours(4d),
                cycleCount: 10u,
                chargeLevel: Ratio.FromPercent(75d),
                batteryState: BatteryState));

    private FrameworkThermalSnapshot CreateSnapshot(double celsius, double rpm)
    {
        var fan = new FrameworkFanSnapshot(
            FrameworkFanState.Ok,
            RotationalSpeed.FromRevolutionsPerMinute(rpm),
            FrameworkFanName.Unknown);

        return new FrameworkThermalSnapshot(
            fanCount: 2,
            sensorCount: 8,
            Sensor(0, celsius), Sensor(1, celsius), Sensor(2, celsius), Sensor(3, celsius),
            Sensor(4, celsius), Sensor(5, celsius), Sensor(6, celsius), Sensor(7, celsius),
            fan, fan, fan, fan);
    }

    private FrameworkTemperatureSnapshot Sensor(int index, double celsius)
    {
        // Sensor 0 is the one tests drive against, so failing it is how a dead driving sensor is simulated.
        if (index == 0 && DrivingSensorFails)
        {
            return new FrameworkTemperatureSnapshot(
                FrameworkTemperatureState.Error,
                Temperature.FromDegreesCelsius(0d),
                FrameworkSensorName.Unknown);
        }

        return new FrameworkTemperatureSnapshot(
            FrameworkTemperatureState.Ok,
            Temperature.FromDegreesCelsius(index == RunawaySensorIndex ? _runawayCelsius : celsius),
            index == 0 ? DrivingSensorName : FrameworkSensorName.Unknown);
    }
}
