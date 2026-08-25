using System.Diagnostics;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Loads the CPU on purpose, so a calibration run can create a thermal gradient worth measuring.
/// </summary>
/// <remarks>
/// <para>
/// A calibration needs the machine hot and, crucially, hot at a STEADY rate — the fit assumes the only thing
/// changing during the measurement is fan duty. Waiting for the user to happen to run a build would take
/// hours and would not hold still, so the service generates the heat itself.
/// </para>
/// <para>
/// <b>Below-normal priority, deliberately.</b> The load must not starve the very telemetry loop that is
/// measuring it, nor make the machine unusable while it runs. The OS scheduler will still give these threads
/// essentially all otherwise-idle time, which is what produces the heat; what it will not do is delay the EC
/// reads the fit depends on.
/// </para>
/// <para>
/// <b>Deliberately not vectorised.</b> A tight AVX loop would draw more power, but it also trips different
/// power limits and a different clock ceiling than ordinary work — so the machine would be calibrated for a
/// load nothing else on it produces. Plain scalar arithmetic across every core is closer to a compile or a
/// game, which is what the resulting model has to serve.
/// </para>
/// </remarks>
public sealed class CpuLoadGenerator : ICpuLoadGenerator, IDisposable
{
    /// <summary>
    /// How long each worker burns before considering whether it owes the machine any idle time.
    /// </summary>
    /// <remarks>
    /// Milliseconds, against a chassis time constant of tens of seconds — so the cycle is invisible thermally
    /// and the run sees a steady fraction of maximum rather than a pulsing load. The duty is applied to every
    /// core rather than loading four fifths of them and leaving the rest cold, which would heat the package
    /// unevenly and model a thermal state no real workload produces.
    /// </remarks>
    private static readonly TimeSpan BurnChunk = TimeSpan.FromMilliseconds(10);


    /// <summary>
    /// The least of the machine this generator will hold, however busy everything else is.
    /// </summary>
    /// <remarks>
    /// Backing off entirely would be wrong even under heavy foreign load: the calibration needs SOME of the
    /// heat to be its own, or a run becomes a measurement of whatever the user happened to be doing. If the
    /// machine is genuinely too busy, the run's minimum-power check is what refuses it — not silent starvation
    /// here.
    /// </remarks>
    private const double MinimumOwnFraction = 0.2d;

    private readonly ISystemLoadProbe? _systemLoad;
    private readonly TimeSpan? _rampDuration;
    private readonly TimeSpan _governorInterval;
    private readonly double _targetFraction;
    private readonly Lock _stateLock = new();
    private CancellationTokenSource? _cancellation;
    private Task[] _workers = [];
    private LoadRamp? _ramp;
    private double _effectiveTargetFraction = LoadRamp.DefaultTargetFraction;
    private bool _disposed;

    /// <param name="systemLoad">
    /// Measures how busy the whole machine is, so the generator can aim at a share of the MACHINE rather than
    /// of itself. Null degrades to holding its own fixed share — correct on an idle machine, and too much on
    /// a busy one.
    /// </param>
    /// <param name="rampDuration">How long to climb to the target. Shortened by tests; see <see cref="LoadRamp"/>.</param>
    /// <param name="governorInterval">
    /// How often to reconsider how much the machine can spare. Shortened by tests, whose scripted load
    /// changes instantly and which would otherwise spend their time watching the smoothing converge.
    /// </param>
    /// <param name="targetFraction">Where the ramp ends. Varied by tests; see <see cref="LoadRamp"/>.</param>
    public CpuLoadGenerator(
        ISystemLoadProbe? systemLoad = null,
        TimeSpan? rampDuration = null,
        TimeSpan? governorInterval = null,
        double? targetFraction = null)
    {
        _systemLoad = systemLoad;
        _rampDuration = rampDuration;
        _governorInterval = governorInterval ?? TimeSpan.FromMilliseconds(500);
        _targetFraction = targetFraction ?? LoadRamp.DefaultTargetFraction;
    }

    /// <summary>True while load is being generated.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _cancellation is not null;
            }
        }
    }

    /// <summary>What each worker is currently aiming at: the ramp, capped by whatever the machine can spare.</summary>
    public double CurrentLoadFraction
    {
        get
        {
            lock (_stateLock)
            {
                return _ramp is null ? 0d : Math.Min(_ramp.CurrentFraction, _effectiveTargetFraction);
            }
        }
    }

    public bool IsAtTargetLoad
    {
        get
        {
            lock (_stateLock)
            {
                return _ramp?.IsAtTarget ?? false;
            }
        }
    }

    /// <summary>
    /// Starts loading every logical processor. Idempotent — a second call while running does nothing.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The generator has been disposed.</exception>
    public void Start()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_cancellation is not null)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;

            var ramp = new LoadRamp(_rampDuration, _targetFraction);
            _ramp = ramp;
            _effectiveTargetFraction = _targetFraction;

            // One worker per logical processor. Fewer would leave cores idle and under-heat the package;
            // more would only add scheduling overhead.
            var workerCount = Math.Max(1, Environment.ProcessorCount);
            _workers = new Task[workerCount];

            for (var i = 0; i < workerCount; i++)
            {
                _workers[i] = Task.Factory.StartNew(
                    () => Burn(ramp, cancellation.Token),
                    cancellation.Token,

                    // LongRunning gets a dedicated thread rather than a pool one: these run for minutes and
                    // would otherwise occupy — and starve — the same pool the service does its real work on.
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            // One governor for all workers. Each sampling the machine independently would multiply the probe
            // cost by the core count and let the workers disagree about how much room there is.
            if (_systemLoad is not null)
            {
                Task.Factory.StartNew(
                    () => Govern(cancellation.Token),
                    cancellation.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// Stops the load and waits for the workers to finish.
    /// </summary>
    /// <remarks>
    /// Waits rather than fires and forgets, so a run that ends — for any reason, including a safety abort —
    /// leaves the machine genuinely idle before the next step measures anything.
    /// </remarks>
    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Task[] workers;

        lock (_stateLock)
        {
            cancellation = _cancellation;
            workers = _workers;
            _cancellation = null;
            _workers = [];
            _ramp = null;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();

        try
        {
            // Bounded: a worker that somehow will not exit must not hang service shutdown. The loop below
            // checks cancellation every iteration, so this should return almost immediately.
            Task.WaitAll(workers, TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation surfaces here; nothing to report.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    /// <summary>
    /// Keeps the WHOLE MACHINE near the target by giving away whatever everything else is already using.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this the target is a promise about this process, not about the machine — and the machine is
    /// what the user is trying to keep using. A generator holding its own 80% while a build takes another 20%
    /// leaves nothing spare, which is the state the number was chosen to prevent.
    /// </para>
    /// <para>
    /// It also makes the run's load STEADIER, which the thermal fit depends on: as foreign load comes and
    /// goes, this moves the opposite way and the total the chassis actually sees stays put.
    /// </para>
    /// <para>
    /// Adjusts gradually rather than snapping to each new reading. Utilisation samples are noisy, and a
    /// target that chases every sample would turn that noise into a load that visibly pulses.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The share of the machine this generator is currently willing to take, after giving away whatever
    /// everything else is already using.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="CurrentLoadFraction"/>, which is this capped by how far the ramp has climbed.
    /// Worth surfacing: a run quietly holding 40% because the machine is busy looks like a broken calibration
    /// unless something can say why.
    /// </remarks>
    public double EffectiveTargetFraction
    {
        get
        {
            lock (_stateLock)
            {
                return _effectiveTargetFraction;
            }
        }
    }

    private void Govern(CancellationToken cancellationToken)
    {
        const double smoothing = 0.25d;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_systemLoad?.TotalCpuUtilizationFraction is double total)
            {
                // Everything that is not us. Clamped at zero because the two figures come from different
                // sources and can disagree by a little around the edges.
                var foreign = Math.Max(0d, total - _systemLoad.OwnCpuUtilizationFraction);
                var room = Math.Clamp(_targetFraction - foreign, Math.Min(MinimumOwnFraction, _targetFraction), _targetFraction);

                lock (_stateLock)
                {
                    _effectiveTargetFraction += (room - _effectiveTargetFraction) * smoothing;
                }
            }

            cancellationToken.WaitHandle.WaitOne(_governorInterval);
        }
    }

    private void Burn(LoadRamp ramp, CancellationToken cancellationToken)
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        }
        catch (PlatformNotSupportedException)
        {
            // Priority is advisory; the load still works without it.
        }

        // Floating-point work with a serial dependency, so the compiler cannot hoist it and the CPU cannot
        // retire it out of order — the loop has to actually execute, which is the entire point.
        var accumulator = 1.000001d;
        var iterations = 0L;

        var limiter = new AdaptiveDutyLimiter(BurnChunk);
        var session = Stopwatch.StartNew();
        var chunkStartedAt = session.Elapsed;

        while (!cancellationToken.IsCancellationRequested)
        {
            accumulator = Math.Sqrt(accumulator * 1.0000173d) + 1.0000019d;

            // Checking the clock every iteration would cost more than the work; every few thousand keeps the
            // slice accurate to well under a millisecond while staying negligible.
            if (++iterations % 4096 != 0)
            {
                continue;
            }

            if (accumulator == double.PositiveInfinity)
            {
                accumulator = 1.000001d;
            }

            var burned = session.Elapsed - chunkStartedAt;
            if (burned < limiter.BurnFor)
            {
                continue;
            }

            // The ramp says how far the climb has got; the governor says how much the machine can spare.
            // Whichever is smaller is what this worker may take.
            var target = Math.Min(ramp.CurrentFraction, EffectiveTargetFraction);

            var beforeSleep = session.Elapsed;

            // WaitHandle rather than Sleep so cancellation cuts it short, instead of shutdown waiting out a
            // slice per worker.
            if (limiter.SleepFor > TimeSpan.Zero)
            {
                cancellationToken.WaitHandle.WaitOne(limiter.SleepFor);
            }

            // What the sleep ACTUALLY cost, which is the only figure the limiter can learn from.
            var idled = session.Elapsed - beforeSleep;
            limiter.Record(burned, idled, target);

            chunkStartedAt = session.Elapsed;
        }

        // Consumed so a future optimiser cannot decide the whole loop is dead. GC.KeepAlive is the cheapest
        // sink that is guaranteed not to be elided.
        GC.KeepAlive(accumulator);
    }
}
