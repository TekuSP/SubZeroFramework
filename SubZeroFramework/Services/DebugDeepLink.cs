#if DEBUG
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;
using SubZeroFramework.Services.Units;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Services;

/// <summary>
/// Debug-build-only startup navigation: pass a route as a command-line argument and the app opens there.
/// </summary>
/// <remarks>
/// <para>
/// Exists for design review. Reaching a calibration failure screen legitimately costs a ten-minute hot run
/// per attempt; reaching the blocked-on-battery state costs unplugging the machine. A route argument reaches
/// any of them in seconds, rendered by the PRODUCTION XAML rather than a mockup. The whole type is compiled
/// out of RELEASE — this is a workbench door, and shipping it would let a stray shortcut argument navigate
/// users somewhere they never asked to go.
/// </para>
/// <para>
/// Two grammars:
/// <list type="bullet">
/// <item><c>SubZeroFramework.exe Settings</c> — any registered route name (see <c>App.RegisterRoutes</c>):
/// <c>Dashboard</c>, <c>FanCurveProfiles</c>, <c>DeviceCapabilities</c>, <c>PowerTelemetry</c>,
/// <c>ThermalTelemetry</c>, <c>Modules</c>, <c>WarningIssues</c>, <c>Settings</c>, and their nested routes
/// like <c>FanCurveProfiles/Adaptive</c>.</item>
/// <item><c>SubZeroFramework.exe dialog/calibration/{state}</c> — opens the calibration wizard driven into a
/// state, with plausible fake data: <c>consent</c>, <c>blocked</c>, <c>running</c>, <c>success</c>,
/// <c>failure-load</c>, <c>failure-swing</c>, <c>failure-ceiling</c>, <c>failure-cancelled</c>,
/// <c>failure-disconnected</c>.</item>
/// </list>
/// </para>
/// <para>
/// The wizard itself deliberately stays a hand-shown <c>ContentDialog</c> rather than a navigation
/// <c>DialogViewMap</c>: its live flow is wired to the fan page — the gRPC client, the staging model, the
/// streamed progress, the live power push — none of which a navigation-constructed instance would have.
/// Making it navigable would mean re-plumbing a working flow so that a DEBUG tool could reach it; driving
/// the same dialog with a fake model reaches every state without touching the flow at all.
/// </para>
/// </remarks>
internal static class DebugDeepLink
{
    /// <summary>Handles a route argument if one was passed; quietly does nothing otherwise.</summary>
    public static async Task TryHandleAsync(Window window, IServiceProvider services)
    {
        var route = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(static argument => !argument.StartsWith('-'));

        if (string.IsNullOrWhiteSpace(route) || window.Content is not FrameworkElement root)
        {
            return;
        }

        // The shell's own first navigation is still settling when OnLaunched returns; navigating into a
        // nested region before it attaches silently does nothing. A beat of delay is crude and entirely
        // adequate for a debug-only door.
        await Task.Delay(TimeSpan.FromMilliseconds(600)).ConfigureAwait(true);

        if (route.StartsWith("dialog/calibration/", StringComparison.OrdinalIgnoreCase))
        {
            await ShowCalibrationStateAsync(
                root,
                route["dialog/calibration/".Length..],
                services.GetRequiredService<IUnitFormattingService>()).ConfigureAwait(true);
            return;
        }

        if (root.Navigator() is { } navigator)
        {
            await navigator.NavigateRouteAsync(root, route).ConfigureAwait(true);
        }
    }

    /// <summary>Opens the real calibration dialog, driven into the named state with plausible fake data.</summary>
    private static async Task ShowCalibrationStateAsync(FrameworkElement root, string state, IUnitFormattingService units)
    {
        var model = new FanCalibrationDialogModel(
            "Left fan",
            FanCoolingRole.Cpu,
            units,
            [
                new SensorChipModel(0, "Mainboard", units),
                new SensorChipModel(1, "CPU", units),
                new SensorChipModel(3, "APU / SoC", units),
            ],
            selectedSensorIndices: [0, 1, 3]);

        model.PowerReadyText = "Running on AC power — ready to start.";

        switch (state.ToLowerInvariant())
        {
            case "consent":
                break;

            case "blocked":
                model.IsOnBattery = true;
                model.BatteryChargePercent = 76d;
                break;

            case "running":
                model.BeginRun();
                ApplyFakeRun(model);
                break;

            case "success":
                model.BeginRun();
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = 0,
                    Succeeded = true,
                    StoppedAt = FanCalibrationStep.Completed,
                    Duration = TimeSpan.FromMinutes(5.2),
                    FansRestored = true,
                    Calibration = FanCalibrationSnapshot.Bootstrap with
                    {
                        State = FanCalibrationState.Ok,
                        CalibratedAt = DateTimeOffset.UtcNow,
                        ProcessGainCelsiusPerPercent = 0.42d,
                        TimeConstantSeconds = 26d,
                        DeadTimeSeconds = 4d,
                        MinimumSpinRpm = 1180d,
                        MinimumSpinDutyPercent = 17d,
                        MaximumRpm = 6100d,
                    },
                });
                break;

            case "failure-load":
                model.BeginRun();
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = 0,
                    Succeeded = false,
                    Failure = FanCalibrationFailure.InsufficientLoad,
                    StoppedAt = FanCalibrationStep.LoadingAndSettling,
                    AveragePackagePowerWatts = 6.4d,
                    Duration = TimeSpan.FromSeconds(161),
                    FansRestored = true,
                });
                break;

            case "failure-swing":
                model.BeginRun();
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = 0,
                    Succeeded = false,
                    Failure = FanCalibrationFailure.InsufficientTemperatureSwing,
                    StoppedAt = FanCalibrationStep.FittingModel,
                    TemperatureSwingCelsius = 2.1d,
                    PeakTemperatureCelsius = 85d,
                    Duration = TimeSpan.FromMinutes(4.1),
                    FansRestored = true,
                });
                break;

            case "failure-ceiling":
                model.BeginRun();
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = 0,
                    Succeeded = false,
                    Failure = FanCalibrationFailure.TemperatureCeiling,
                    StoppedAt = FanCalibrationStep.SteppingFan,
                    PeakTemperatureCelsius = 97d,
                    Duration = TimeSpan.FromMinutes(2.6),
                    FansRestored = true,
                });
                break;

            case "failure-cancelled":
                model.BeginRun();
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = 0,
                    Succeeded = false,
                    Failure = FanCalibrationFailure.Cancelled,
                    StoppedAt = FanCalibrationStep.LoadingAndSettling,
                    Duration = TimeSpan.FromMinutes(1.4),
                    FansRestored = true,
                });
                break;

            case "failure-disconnected":
                model.BeginRun();
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = 0,
                    Succeeded = false,
                    Failure = FanCalibrationFailure.ClientDisconnected,
                    StoppedAt = FanCalibrationStep.MeasuringResponse,
                    Duration = TimeSpan.FromMinutes(3.2),
                    FansRestored = true,
                });
                break;

            default:
                return;
        }

        using var dialog = new FanCalibrationDialog(model) { XamlRoot = root.XamlRoot };

        // The running state's close is a cancel, exactly as in production — the dialog vetoes closing while
        // Running, so without this the review dialog could never be dismissed from that state.
        dialog.CancellationToken.Register(() => root.DispatcherQueue.TryEnqueue(() =>
        {
            if (model.Stage == FanCalibrationStage.Running)
            {
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = 0,
                    Succeeded = false,
                    Failure = FanCalibrationFailure.Cancelled,
                    StoppedAt = FanCalibrationStep.MeasuringResponse,
                    FansRestored = true,
                });
            }
        }));

        await dialog.ShowAsync();
    }

    /// <summary>
    /// A plausible run: warm-up climb, a marked step, the fall — so every chart has a shape worth reviewing.
    /// </summary>
    private static void ApplyFakeRun(FanCalibrationDialogModel model)
    {
        const int stepAtSeconds = 150;

        for (var second = 0; second <= 240; second += 2)
        {
            var beforeStep = second < stepAtSeconds;

            // First-order rise toward 84 °C before the step, fall toward 76 °C after it, with a wobble.
            var celsius = beforeStep
                ? 62d + (22d * (1d - Math.Exp(-second / 60d)))
                : 76d + (8d * Math.Exp(-(second - stepAtSeconds) / 40d));
            celsius += Math.Sin(second * 0.7d) * 0.8d;

            model.Apply(new FanCalibrationProgress
            {
                FanIndex = 0,
                Step = beforeStep ? FanCalibrationStep.LoadingAndSettling : FanCalibrationStep.MeasuringResponse,
                ElapsedSeconds = second,
                OverallProgress = Math.Min(0.75d, second / 320d),
                EstimatedRemaining = TimeSpan.FromSeconds(300 - second),
                TemperatureCelsius = celsius,
                SpeedRpm = beforeStep ? 1860d : 6140d,
                DutyPercent = beforeStep ? 22d : 100d,
                PackagePowerWatts = 48d + (Math.Sin(second * 0.3d) * 3d),
                ClockMegahertz = 3700d + (Math.Sin(second * 0.9d) * 250d),
                UtilizationPercent = 92d + (Math.Sin(second * 1.3d) * 5d),
                IsStepMarker = second == stepAtSeconds,
            });
        }
    }
}
#endif
