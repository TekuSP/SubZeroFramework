using System.Globalization;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// The device identity carried inside a Windows <c>GPU Engine</c> performance-counter instance name.
/// </summary>
/// <param name="Luid">
/// The adapter LUID folded into one 64-bit value, high part first — the same shape
/// <c>DEVPKEY_Gpu_Luid</c> reports, so the two can be compared directly. SESSION-SCOPED: Windows regenerates
/// LUIDs across reboots.
/// </param>
/// <param name="PhysicalIndex">The physical adapter index that pairs with <paramref name="Luid"/>.</param>
/// <param name="EngineType">
/// The engine label exactly as the driver spelled it. Casing, spaces and trailing ordinals are all
/// driver-chosen — <c>3D</c>, <c>3d</c>, <c>video codec engine</c>, <c>JPEG_Decode_0</c> and <c>compute 0</c>
/// have all been observed on one machine — so compare it case-insensitively and never key logic on its exact
/// text beyond a prefix.
/// </param>
public readonly record struct GpuEngineInstance(long Luid, int PhysicalIndex, string EngineType);

/// <summary>
/// Parses <c>GPU Engine</c> counter instance names, e.g.
/// <c>pid_2908_luid_0x00000000_0x00018A19_phys_0_eng_0_engtype_3D</c>.
/// </summary>
/// <remarks>
/// Microsoft publishes neither the counter set nor this name layout, so a future Windows build is free to
/// change it. Everything here is therefore a probe: an unexpected shape yields <see langword="false"/> and the
/// caller drops that instance, rather than an exception climbing out of a telemetry tick.
/// </remarks>
public static class GpuEngineInstanceName
{
    private const string LuidToken = "luid_";
    private const string PhysicalToken = "phys_";
    private const string EngineTypeToken = "engtype_";

    /// <summary>
    /// Extracts the adapter and engine identity from one counter instance name.
    /// </summary>
    /// <returns><see langword="false"/> for any name that does not carry all three fields.</returns>
    public static bool TryParse(ReadOnlySpan<char> instanceName, out GpuEngineInstance instance)
    {
        instance = default;

        // Every token is matched case-insensitively: the same driver stack spells these lowercase on one
        // Windows build and mixed-case on another, and both spellings have been seen on this hardware.
        var luidStart = instanceName.IndexOf(LuidToken, StringComparison.OrdinalIgnoreCase);
        if (luidStart < 0)
        {
            return false;
        }

        var cursor = instanceName[(luidStart + LuidToken.Length)..];

        // The LUID is printed as its two halves — high part then low part — which recombine into the single
        // INT64 the device property store reports.
        if (!TryTakeHexHalf(ref cursor, out var highPart) || !TryTakeHexHalf(ref cursor, out var lowPart))
        {
            return false;
        }

        var physicalStart = cursor.IndexOf(PhysicalToken, StringComparison.OrdinalIgnoreCase);
        if (physicalStart < 0)
        {
            return false;
        }

        cursor = cursor[(physicalStart + PhysicalToken.Length)..];
        if (!TryTakePhysicalIndex(ref cursor, out var physicalIndex))
        {
            return false;
        }

        // The engine ordinal between phys and engtype is deliberately skipped rather than required: it is of
        // no use here, and tolerating its absence costs nothing.
        var engineTypeStart = cursor.IndexOf(EngineTypeToken, StringComparison.OrdinalIgnoreCase);
        if (engineTypeStart < 0)
        {
            return false;
        }

        // Everything after the marker is the engine type, underscores, spaces and trailing ordinals included.
        var engineType = cursor[(engineTypeStart + EngineTypeToken.Length)..];
        if (engineType.IsEmpty)
        {
            return false;
        }

        instance = new GpuEngineInstance(((long)highPart << 32) | lowPart, physicalIndex, engineType.ToString());
        return true;
    }

    private static bool TryTakeHexHalf(ref ReadOnlySpan<char> cursor, out uint value)
    {
        value = 0;

        // Both halves are followed by another token, so a missing separator means the name is truncated.
        var end = cursor.IndexOf('_');
        if (end < 0)
        {
            return false;
        }

        var token = cursor[..end];
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }

        // AllowHexSpecifier alone, not NumberStyles.HexNumber, so surrounding whitespace is rejected instead
        // of quietly accepted.
        if (token.IsEmpty || !uint.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        cursor = cursor[(end + 1)..];
        return true;
    }

    private static bool TryTakePhysicalIndex(ref ReadOnlySpan<char> cursor, out int value)
    {
        value = 0;

        var end = cursor.IndexOf('_');
        if (end < 0)
        {
            return false;
        }

        // NumberStyles.None rejects a sign and any whitespace; a negative adapter index is not a thing.
        if (!int.TryParse(cursor[..end], NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        cursor = cursor[(end + 1)..];
        return true;
    }
}
