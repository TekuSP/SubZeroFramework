namespace SubZeroFramework.Services;

public sealed record FrameworkServiceCommandResult
{
    public required string OperationName { get; init; }

    public required bool Succeeded { get; init; }

    public required FrameworkServiceCommandResultKind Kind { get; init; }

    public required string Message { get; init; }
}