using DynamicData;

namespace SubZeroFramework.Services;

public interface IFanControlStateClient
{
    IObservable<IChangeSet<FanControlStateSnapshot, int>> WatchFanControlStates();
}