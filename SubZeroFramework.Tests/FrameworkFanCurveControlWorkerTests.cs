using FrameworkDotnet.Enums;
using FrameworkDotnet.Snapshots;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Service.Services.Hosting;
using SubZeroFramework.Services;

using UnitsNet;

namespace SubZeroFramework.Tests;

/// <summary>
/// End-to-end tests for the curve worker's actuation decisions, driven through the real state store and a
/// stub EC.
/// </summary>
/// <remarks>
/// These run the worker as a hosted service rather than calling its evaluation directly: the decision under
/// test depends on state that only exists once the store subscription has delivered a change set, so testing
/// the private method in isolation would not exercise the path that actually runs.
///
/// The worker is constructed with a short evaluation interval so a test drives evaluations by pushing thermal
/// snapshots instead of waiting out the production one-second sampling window.
/// </remarks>
[TestFixture]
public class FrameworkFanCurveControlWorkerTests
{
    private static readonly TimeSpan TestEvaluationInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>Evaluation rounds a positive assertion is given before it is treated as a failure.</summary>
    private const int SettleAttempts = 12;

    /// <summary>
    /// The safety-critical case: a fan this worker drove is switched back to Auto, and the EC override must
    /// actually be released. Without the restore the store, the streams and the UI all report Auto while the
    /// fan stays pinned at whatever duty was last written — possibly a low one, with no thermal protection.
    /// </summary>
    [Test]
    public async Task Evaluate_WhenDrivenFanSwitchesToAuto_RestoresAutomaticFanControl()
    {
        using var harness = new WorkerHarness();

        // Drive fan 0 at a fixed duty so the worker records it as one of ours.
        harness.Store.MarkManual(0);
        harness.Store.RecordAppliedDuty(0, 60d);
        await harness.EvaluateAsync(() => harness.Provider.SetFanDutyCalls.Count > 0);

        Assert.That(harness.Provider.SetFanDutyCalls, Is.Not.Empty, "The manual fan should have been actuated first.");

        // Hand it back. The next evaluation sees NotDriven for a fan we had driven.
        harness.Provider.RestoreAutoCalls.Clear();
        harness.Store.MarkAuto(0);
        await harness.EvaluateAsync(() => harness.Provider.RestoreAutoCalls.Count > 0);

        Assert.That(harness.Provider.RestoreAutoCalls, Does.Contain(0), "Switching a driven fan to Auto must release the EC override.");
    }

    /// <summary>
    /// The restore must be issued once per episode, not on every evaluation — re-issuing it every second
    /// would write the EC continuously for a fan nobody is driving.
    /// </summary>
    [Test]
    public async Task Evaluate_WhenFanStaysOnAuto_DoesNotRestoreRepeatedly()
    {
        using var harness = new WorkerHarness();

        harness.Store.MarkManual(0);
        harness.Store.RecordAppliedDuty(0, 60d);
        await harness.EvaluateAsync(() => harness.Provider.SetFanDutyCalls.Count > 0);

        harness.Provider.RestoreAutoCalls.Clear();
        harness.Store.MarkAuto(0);

        // No early exit: every remaining round is an opportunity to wrongly re-issue the restore.
        await harness.EvaluateAsync();

        Assert.That(harness.Provider.RestoreAutoCalls, Has.Count.EqualTo(1), "The restore should be issued once, not on every pass.");
    }

    /// <summary>
    /// A restore that fails must be retried, not dropped. This is the difference between a fan recovering on
    /// the next pass and a fan stranded at whatever duty was last written to it — with the store, the streams
    /// and the UI all reporting Auto, so nothing anywhere would show the machine was still being driven.
    /// </summary>
    [Test]
    public async Task Evaluate_WhenRestoreFails_RetriesOnTheNextPass()
    {
        using var harness = new WorkerHarness();

        harness.Store.MarkManual(0);
        harness.Store.RecordAppliedDuty(0, 60d);
        await harness.EvaluateAsync(() => harness.Provider.SetFanDutyCalls.Count > 0);

        // Fail the first restore attempt only; the retry on a later pass should succeed.
        harness.Provider.RestoreAutoCalls.Clear();
        harness.Provider.RestoreAutoFailuresRemaining = 1;
        harness.Store.MarkAuto(0);
        await harness.EvaluateAsync(() => harness.Provider.RestoreAutoCalls.Count >= 2);

        Assert.That(
            harness.Provider.RestoreAutoCalls,
            Has.Count.GreaterThanOrEqualTo(2),
            "A failed restore must be retried rather than leaving the fan stranded on the applied duty.");
    }

    /// <summary>
    /// A fan that was never driven by this worker must not be touched when it reports Auto. Issuing a restore
    /// for it would be this process reaching for a fan it does not own.
    /// </summary>
    [Test]
    public async Task Evaluate_WhenUndrivenFanIsAuto_DoesNotTouchTheEc()
    {
        using var harness = new WorkerHarness();

        harness.Store.MarkAuto(0);
        await harness.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Provider.RestoreAutoCalls, Is.Empty, "A fan we never drove must not be restored.");
            Assert.That(harness.Provider.SetFanDutyCalls, Is.Empty, "An Auto fan must not be actuated.");
        });
    }

    /// <summary>
    /// Max mode is re-asserted after a restart, so it must actuate at 100% rather than being left to the EC.
    /// </summary>
    [Test]
    public async Task Evaluate_WhenFanIsMax_AppliesFullDuty()
    {
        using var harness = new WorkerHarness();

        harness.Store.MarkMax(0);
        await harness.EvaluateAsync(() => harness.Provider.SetFanDutyCalls.Count > 0);

        Assert.That(harness.Provider.SetFanDutyCalls, Is.Not.Empty);
        Assert.That(harness.Provider.SetFanDutyCalls[^1].DutyPercent, Is.EqualTo(100d));
    }

    /// <summary>
    /// Commands disabled means never actuate, even for a persisted override restored at startup.
    /// </summary>
    [Test]
    public async Task Evaluate_WhenFanControlIsDisabled_DoesNotActuate()
    {
        using var harness = new WorkerHarness(allowFanControlCommands: false);

        harness.Store.MarkMax(0);
        await harness.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Provider.SetFanDutyCalls, Is.Empty);
            Assert.That(harness.Provider.RestoreAutoCalls, Is.Empty);
        });
    }

    /// <summary>
    /// Wires the worker to a real state store and a stub EC, and starts it as the host would.
    /// </summary>
    private sealed class WorkerHarness : IDisposable
    {
        private readonly FrameworkFanCurveControlWorker _worker;
        private readonly FrameworkShutdownCoordinator _shutdownCoordinator;
        private readonly TestHostApplicationLifetime _lifetime = new();

        public WorkerHarness(bool allowFanControlCommands = true)
        {
            Provider = new StubFrameworkDataProvider();

            var options = new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
            {
                AllowFanControlCommands = allowFanControlCommands,
            });

            Store = new FrameworkFanControlStateStore(
                Provider,
                new FrameworkFanControlSafetyTracker(),
                options,
                NullLogger<FrameworkFanControlStateStore>.Instance);

            _shutdownCoordinator = new FrameworkShutdownCoordinator(Provider, NullLogger<FrameworkShutdownCoordinator>.Instance);

            _worker = new FrameworkFanCurveControlWorker(
                Provider,
                Store,
                new FrameworkFanControlAuthorizationService(options, NullLogger<FrameworkFanControlAuthorizationService>.Instance),
                new FrameworkFatalExitHandler(_shutdownCoordinator, _lifetime, NullLogger<FrameworkFatalExitHandler>.Instance),
                _lifetime,
                NullLogger<FrameworkFanCurveControlWorker>.Instance,
                TestEvaluationInterval);

            _worker.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public StubFrameworkDataProvider Provider { get; }

        public FrameworkFanControlStateStore Store { get; }

        /// <summary>
        /// Drives evaluations until <paramref name="until"/> holds, or for the full settle window when no
        /// condition is given (which is how a test asserts that nothing happened).
        /// </summary>
        /// <remarks>
        /// Pumping rather than sleeping a fixed amount is deliberate. Two things have to line up before an
        /// evaluation can act: the store's change set has to reach the worker's control-state mirror, and a
        /// thermal snapshot has to survive the sampling window. Both are asynchronous, so a single push and a
        /// fixed delay is a race — it passes or fails on machine load rather than on behavior.
        /// </remarks>
        public async Task EvaluateAsync(Func<bool>? until = null)
        {
            for (var attempt = 0; attempt < SettleAttempts; attempt++)
            {
                Provider.ThermalSource.OnNext(CreateThermalSnapshot());
                await Task.Delay(TestEvaluationInterval * 3);

                if (until?.Invoke() == true)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// A snapshot with every sensor reporting a benign temperature. These tests are about the Manual /
        /// Max / Auto decisions, which do not consult the curve, so the readings only need to be valid.
        /// </summary>
        private static FrameworkThermalSnapshot CreateThermalSnapshot()
        {
            var temperature = new FrameworkTemperatureSnapshot(
                FrameworkTemperatureState.Ok,
                Temperature.FromDegreesCelsius(50d),
                FrameworkSensorName.Unknown);

            var fan = new FrameworkFanSnapshot(
                FrameworkFanState.Ok,
                RotationalSpeed.FromRevolutionsPerMinute(2000d),
                FrameworkFanName.Unknown);

            return new FrameworkThermalSnapshot(
                fanCount: 2,
                sensorCount: 8,
                temperature, temperature, temperature, temperature,
                temperature, temperature, temperature, temperature,
                fan, fan, fan, fan);
        }

        public void Dispose()
        {
            _worker.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            _worker.Dispose();
            _shutdownCoordinator.Dispose();
            Store.Dispose();
            _lifetime.Dispose();
        }
    }
}
