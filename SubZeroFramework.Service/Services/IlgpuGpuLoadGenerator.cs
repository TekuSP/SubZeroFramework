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
    /// Elements per dispatch. Large enough that a single launch keeps the GPU busy for a meaningful slice of
    /// time, so the dispatch loop is not spending its life in launch overhead.
    /// </summary>
    private const int ProblemSize = 1 << 22;

    /// <summary>Arithmetic iterations per element, which is what actually sets the power draw.</summary>
    private const int IterationsPerElement = 512;

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

            var session = Stopwatch.StartNew();

            // A dispatch is the smallest unit of work here — it cannot be cut short once launched — so the
            // minimum burn is however long one takes on this accelerator. Seeded, then measured below.
            var limiter = new AdaptiveDutyLimiter(TimeSpan.FromMilliseconds(1d));

            while (!cancellationToken.IsCancellationRequested)
            {
                var startedAt = session.Elapsed;
                var dispatched = TimeSpan.Zero;

                // Dispatch until the limiter's slice is filled. A single dispatch is atomic, so the slice is
                // approached in whole dispatches rather than interrupted part-way.
                do
                {
                    kernel((int)buffer.Length, buffer.View, IterationsPerElement);

                    // Synchronising each dispatch keeps the queue from growing without bound, and means
                    // cancellation takes effect within one dispatch rather than after everything queued.
                    accelerator.Synchronize();

                    dispatched = session.Elapsed - startedAt;
                }
                while (dispatched < limiter.BurnFor && !cancellationToken.IsCancellationRequested);

                var beforeSleep = session.Elapsed;

                if (limiter.SleepFor > TimeSpan.Zero)
                {
                    cancellationToken.WaitHandle.WaitOne(limiter.SleepFor);
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
