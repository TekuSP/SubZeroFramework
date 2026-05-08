using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public interface IFrameworkStatusClient
{
    Task<FrameworkSystemStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    IObservable<FrameworkSystemStatus> WatchStatus();

    DateTimeOffset? LastObservedAt { get; }

    FrameworkGrpcEndpointValidationResult EndpointValidation { get; }
}
