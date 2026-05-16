using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public interface IHardwareInfoClient
{
    Task<HardwareInfoSnapshot> GetHardwareInfoAsync(CancellationToken cancellationToken = default);

    IObservable<HardwareInfoSnapshot> WatchHardwareInfo();
}
