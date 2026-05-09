using DynamicData;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public interface IFanControlStateClient
{
    IObservable<IChangeSet<FanControlStateSnapshot, int>> WatchFanControlStates();
}