namespace SubZeroFramework.Service.Services;

/// <summary>
/// Result of a <see cref="StorePathRelocator"/> relocation request.
/// <see cref="ActivePath"/> always reflects the path the store should use after the call,
/// regardless of whether the relocation succeeded or rolled back.
/// </summary>
public sealed record StoreRelocationResult(bool Succeeded, string Message, string ActivePath);
