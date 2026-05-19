namespace SubZeroFramework.Services;

public interface IFrameworkServiceConfigurationClient
{
    Task<FrameworkServiceConfigurationSnapshot> GetConfigurationAsync(CancellationToken cancellationToken = default);

    IObservable<FrameworkServiceConfigurationSnapshot> WatchConfiguration();

    Task<FrameworkServiceConfigurationUpdateResult> UpdateConfigurationAsync(FrameworkServiceConfigurationUpdateRequest request, CancellationToken cancellationToken = default);
}
