namespace SubZeroFramework.Models;

public sealed record FanControlSafetyStateSnapshot
{
    public bool HasActiveOverride { get; init; }

    public bool LastAutoRestoreAttemptFailed { get; init; }

    public DateTimeOffset? LastAutoRestoreAttemptAt { get; init; }

    public string? LastAutoRestoreError { get; init; }
}