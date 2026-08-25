namespace SubZeroFramework.Service.Models;

/// <summary>
/// The outcome of a fan-control store command that can fail for a reason the USER can act on.
/// </summary>
/// <remarks>
/// Distinct from throwing, deliberately. "This fan has not been calibrated" is an expected state with an
/// obvious next step, not an exceptional one — the UI turns it into a Calibrate call to action. Reserving
/// exceptions for genuine faults keeps the two apart at the gRPC boundary, where an exception becomes an
/// error status and a failed result becomes an ordinary reply carrying a message.
/// </remarks>
/// <param name="Succeeded">Whether the command took effect.</param>
/// <param name="Message">A user-facing explanation when it did not; empty on success.</param>
public readonly record struct FanControlStoreResult(bool Succeeded, string Message)
{
    /// <summary>The command took effect.</summary>
    public static FanControlStoreResult Ok { get; } = new(true, string.Empty);

    /// <summary>The command was refused for a reason worth showing the user.</summary>
    /// <param name="message">The explanation.</param>
    public static FanControlStoreResult Failed(string message) => new(false, message);
}
