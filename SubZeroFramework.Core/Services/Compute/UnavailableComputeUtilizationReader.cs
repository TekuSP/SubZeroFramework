using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// The reader used where no source exists yet — Linux until its per-vendor readers land, and any platform
/// whose sources are all absent.
/// </summary>
/// <remarks>
/// Exists so "we cannot read this" is an ordinary, silent state rather than a special case the telemetry loop
/// has to test for. It reports nothing, which the publisher turns into unavailable channels and the UI omits.
/// </remarks>
public sealed class UnavailableComputeUtilizationReader : IComputeUtilizationReader
{
    public static readonly UnavailableComputeUtilizationReader Instance = new();

    public bool IsAvailable => false;

    public IReadOnlyList<ComputeDeviceUtilization> Sample() => [];

    public void Dispose()
    {
        // Nothing held.
    }
}

/// <summary>Identity resolver for platforms with no enumeration implemented. Names nothing.</summary>
public sealed class UnavailableComputeDeviceIdentityResolver : IComputeDeviceIdentityResolver
{
    public static readonly UnavailableComputeDeviceIdentityResolver Instance = new();

    public IReadOnlyList<ComputeDeviceIdentity> Enumerate() => [];
}
