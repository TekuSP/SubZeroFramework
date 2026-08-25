using System.Collections.Immutable;
using System.Diagnostics;

using ILGPU;
using ILGPU.Runtime;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Loads the graphics hardware with a compute kernel, so a calibration can heat what a GPU fan cools.
/// </summary>
/// <remarks>
/// <para>
/// Uses ILGPU, which compiles a plain C# kernel to the accelerator's own instruction set — OpenCL on the AMD
/// hardware a Framework 16 ships, CUDA where that is what is present. Written as C# rather than hand-written
/// driver interop deliberately: this is a system service, and a bug in hand-rolled GPU marshalling would be a
/// bug in a privileged process.
/// </para>
/// <para>
/// <b>Everything here fails soft.</b> Accelerators are created only while a calibration runs and disposed
/// immediately after, and every entry point catches. A missing OpenCL runtime, a driver that will not
/// initialise in a service session, an unplugged eGPU — all of them report "no GPU load available", which
/// makes the calibration refuse. None of them may take down the process that is controlling the fans.
/// </para>
/// </remarks>
public sealed class IlgpuGpuLoadGenerator : IGpuLoadGenerator, IDisposable
{
    /// <summary>
    /// Elements per dispatch: enough lanes to occupy any accelerator this is likely to meet, and no more.
    /// </summary>
    /// <remarks>
    /// The arithmetic per element — not the element count — is what sets the power draw, and it is the knob
    /// that gets tuned below. Keeping the buffer modest keeps the kernel compute-bound: a working set large
    /// enough to fall out of cache turns this into a memory-bandwidth load, which draws its power somewhere
    /// else entirely and would calibrate the fan for a machine state no real workload produces.
    /// </remarks>
    private const int ProblemSize = 1 << 20;

    /// <summary>Where the search for the right dispatch size starts.</summary>
    private const int ProbeIterations = 128;

    /// <summary>Dispatches averaged per timing probe, so one unlucky launch cannot size the whole run.</summary>
    private const int ProbeDispatches = 4;

    /// <summary>
    /// Bounds on the arithmetic per element.
    /// </summary>
    /// <remarks>
    /// The floor exists to keep the kernel compute-bound: below roughly this much arithmetic the read and
    /// write per element start to dominate, and the load stops resembling the thing being modelled.
    /// </remarks>
    private const int MinimumIterations = 32;

    private const int MaximumIterations = 1 << 16;

    /// <summary>
    /// How long one dispatch should take.
    /// </summary>
    /// <remarks>
    /// <b>This is the control resolution of the whole generator.</b> A dispatch cannot be cut short once
    /// launched, so it is the finest slice of work that can be scheduled and therefore the smallest step the
    /// duty control can take. Short enough that even a 15% target is a few dispatches against a sleep; long
    /// enough that launch and synchronisation overhead — tens of microseconds — stays a rounding error rather
    /// than becoming the workload.
    /// </remarks>
    private static readonly TimeSpan TargetDispatchDuration = TimeSpan.FromMilliseconds(2d);

    /// <summary>How much of each cycle's measurement folds into the running dispatch cost.</summary>
    private const double DispatchSmoothing = 0.25d;

    /// <summary>
    /// How much smaller than the largest GPU a device may be and still count as its sibling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The discriminator is compute units, NOT memory size. An integrated GPU carves its memory out of system
    /// RAM and can therefore report far MORE than a discrete card with its own VRAM — ranking by memory picks
    /// the integrated part on exactly the machines where that is most wrong. Compute-unit counts are not
    /// inflated that way: a Radeon 780M reports a handful, an RX 7700S reports several times as many.
    /// </para>
    /// <para>
    /// Half is generous enough to keep genuine siblings together — two cards on one module share a fan and
    /// should be loaded together — while excluding an integrated part that is a fraction of the size.
    /// </para>
    /// </remarks>
    private const double SiblingMultiprocessorFraction = 0.5d;

    private readonly ILogger<IlgpuGpuLoadGenerator> _logger;
    private readonly TimeSpan? _rampDuration;
    private readonly double? _targetFraction;
    private readonly Lock _stateLock = new();

    private CancellationTokenSource? _cancellation;
    private Task[] _workers = [];
    private LoadRamp? _ramp;
    private double[] _observedByDevice = [];
    private TimeSpan[] _dispatchByDevice = [];
    private bool _disposed;
    private bool _probed;
    private string? _acceleratorName;

    /// <param name="logger">Where accelerator selection and faults are reported.</param>
    /// <param name="rampDuration">How long to climb to the target. Shortened by tests; see <see cref="LoadRamp"/>.</param>
    /// <param name="targetFraction">Where the ramp ends. Varied by tests; see <see cref="LoadRamp"/>.</param>
    public IlgpuGpuLoadGenerator(
        ILogger<IlgpuGpuLoadGenerator> logger,
        TimeSpan? rampDuration = null,
        double? targetFraction = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _rampDuration = rampDuration;
        _targetFraction = targetFraction;
    }

    public bool IsAvailable
    {
        get
        {
            EnsureProbed();
            return _acceleratorName is not null;
        }
    }

    public string? AcceleratorName
    {
        get
        {
            EnsureProbed();
            return _acceleratorName;
        }
    }

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

    public double CurrentLoadFraction
    {
        get
        {
            lock (_stateLock)
            {
                return _ramp?.CurrentFraction ?? 0d;
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
    /// The measured dispatch share, taken as the LOWEST across the loaded devices.
    /// </summary>
    /// <remarks>
    /// Lowest rather than average: the figure is used to decide whether the load is up to strength, and one
    /// device sitting idle behind three busy ones is exactly the situation that must not average away.
    /// </remarks>
    public double ObservedLoadFraction
    {
        get
        {
            lock (_stateLock)
            {
                return _observedByDevice.Length == 0 ? 0d : _observedByDevice.Min();
            }
        }
    }

    /// <summary>
    /// What one dispatch costs, taken as the LONGEST across the loaded devices.
    /// </summary>
    /// <remarks>
    /// Longest rather than average, for the same reason the observed load takes the lowest: this bounds what
    /// the duty control can do, and the device with the coarsest resolution is the one that decides whether a
    /// low target is reachable at all. Zero until a device has measured itself.
    /// </remarks>
    public TimeSpan DispatchDuration
    {
        get
        {
            lock (_stateLock)
            {
                return _dispatchByDevice.Length == 0 ? TimeSpan.Zero : _dispatchByDevice.Max();
            }
        }
    }

    public bool Start()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_cancellation is not null)
            {
                return true;
            }

            if (!IsAvailable)
            {
                return false;
            }

            var devices = SelectDevices();
            if (devices.IsEmpty)
            {
                return false;
            }

            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;

            var ramp = new LoadRamp(_rampDuration, _targetFraction);
            _ramp = ramp;
            _observedByDevice = new double[devices.Length];
            _dispatchByDevice = new TimeSpan[devices.Length];

            _workers = new Task[devices.Length];
            for (var i = 0; i < devices.Length; i++)
            {
                var deviceIndex = i;
                var device = devices[i];

                // LongRunning: each occupies its thread for minutes driving dispatches, and would otherwise
                // starve the pool the service does its real work on.
                _workers[i] = Task.Factory.StartNew(
                    () => Burn(device, deviceIndex, ramp, cancellation.Token),
                    cancellation.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            return true;
        }
    }

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
            _observedByDevice = [];
            _dispatchByDevice = [];
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();

        try
        {
            // Bounded. A GPU dispatch that will not return must not hang service shutdown — and unlike CPU
            // work, a wedged kernel cannot be interrupted, only waited out or abandoned.
            Task.WaitAll(workers, TimeSpan.FromSeconds(10));
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
    /// The GPUs to load: every sibling of the most capable one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefer the dedicated hardware, and fall back to integrated only when there is nothing else. On a
    /// machine whose only GPU is integrated, that GPU sits on the same package as the processor and under the
    /// same fan, so loading it is both the best available option and thermally coherent.
    /// </para>
    /// <para>
    /// <b>The integrated part is excluded whenever a discrete one exists</b>, and not merely as second
    /// choice. It shares silicon and a power budget with the CPU cores, so loading it heats what the CPU fan
    /// cools — reintroducing exactly the cross-heating that running CPU and GPU load together would cause.
    /// </para>
    /// </remarks>
    private ImmutableArray<Device> SelectDevices()
    {
        try
        {
            using var context = Context.CreateDefault();

            var candidates = context.Devices
                .Where(static candidate => candidate.AcceleratorType != AcceleratorType.CPU)
                .OrderByDescending(static candidate => candidate.NumMultiprocessors)
                .ToArray();

            if (candidates.Length == 0)
            {
                return [];
            }

            var best = candidates[0].NumMultiprocessors;
            var threshold = best * SiblingMultiprocessorFraction;

            return [.. candidates.Where(candidate => candidate.NumMultiprocessors >= threshold)];
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not enumerate GPU accelerators; GPU load is unavailable.");
            return [];
        }
    }

    /// <summary>
    /// Looks for usable accelerators once, and remembers the answer.
    /// </summary>
    /// <remarks>
    /// The CPU accelerator is rejected explicitly. ILGPU always offers one, and taking it would run this
    /// "GPU" load on the processor — producing a confident, completely wrong model of a fan that never saw
    /// the heat. A silent fallback is far worse here than an honest refusal.
    /// </remarks>
    private void EnsureProbed()
    {
        lock (_stateLock)
        {
            if (_probed)
            {
                return;
            }

            _probed = true;

            var devices = SelectDevices();
            if (devices.IsEmpty)
            {
                _logger.LogInformation("No GPU accelerator is available; GPU-cooled fans cannot be calibrated on this machine.");
                return;
            }

            _acceleratorName = string.Join(", ", devices.Select(static device => $"{device.Name} ({device.NumMultiprocessors} CU, {device.AcceleratorType})"));
            _logger.LogInformation("GPU load will use {Accelerator}.", _acceleratorName);
        }
    }

    private void Burn(Device device, int deviceIndex, LoadRamp ramp, CancellationToken cancellationToken)
    {
        try
        {
            using var context = Context.CreateDefault();

            // Re-resolved inside this thread's own context: an ILGPU Context is not shared across the
            // accelerators created from it, and each worker owning its own keeps one faulting device from
            // taking the others down.
            var target = context.Devices.FirstOrDefault(candidate =>
                candidate.Name == device.Name && candidate.AcceleratorType == device.AcceleratorType);

            if (target is null)
            {
                return;
            }

            using var accelerator = target.CreateAccelerator(context);
            using var buffer = accelerator.Allocate1D<float>(ProblemSize);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int>(BurnKernel);
            var length = (int)buffer.Length;

            // Sized to THIS accelerator. The same kernel is a fraction of a millisecond on a discrete card
            // and tens of milliseconds on an integrated one, and since a dispatch cannot be cut short, its
            // duration is the duty control's resolution. Fixing the arithmetic instead of the duration hands
            // the slowest machines a control they cannot steer — which is exactly the wrong way round.
            var (iterations, dispatchCost) = CalibrateDispatch(kernel, buffer.View, length, accelerator, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            RecordDispatchCost(deviceIndex, dispatchCost);

            _logger.LogDebug(
                "GPU load on {Device}: {Iterations} iterations per element, {Dispatch:0.##} ms per dispatch.",
                device.Name,
                iterations,
                dispatchCost.TotalMilliseconds);

            // Seeded with what a dispatch actually costs rather than with a hopeful constant. A limiter told
            // the minimum is shorter than one dispatch cannot reach any target below one-dispatch-per-idle.
            var limiter = new AdaptiveDutyLimiter(dispatchCost);

            var session = Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested)
            {
                var startedAt = session.Elapsed;
                var dispatched = TimeSpan.Zero;
                var dispatches = 0;

                // Dispatch until the limiter's slice is filled. A single dispatch is atomic, so the slice is
                // approached in whole dispatches rather than interrupted part-way.
                do
                {
                    kernel(length, buffer.View, iterations);

                    // Synchronising each dispatch keeps the queue from growing without bound, and means
                    // cancellation takes effect within one dispatch rather than after everything queued.
                    accelerator.Synchronize();

                    dispatches++;
                    dispatched = session.Elapsed - startedAt;
                }
                while (dispatched < limiter.BurnFor && !cancellationToken.IsCancellationRequested);

                var beforeSleep = session.Elapsed;

                if (limiter.SleepFor > TimeSpan.Zero)
                {
                    cancellationToken.WaitHandle.WaitOne(limiter.SleepFor);
                }

                // Re-measured every cycle rather than trusted from start-up. A dispatch gets slower as the
                // accelerator heats and drops clocks — which is precisely what this run is causing — and a
                // limiter still holding the cold figure would size every idle against work that no longer
                // fits inside it.
                if (dispatches > 0)
                {
                    dispatchCost += ((dispatched / dispatches) - dispatchCost) * DispatchSmoothing;
                    limiter.MinimumBurn = dispatchCost;
                    RecordDispatchCost(deviceIndex, dispatchCost);
                }

                // What the sleep ACTUALLY cost, which is the only figure the limiter can learn from.
                limiter.Record(dispatched, session.Elapsed - beforeSleep, ramp.CurrentFraction);

                lock (_stateLock)
                {
                    if (deviceIndex < _observedByDevice.Length && limiter.HasMeasurement)
                    {
                        _observedByDevice[deviceIndex] = limiter.ObservedFraction;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "GPU load on {Device} stopped early because the accelerator faulted.", device.Name);
        }
    }

    /// <summary>
    /// Finds how much arithmetic per element makes one dispatch last about
    /// <see cref="TargetDispatchDuration"/>, and reports what a dispatch at that size actually costs.
    /// </summary>
    /// <remarks>
    /// The first dispatch is timed and thrown away. It pays for compiling the kernel and for the
    /// accelerator's clocks coming up off idle, so sizing the workload against it would fit the run to a
    /// machine that stops existing a second later — and always in the direction of too little work.
    /// </remarks>
    private static (int Iterations, TimeSpan DispatchCost) CalibrateDispatch(
        Action<Index1D, ArrayView<float>, int> kernel,
        ArrayView<float> view,
        int length,
        Accelerator accelerator,
        CancellationToken cancellationToken)
    {
        TimeSpan TimeDispatches(int iterations, int repeats)
        {
            var clock = Stopwatch.StartNew();
            var completed = 0;

            for (var i = 0; i < repeats && !cancellationToken.IsCancellationRequested; i++)
            {
                kernel(length, view, iterations);
                accelerator.Synchronize();
                completed++;
            }

            // Averaged over what actually ran, not over what was asked for: a cancelled probe would otherwise
            // divide a partial elapsed time by the full count and report a dispatch far cheaper than it is.
            return completed == 0 ? TimeSpan.Zero : clock.Elapsed / completed;
        }

        TimeDispatches(ProbeIterations, 1);

        var low = TimeDispatches(ProbeIterations, ProbeDispatches);
        if (low <= TimeSpan.Zero)
        {
            // Either cancelled, or a clock too coarse to see a dispatch at the probe size. Both mean the
            // measurement is unusable, and the largest workload is the safe answer — too MUCH work per
            // dispatch costs resolution, while too little would spend the run in launch overhead.
            return (MaximumIterations, TargetDispatchDuration);
        }

        // A first estimate that assumes cost is proportional to work. It is not — every dispatch pays a fixed
        // launch-and-synchronise toll on top — so this always lands SHORT, by charging the toll again for
        // every iteration it adds. Close enough to place a second probe usefully far from the first.
        var firstGuess = (int)Math.Clamp(
            ProbeIterations * (TargetDispatchDuration / low),
            MinimumIterations,
            MaximumIterations);

        var high = TimeDispatches(firstGuess, ProbeDispatches);
        var iterations = firstGuess;

        // Two points and a straight line separate the toll from the per-iteration cost, which one ratio can
        // only fold together.
        if (firstGuess != ProbeIterations && high > low)
        {
            var perIteration = (high - low) / (firstGuess - ProbeIterations);
            var overhead = low - (perIteration * ProbeIterations);

            if (perIteration > TimeSpan.Zero && TargetDispatchDuration > overhead)
            {
                iterations = (int)Math.Clamp(
                    (TargetDispatchDuration - overhead) / perIteration,
                    MinimumIterations,
                    MaximumIterations);
            }
        }

        var measured = iterations == firstGuess ? high : TimeDispatches(iterations, ProbeDispatches);

        return (iterations, measured > TimeSpan.Zero ? measured : TargetDispatchDuration);
    }

    private void RecordDispatchCost(int deviceIndex, TimeSpan cost)
    {
        lock (_stateLock)
        {
            if (deviceIndex < _dispatchByDevice.Length)
            {
                _dispatchByDevice[deviceIndex] = cost;
            }
        }
    }

    /// <summary>
    /// The workload itself: dependent floating-point arithmetic, so it cannot be optimised away.
    /// </summary>
    /// <remarks>
    /// Deliberately plain arithmetic rather than a matrix-multiply hammering the tensor paths. The point is
    /// to reproduce the kind of sustained load a game or a compute job produces, because that is what the
    /// resulting model has to serve — a workload that trips a different power limit would calibrate the fan
    /// for a machine state nothing else creates.
    /// </remarks>
    private static void BurnKernel(Index1D index, ArrayView<float> data, int iterations)
    {
        // Seeded from the index rather than carried over from the previous dispatch. Accumulating across
        // dispatches would run away to infinity after a few minutes, and arithmetic on infinity keeps the
        // lanes busy while measuring nothing.
        var value = (index * 1e-6f) + 1f;

        for (var i = 0; i < iterations; i++)
        {
            value = (value * 1.0000173f) + 1.0000019f;
        }

        // Stored so the chain has an observable result and cannot be optimised away.
        data[index] = value;
    }
}
