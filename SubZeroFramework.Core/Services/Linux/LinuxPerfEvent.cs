using System.Globalization;
using System.Runtime.InteropServices;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// A single open perf counter, plus the discovery of the PMU that owns it.
/// </summary>
/// <remarks>
/// Only what the GPU PMUs need: a counting (non-sampling) system-wide event, read as a raw pair of
/// (value, time_enabled). Intentionally minimal — this is not a general perf binding.
/// </remarks>
public sealed partial class LinuxPerfEvent : IDisposable
{
    /// <summary>Where the kernel advertises every registered PMU.</summary>
    public const string EventSourceRoot = "/sys/bus/event_source/devices";

    // perf_event_attr.read_format: also return how long the counter was enabled, which is the timebase a
    // busy-percentage needs and is more honest than measuring the interval ourselves.
    private const ulong PerfFormatTotalTimeEnabled = 1UL << 0;

    /// <summary>
    /// PERF_ATTR_SIZE_VER0. Every field this code sets lives inside the original 64-byte struct, and the
    /// kernel zero-fills the rest, so the oldest ABI version is also the most portable one to declare.
    /// </summary>
    private const uint PerfAttrSizeVer0 = 64;

    private int _fd = -1;

    private LinuxPerfEvent(int fd) => _fd = fd;

    public bool IsOpen => _fd >= 0;

    /// <summary>
    /// Opens a counting event on the given PMU type and config.
    /// </summary>
    /// <remarks>
    /// The GPU PMUs are system-wide: they reject a task-bound event (pid must be -1) and equally reject
    /// cpu == -1, so the event is pinned to a CPU. Kernels before 6.15 additionally require that CPU to be in
    /// the PMU's own cpumask and answer EINVAL otherwise, which is why the caller walks candidate CPUs.
    /// </remarks>
    public static LinuxPerfEvent? TryOpen(uint pmuType, ulong config, IReadOnlyList<int> candidateCpus)
    {
        // The one place an OS check genuinely belongs: everything else in the Linux readers is file I/O that
        // simply finds nothing elsewhere, but this reaches libc directly and would throw on any other system.
        if (pmuType == 0 || !OperatingSystem.IsLinux())
        {
            return null;
        }

        var attr = new PerfEventAttr
        {
            Type = pmuType,
            Size = PerfAttrSizeVer0,
            Config = config,
            // A sampling period is rejected outright by both i915 and xe; this is a pure counter.
            SamplePeriod = 0,
            SampleType = 0,
            ReadFormat = PerfFormatTotalTimeEnabled,
            // disabled = 0, so the counter runs from open and needs no enable ioctl.
            Flags = 0,
        };

        try
        {
            foreach (var cpu in candidateCpus)
            {
                var fd = PerfEventOpen(ref attr, pid: -1, cpu: cpu, groupFd: -1, flags: 0);
                if (fd >= 0)
                {
                    return new LinuxPerfEvent(fd);
                }

                // EINVAL means "not this CPU" on kernels that enforce the PMU cpumask; every other error
                // (EACCES from perf_event_paranoid, EPERM from a container's seccomp policy, ENOENT for an
                // engine that does not exist) will not improve by trying another CPU.
                if (Marshal.GetLastPInvokeError() != 22)
                {
                    return null;
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        return null;
    }

    /// <summary>Reads the counter and the time it has been enabled, both monotonic since open.</summary>
    public bool TryRead(out ulong value, out ulong timeEnabled)
    {
        value = 0;
        timeEnabled = 0;

        if (_fd < 0)
        {
            return false;
        }

        Span<ulong> buffer = stackalloc ulong[2];
        int read;
        unsafe
        {
            fixed (ulong* pointer = buffer)
            {
                read = (int)Read(_fd, pointer, (nuint)(sizeof(ulong) * 2));
            }
        }

        if (read != sizeof(ulong) * 2)
        {
            // A short read means the counter went away (driver unbind, hot-unplug); the caller re-probes.
            return false;
        }

        value = buffer[0];
        timeEnabled = buffer[1];
        return true;
    }

    public void Dispose()
    {
        if (_fd >= 0)
        {
            _ = Close(_fd);
            _fd = -1;
        }
    }

    // ----- PMU discovery helpers -----

    /// <summary>Every registered PMU directory whose name starts with the given prefix.</summary>
    public static IReadOnlyList<string> FindPmuDirectories(string namePrefix, string eventSourceRoot = EventSourceRoot)
    {
        try
        {
            if (!Directory.Exists(eventSourceRoot))
            {
                return [];
            }

            List<string> matches = [];
            foreach (var directory in Directory.EnumerateDirectories(eventSourceRoot))
            {
                if (Path.GetFileName(directory).StartsWith(namePrefix, StringComparison.Ordinal))
                {
                    matches.Add(directory);
                }
            }

            matches.Sort(StringComparer.Ordinal);
            return matches;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Reads a PMU's dynamically assigned type id. Zero means "no usable PMU here".</summary>
    public static uint ReadPmuType(string pmuDirectory)
    {
        var text = DrmSysfs.ReadAttribute(Path.Combine(pmuDirectory, "type"));
        return text is not null && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type)
            ? type
            : 0;
    }

    /// <summary>
    /// CPUs an event on this PMU may be opened against, most-preferred first.
    /// </summary>
    /// <remarks>
    /// The PMU's own cpumask is authoritative on kernels that enforce it; CPU 0 is appended as a fallback for
    /// the case where the file is missing or unparseable.
    /// </remarks>
    public static IReadOnlyList<int> ReadCandidateCpus(string pmuDirectory)
    {
        List<int> cpus = [];

        var text = DrmSysfs.ReadAttribute(Path.Combine(pmuDirectory, "cpumask"));
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Format is a cpu list: "0" or "0-3" or "0,4-7".
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var dash = part.IndexOf('-');
                if (dash < 0)
                {
                    if (int.TryParse(part, out var single))
                    {
                        cpus.Add(single);
                    }

                    continue;
                }

                if (int.TryParse(part.AsSpan(..dash), out var start) && int.TryParse(part.AsSpan(dash + 1), out var end))
                {
                    // Only the first of a range is ever needed; the whole range would be redundant opens.
                    for (var cpu = start; cpu <= end && cpu < start + 4; cpu++)
                    {
                        cpus.Add(cpu);
                    }
                }
            }
        }

        if (!cpus.Contains(0))
        {
            cpus.Add(0);
        }

        return cpus;
    }

    /// <summary>
    /// Parses a PMU <c>events/</c> entry, which holds a term list such as <c>config=0x2000</c> (i915) or
    /// <c>event=0x02</c> (xe).
    /// </summary>
    public static ulong? ParseEventConfig(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        ulong config = 0;
        var found = false;

        foreach (var term in text.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = term.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = term[..separator].Trim();
            // Only the primary config word matters here; config1/config2 are unused by these PMUs.
            if (key is not ("config" or "event"))
            {
                continue;
            }

            var value = term[(separator + 1)..].Trim();
            var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (isHex)
            {
                value = value[2..];
            }

            if (ulong.TryParse(
                    value,
                    isHex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                config = parsed;
                found = true;
            }
        }

        return found ? config : null;
    }

    /// <summary>
    /// Reads the low bit position out of a <c>format/</c> entry such as <c>config:20-27</c>, which is the
    /// shift needed to place a field into the event config.
    /// </summary>
    public static int? ParseFormatShift(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var span = text.Trim().AsSpan();
        var colon = span.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        var bits = span[(colon + 1)..];
        var dash = bits.IndexOf('-');
        if (dash >= 0)
        {
            bits = bits[..dash];
        }

        return int.TryParse(bits, out var shift) ? shift : null;
    }

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long Syscall(long number, ref PerfEventAttr attr, int pid, int cpu, int groupFd, ulong flags);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint Read(int fd, void* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fd);

    private static int PerfEventOpen(ref PerfEventAttr attr, int pid, int cpu, int groupFd, ulong flags)
    {
        // __NR_perf_event_open differs per architecture and has no libc wrapper.
        var syscallNumber = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => 298L,
            Architecture.Arm64 => 241L,
            _ => -1L,
        };

        if (syscallNumber < 0)
        {
            return -1;
        }

        return (int)Syscall(syscallNumber, ref attr, pid, cpu, groupFd, flags);
    }

    /// <summary>
    /// perf_event_attr truncated to PERF_ATTR_SIZE_VER0 — see <see cref="PerfAttrSizeVer0"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct PerfEventAttr
    {
        public uint Type;
        public uint Size;
        public ulong Config;
        public ulong SamplePeriod;
        public ulong SampleType;
        public ulong ReadFormat;

        /// <summary>Bitfield: bit 0 is <c>disabled</c>, bit 1 <c>inherit</c>, and so on. Left at zero.</summary>
        public ulong Flags;

        public uint WakeupEvents;
        public uint BreakpointType;
        public ulong Config1;
    }
}
