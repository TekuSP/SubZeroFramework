using DynamicData;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public interface IHardwareInfoClient
{
    Task<HardwareInfoSnapshot> GetHardwareInfoAsync(CancellationToken cancellationToken = default);

    IObservable<HardwareInfoSnapshot> WatchHardwareInfo();

    IObservable<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>> WatchHardwareInfoHistory(TimeSpan historyWindow);
}
