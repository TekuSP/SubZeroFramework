namespace SubZeroFramework.Services;

public interface IFrameworkServiceControlClient
{
    FrameworkServiceControlInfo GetInfo();

    Task<FrameworkServiceCommandResult> ShutdownAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceCommandResult> RestartAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceCommandResult> EnableAutorunAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceCommandResult> DisableAutorunAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceCommandResult> InstallAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceCommandResult> UpdateAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceCommandResult> UninstallAsync(CancellationToken cancellationToken = default);

    Task<FrameworkServiceCommandResult> ReinstallAsync(CancellationToken cancellationToken = default);
}