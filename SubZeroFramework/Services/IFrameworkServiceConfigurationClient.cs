namespace SubZeroFramework.Services;

public interface IFrameworkServiceConfigurationClient
{
    Task<FrameworkServiceConfigurationSnapshot> GetConfigurationAsync(CancellationToken cancellationToken = default);

    IObservable<FrameworkServiceConfigurationSnapshot> WatchConfiguration();

    /// <summary>
    /// The background service's own log since it started (bounded — see
    /// <see cref="FrameworkServiceLogSnapshot.IsTruncated"/>), filtered to <paramref name="minimumLevel"/>.
    /// A snapshot on demand, not a live tail.
    /// </summary>
    Task<FrameworkServiceLogSnapshot> GetServiceLogsAsync(LogLevel minimumLevel, CancellationToken cancellationToken = default);

    Task<FrameworkServiceConfigurationOperationResult> ApplyConfigurationAsync(FrameworkServiceConfigurationApplyRequest request, CancellationToken cancellationToken = default);

    Task<FrameworkServiceConfigurationOperationResult> SaveConfigurationAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceConfigurationOperationResult> LoadConfigurationAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceConfigurationOperationResult> RelocateConfigurationStoreAsync(string targetDirectory, CancellationToken cancellationToken = default);
}
