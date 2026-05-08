namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of validating the local gRPC endpoint path and metadata.
/// </summary>
public sealed record FrameworkGrpcEndpointValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the endpoint passed validation.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the normalized endpoint path.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Gets the validation message when validation failed, or an informational description when it succeeded.
    /// </summary>
    public required string Message { get; init; }
}
