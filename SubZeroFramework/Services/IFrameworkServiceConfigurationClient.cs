namespace SubZeroFramework.Services;

public interface IFrameworkServiceConfigurationClient
{
    Task<FrameworkServiceConfigurationSnapshot> GetConfigurationAsync(CancellationToken cancellationToken = default);

    IObservable<FrameworkServiceConfigurationSnapshot> WatchConfiguration();

    Task<FrameworkServiceConfigurationOperationResult> ApplyConfigurationAsync(FrameworkServiceConfigurationApplyRequest request, CancellationToken cancellationToken = default);

    Task<FrameworkServiceConfigurationOperationResult> SaveConfigurationAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceConfigurationOperationResult> LoadConfigurationAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceConfigurationOperationResult> RelocateConfigurationStoreAsync(string targetDirectory, CancellationToken cancellationToken = default);
}
