using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FrameworkDotnet.Enums;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

/// <summary>
/// The pack-health section: the battery's own registers, read on demand.
/// </summary>
/// <remarks>
/// <para>
/// Split into its own partial because it is the one part of this page driven by a button rather than a
/// stream. Everything else here is live telemetry; this costs many I2C round trips to the pack and is read
/// only when a person asks, so its loading and failure handling have nothing in common with the rest.
/// </para>
/// <para>
/// Answers the question the existing Battery card cannot: not "how full is it" but "how is it doing" — cell
/// balance, real age beside cycle count, and what the pack is asking the charger for versus what it is
/// getting.
/// </para>
/// </remarks>
public partial class PowerTelemetryModel
{
    /// <summary>Whether a pack read is in flight. Disables the button and shows the ring.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackHealthBusyVisibility))]
    [NotifyCanExecuteChangedFor(nameof(RefreshPackHealthCommand))]
    public partial bool IsReadingPackHealth { get; private set; }

    public Visibility PackHealthBusyVisibility => IsReadingPackHealth ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Whether a pack has been read successfully at least once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackHealthVisibility))]
    public partial bool HasPackHealth { get; private set; }

    public Visibility PackHealthVisibility => HasPackHealth ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shown instead of the numbers — an unreadable pack, or a sealed one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackHealthNoticeVisibility))]
    public partial string PackHealthNotice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity PackHealthNoticeSeverity { get; private set; } = InfoBarSeverity.Informational;

    public Visibility PackHealthNoticeVisibility => PackHealthNotice.Length > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    [ObservableProperty]
    public partial string PackIdentityText { get; private set; } = string.Empty;

    /// <summary>Age beside cycle count, because neither means much alone.</summary>
    [ObservableProperty]
    public partial string PackAgeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PackCellImbalanceText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackCellWarningVisibility))]
    public partial bool IsPackCellImbalanceHigh { get; private set; }

    public Visibility PackCellWarningVisibility => IsPackCellImbalanceHigh ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>One bar per cell, in the order the pack reports them.</summary>
    public ObservableCollection<PackCellReading> PackCells { get; } = [];

    /// <summary>What the pack is ASKING for, which is not always what it is being given.</summary>
    [ObservableProperty]
    public partial string PackRequestText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackStateOfHealthVisibility))]
    public partial string PackStateOfHealthText { get; private set; } = string.Empty;

    public Visibility PackStateOfHealthVisibility => PackStateOfHealthText.Length > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>
    /// Reads the pack once, the first time the section is opened.
    /// </summary>
    /// <remarks>
    /// Opening the section IS the request — it is the only reason to open it. Still not done on navigation:
    /// the read is slow and holds the I²C passthrough, and paying that for every visit to a page that is
    /// mostly about the live numbers above would spend the user's telemetry on a section they never looked
    /// at. Subsequent reads come from pulling the section down to refresh.
    /// </remarks>
    public void OnPackHealthExpanded()
    {
        if (HasPackHealth || IsReadingPackHealth)
        {
            return;
        }

        RefreshPackHealthCommand.Execute(parameter: null);
    }

    /// <summary>
    /// Reads the pack.
    /// </summary>
    /// <remarks>
    /// Driven by the section opening and by a pull-to-refresh. The service rate-limits repeats to one real
    /// read every fifteen seconds, so an impatient pull costs nothing.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRefreshPackHealth))]
    private async Task RefreshPackHealthAsync(CancellationToken cancellationToken)
    {
        IsReadingPackHealth = true;
        try
        {
            var pack = await _smartBatteryClient.ReadAsync(cancellationToken).ConfigureAwait(true);
            ApplyPackHealth(pack);
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-read is not a failure worth reporting.
        }
        finally
        {
            IsReadingPackHealth = false;
        }
    }

    private bool CanRefreshPackHealth() => !IsReadingPackHealth;

    private void ApplyPackHealth(SmartBatteryStatus pack)
    {
        if (!pack.IsAvailable)
        {
            HasPackHealth = false;
            PackCells.Clear();
            PackHealthNoticeSeverity = InfoBarSeverity.Informational;
            PackHealthNotice = "This machine's battery did not answer. Detailed pack readings are unavailable here.";
            return;
        }

        HasPackHealth = true;

        // A cut-off pack is electrically disconnected and will not charge until woken, which looks exactly
        // like a dead battery. Say so before anything else — it explains everything below it.
        PackHealthNoticeSeverity = pack.CutoffState == FrameworkBatteryCutoffState.CutOff
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Informational;
        PackHealthNotice = pack.CutoffState == FrameworkBatteryCutoffState.CutOff
            ? "This pack is in shipping cutoff. It will not charge until it is woken."
            : pack.IsUnsealed
                ? string.Empty
                : "This pack does not publish its detailed health registers, so its own state of health is not shown.";

        PackIdentityText = string.Join(
            " · ",
            new[] { pack.ManufacturerName, pack.DeviceName, pack.Chemistry }.Where(static part => part.Length > 0));

        PackAgeText = pack.AgeInDays is int days
            ? $"{days / 365d:0.#} years old · {pack.CycleCount} cycles"
            : $"{pack.CycleCount} cycles";

        RefreshPackCells(pack);

        PackRequestText =
            $"Asking for {_unitFormattingService.FormatVoltage(pack.ChargingVoltageVolts)} · "
            + $"{_unitFormattingService.FormatCurrent(pack.ChargingCurrentAmperes)}";

        PackStateOfHealthText = pack.StateOfHealthEnergyWattHours is double wattHours && wattHours > 0d
            ? $"Pack reports {_unitFormattingService.FormatEnergyWattHours(wattHours, decimals: 1)} of health capacity"
            : string.Empty;
    }

    /// <summary>
    /// Rebuilds the per-cell bars and the imbalance verdict.
    /// </summary>
    /// <remarks>
    /// Bars are scaled against the HIGHEST reporting cell rather than a nominal 4.2 V, because the point of
    /// the display is the difference between cells. Against a fixed ceiling four healthy cells all render at
    /// the same near-full length and a drifting one is invisible.
    /// </remarks>
    private void RefreshPackCells(SmartBatteryStatus pack)
    {
        double[] cells =
        [
            pack.CellVoltageVolts1,
            pack.CellVoltageVolts2,
            pack.CellVoltageVolts3,
            pack.CellVoltageVolts4,
        ];

        var reporting = cells.Where(static volts => volts > 0d).ToArray();
        PackCells.Clear();

        if (reporting.Length == 0)
        {
            PackCellImbalanceText = "This pack does not report individual cell voltages.";
            IsPackCellImbalanceHigh = false;
            return;
        }

        var highest = reporting.Max();
        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index] <= 0d)
            {
                continue;
            }

            PackCells.Add(new PackCellReading(
                $"Cell {index + 1}",
                _unitFormattingService.FormatVoltage(cells[index]),
                highest > 0d ? cells[index] / highest : 0d));
        }

        if (pack.CellImbalanceVolts is not double imbalance)
        {
            PackCellImbalanceText = "Only one cell reported, so there is nothing to compare.";
            IsPackCellImbalanceHigh = false;
            return;
        }

        IsPackCellImbalanceHigh = imbalance >= HighCellImbalanceVolts;
        PackCellImbalanceText = IsPackCellImbalanceHigh
            ? $"Cells differ by {_unitFormattingService.FormatVoltage(imbalance)} — enough to be worth watching."
            : $"Cells within {_unitFormattingService.FormatVoltage(imbalance)} of each other.";
    }

    /// <summary>
    /// The spread at which cell drift stops being normal.
    /// </summary>
    /// <remarks>
    /// Healthy lithium cells in a pack track each other within a few tens of millivolts once settled. Fifty
    /// is loose enough not to fire on a pack mid-charge, where the cells legitimately diverge for a while.
    /// </remarks>
    private const double HighCellImbalanceVolts = 0.05d;
}

/// <summary>One cell's voltage, ready to render as a labelled bar.</summary>
/// <param name="Label">"Cell 1".</param>
/// <param name="VoltageText">The voltage, already in the user's units.</param>
/// <param name="Fraction">Length relative to the highest reporting cell, 0–1.</param>
public sealed record PackCellReading(string Label, string VoltageText, double Fraction);
