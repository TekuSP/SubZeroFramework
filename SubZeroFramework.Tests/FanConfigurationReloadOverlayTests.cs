using DynamicData;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the configuration-reload overlay against re-asserting persisted state over live fan state.
/// </summary>
/// <remarks>
/// The service watches the very file it writes, so EVERY persisting fan command — and a Settings save —
/// triggers a configuration reload, which re-ran the persisted overlay across ALL fans. Persisting a change
/// on one fan could therefore revert another fan that was mid-preview. Worse, when the clobbered fan's
/// persisted mode was Auto, the store reported Auto while the EC was still holding the preview duty, because
/// the overlay moves in-memory state without actuating anything.
///
/// These tests could not have been written before: <see cref="TestOptionsMonitor{TOptions}"/> previously
/// returned a no-op disposable from OnChange and never invoked listeners, so the reload path was unreachable
/// from tests.
/// </remarks>
[TestFixture]
public class FanConfigurationReloadOverlayTests
{
    [Test]
    public void ConfigurationReload_DoesNotRevertAFanWithAnOpenPreviewHold()
    {
        var provider = new StubFrameworkDataProvider();
        var options = new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            FanControlStates =
            [
                new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Auto },
                new FanControlStateOptions { FanIndex = 1, Mode = FanControlMode.Auto },
            ],
        });

        var watchdog = new FanPreviewWatchdog();
        using var store = CreateStore(provider, options, watchdog);

        provider.FanStateSource.AddOrUpdate(NewFanState(0));
        provider.FanStateSource.AddOrUpdate(NewFanState(1));

        // Fan 1 is being previewed: a hold is open and the live state is a volatile Manual override that is
        // deliberately NOT persisted.
        watchdog.Begin(1, store.GetState(1)!);
        store.MarkManual(1);
        Assert.That(store.GetState(1)!.Mode, Is.EqualTo(FanControlMode.Manual));

        // Applying anything on fan 0 rewrites the watched config file, which reloads configuration for ALL fans.
        options.RaiseChanged();

        Assert.That(
            store.GetState(1)!.Mode,
            Is.EqualTo(FanControlMode.Manual),
            "a fan under an open preview hold must keep its live preview state through a configuration reload");
    }

    [Test]
    public void ConfigurationReload_StillSeedsFansWithoutAPreviewHold()
    {
        // The guard must not disable the overlay generally — a fan that is not previewing still takes the
        // persisted configuration, which is how a restart restores a saved mode.
        var provider = new StubFrameworkDataProvider();
        var options = new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Auto }],
        });

        var watchdog = new FanPreviewWatchdog();
        using var store = CreateStore(provider, options, watchdog);
        provider.FanStateSource.AddOrUpdate(NewFanState(0));

        options.Set(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Max }],
        });

        Assert.That(store.GetState(0)!.Mode, Is.EqualTo(FanControlMode.Max));
    }

    [Test]
    public void ConfigurationReload_LeavesAPreviewingFanAloneEvenWhenTheConfigChanges()
    {
        // The guard must hold when the reload carries a genuinely different mode for the previewing fan —
        // this is the case that used to actuate: the store flipped to the persisted mode while the EC kept
        // holding the preview duty.
        var provider = new StubFrameworkDataProvider();
        var options = new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Auto }],
        });

        var watchdog = new FanPreviewWatchdog();
        using var store = CreateStore(provider, options, watchdog);
        provider.FanStateSource.AddOrUpdate(NewFanState(0));

        var holdToken = watchdog.Begin(0, store.GetState(0)!);
        store.MarkManual(0);

        options.Set(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.CustomCurve }],
        });

        Assert.That(store.GetState(0)!.Mode, Is.EqualTo(FanControlMode.Manual));

        // And once the preview ends, the fan is seeded normally again by the next reload.
        watchdog.TryTakeForRevert(0, holdToken, out _);
        options.RaiseChanged();

        Assert.That(store.GetState(0)!.Mode, Is.EqualTo(FanControlMode.CustomCurve));
    }

    [Test]
    public void ConfigurationReload_WithoutAWatchdog_StillApplies()
    {
        // The watchdog is optional on the store; a host that supplies none must keep the old seeding behaviour
        // rather than silently skipping every fan.
        var provider = new StubFrameworkDataProvider();
        var options = new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Auto }],
        });

        using var store = new FrameworkFanControlStateStore(
            provider,
            new FrameworkFanControlSafetyTracker(),
            options,
            NullLogger<FrameworkFanControlStateStore>.Instance);

        provider.FanStateSource.AddOrUpdate(NewFanState(0));

        options.Set(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Max }],
        });

        Assert.That(store.GetState(0)!.Mode, Is.EqualTo(FanControlMode.Max));
    }

    private static FrameworkFanControlStateStore CreateStore(
        StubFrameworkDataProvider provider,
        TestOptionsMonitor<FrameworkServiceOptions> options,
        FanPreviewWatchdog watchdog)
        => new(
            provider,
            new FrameworkFanControlSafetyTracker(),
            options,
            NullLogger<FrameworkFanControlStateStore>.Instance,
            watchdog);

    private static FanStateSnapshot NewFanState(int fanIndex) => new()
    {
        FanIndex = fanIndex,
        DisplayName = $"Fan {fanIndex}",
        FanState = default,
        ObservedAt = DateTimeOffset.UtcNow,
        IsAvailable = true,
    };
}
