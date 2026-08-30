using System.Collections.ObjectModel;
using System.ComponentModel;

using FrameworkDotnet.Enums;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Owns the selectable driving-temperature sensor chips for the custom-curve editor: which sensors exist
/// (built from the temperature stream), their usable/selected state, and the "never zero usable" guard. The
/// page coordinator reacts to <see cref="SelectionChanged"/> (re-render the chart, recompute dirty),
/// <see cref="SensorRemoved"/> (drop the chart series), and <see cref="SensorRenamed"/> (rename the series).
/// </summary>
public sealed class FanSensorSelectionModel
{
    private readonly IFanHistoryStore _historyStore;
    private readonly Services.Units.IUnitFormattingService _unitFormattingService;
    private readonly ObservableCollection<SensorChipModel> _availableSensors = [];
    private readonly Dictionary<int, SensorChipModel> _sensorChipIndex = [];
    private readonly Dictionary<SensorChipModel, PropertyChangedEventHandler> _sensorChipHandlers = [];
    private bool _suppressSensorSelectionReentry;

    // The selection the last load/user interaction ASKED for, including sensors whose chips have not
    // streamed in yet. A late-arriving chip named here selects itself on arrival, so a saved profile's
    // selection is never silently narrowed by stream timing (which read as a phantom dirty draft).
    private readonly HashSet<int> _desiredSelection = [];

    public FanSensorSelectionModel(IFanHistoryStore historyStore, Services.Units.IUnitFormattingService unitFormattingService)
    {
        _historyStore = historyStore;
        _unitFormattingService = unitFormattingService;
        AvailableSensors = new ReadOnlyObservableCollection<SensorChipModel>(_availableSensors);
    }

    public ReadOnlyObservableCollection<SensorChipModel> AvailableSensors { get; }

    /// <summary>Raised when a chip's selection changes (user toggle, or an unusable sensor auto-deselected).</summary>
    public event Action? SelectionChanged;

    /// <summary>Raised when a sensor leaves the fleet (so the chart can drop its cached series).</summary>
    public event Action<int>? SensorRemoved;

    /// <summary>Raised when a sensor's display name changes (so the chart can relabel its series).</summary>
    public event Action<int, string>? SensorRenamed;

    public bool AnySelected => _availableSensors.Any(static c => c.IsSelected);

    public IReadOnlyList<SensorChipModel> AllChips => _availableSensors;

    public IReadOnlyList<SensorChipModel> SelectedChips => _availableSensors.Where(static c => c.IsSelected).ToArray();

    public int[] SelectedIndices() =>
        _availableSensors.Where(static c => c.IsSelected).Select(static c => c.SensorIndex).OrderBy(static i => i).ToArray();

    public void Upsert(TemperatureTelemetrySnapshot snapshot)
    {
        var state = snapshot.TemperatureState ?? (snapshot.IsAvailable ? FrameworkTemperatureState.Ok : FrameworkTemperatureState.NotPresent);

        // A sensor that has NEVER reported is omitted from the selector entirely (design). One already on
        // screen is a different matter: it must not vanish just because it stopped reporting. The GPU die
        // sensor drops to NotPresent (not NotPowered) when the GPU powers down, and deleting its chip made the
        // sensor list change length under the user — and took a selected sensor out of the selector with it.
        if (state == FrameworkTemperatureState.NotPresent)
        {
            if (_sensorChipIndex.TryGetValue(snapshot.SensorIndex, out var knownChip))
            {
                knownChip.State = state;
                knownChip.CurrentTemperatureCelsius = null;
            }

            return;
        }

        var chipName = ShortenSensorName($"{snapshot.DisplayName.Trim()}{Environment.NewLine}{FrameworkSensorNameDisplay.ToLocation(snapshot.SensorName)}", snapshot.SensorIndex);

        if (!_sensorChipIndex.TryGetValue(snapshot.SensorIndex, out var chip))
        {
            chip = new SensorChipModel(snapshot.SensorIndex, chipName, _unitFormattingService);
            _sensorChipIndex[snapshot.SensorIndex] = chip;
            AttachSensorChipHandler(chip);

            var insertIndex = 0;
            while (insertIndex < _availableSensors.Count && _availableSensors[insertIndex].SensorIndex < snapshot.SensorIndex)
            {
                insertIndex++;
            }
            _availableSensors.Insert(insertIndex, chip);
        }
        else
        {
            chip.DisplayName = chipName;
        }

        chip.State = state;
        chip.CurrentTemperatureCelsius = state == FrameworkTemperatureState.Ok ? snapshot.TemperatureCelsius : null;

        // Kept once seen: the firmware metadata read lands on the first successful thermal poll, which can be
        // after a chip already exists, and a later tick reporting null must not erase what was learned.
        chip.FirmwareWarnCelsius ??= snapshot.FirmwareWarnCelsius;

        // A chip the desired selection was waiting for is now selectable: select it so the draft matches the
        // loaded profile again. This covers both a chip streaming in for the first time AND one that was
        // already on screen but unusable — e.g. GPU sensors coming back when the GPU powers up.
        if (chip.IsUsable && !chip.IsSelected && _desiredSelection.Contains(chip.SensorIndex))
        {
            _suppressSensorSelectionReentry = true;
            try { chip.IsSelected = true; }
            finally { _suppressSensorSelectionReentry = false; }
            _historyStore.EnsureTemperatureHistory(chip.SensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
            SelectionChanged?.Invoke();
        }

        // A sensor going unusable (error / no power — e.g. the GPU powering down) deliberately does NOT change
        // the selection. Deselecting it here silently rewrote the user's saved profile: the selection emptied,
        // the editor's "never none" rule then adopted whichever sensor happened to be first, and the next Apply
        // persisted that. The sensor stays selected and shows as unavailable; how a missing reading affects the
        // curve is the profile's own choice (see the treat-missing-as-zero option), not a selection change.
    }

    public void Remove(int sensorIndex)
    {
        if (_sensorChipIndex.Remove(sensorIndex, out var chip))
        {
            DetachSensorChipHandler(chip);
            _availableSensors.Remove(chip);
            SensorRemoved?.Invoke(sensorIndex);
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Deselects all but the first usable sensor (the default-draft "never none" start). Returns its index.</summary>
    public int? SelectFirstUsableOnly()
    {
        _suppressSensorSelectionReentry = true;
        try
        {
            var firstUsable = _availableSensors.FirstOrDefault(static c => c.IsUsable);
            foreach (var chip in _availableSensors)
            {
                chip.IsSelected = ReferenceEquals(chip, firstUsable);
            }

            _desiredSelection.Clear();
            if (firstUsable is not null)
            {
                _desiredSelection.Add(firstUsable.SensorIndex);
            }
        }
        finally
        {
            _suppressSensorSelectionReentry = false;
        }

        return _availableSensors.FirstOrDefault(static c => c.IsSelected)?.SensorIndex;
    }

    /// <summary>
    /// Sets the selection to exactly the given sensor indices (loading a saved/pending draft). Indices whose
    /// chips have not streamed in yet are remembered and selected on arrival.
    /// </summary>
    public void SetSelected(IReadOnlyCollection<int> sensorIndices)
    {
        _suppressSensorSelectionReentry = true;
        try
        {
            _desiredSelection.Clear();
            foreach (var sensorIndex in sensorIndices)
            {
                _desiredSelection.Add(sensorIndex);
            }

            foreach (var chip in _availableSensors)
            {
                chip.IsSelected = _desiredSelection.Contains(chip.SensorIndex);
            }
        }
        finally
        {
            _suppressSensorSelectionReentry = false;
        }
    }

    /// <summary>
    /// If NOTHING is selected but a usable sensor exists, selects the first usable one and returns its index
    /// (so the caller can ensure its history). Returns null when nothing needed selecting.
    /// </summary>
    /// <remarks>
    /// The test is "nothing selected at all", NOT "nothing usable selected". A selection whose sensors merely
    /// went unusable — a sleeping GPU — must survive untouched; treating that as "none selected" is what let a
    /// replacement sensor be adopted into the user's profile silently.
    /// </remarks>
    public int? SelectFirstUsableIfNoneSelected()
    {
        if (_availableSensors.Any(static c => c.IsSelected))
        {
            return null;
        }

        if (_availableSensors.FirstOrDefault(static c => c.IsUsable) is not { } firstUsable)
        {
            return null;
        }

        _suppressSensorSelectionReentry = true;
        try { firstUsable.IsSelected = true; }
        finally { _suppressSensorSelectionReentry = false; }

        _desiredSelection.Add(firstUsable.SensorIndex);
        return firstUsable.SensorIndex;
    }

    public void DisposeHandlers()
    {
        foreach (var chip in _sensorChipHandlers.Keys.ToArray())
        {
            DetachSensorChipHandler(chip);
        }
    }

    // The redesigned sensor chips use short labels ("Temp 0") instead of the long telemetry name.
    private static string ShortenSensorName(string? displayName, int sensorIndex) =>
        !string.IsNullOrWhiteSpace(displayName)
            ? displayName.Replace("Temperature Sensor", "Temp", StringComparison.OrdinalIgnoreCase).Trim()
            : $"Temp {sensorIndex}";

    private void AttachSensorChipHandler(SensorChipModel chip)
    {
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SensorChipModel.IsSelected))
            {
                if (_suppressSensorSelectionReentry)
                {
                    return;
                }

                if (!chip.IsSelected && !_availableSensors.Any(static c => c.IsSelected))
                {
                    _suppressSensorSelectionReentry = true;
                    try { chip.IsSelected = true; }
                    finally { _suppressSensorSelectionReentry = false; }
                    return;
                }

                // A user toggle updates the desired selection (loads overwrite it wholesale via SetSelected).
                if (chip.IsSelected)
                {
                    _desiredSelection.Add(chip.SensorIndex);
                    _historyStore.EnsureTemperatureHistory(chip.SensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
                }
                else
                {
                    _desiredSelection.Remove(chip.SensorIndex);
                }

                SelectionChanged?.Invoke();
            }
            else if (args.PropertyName == nameof(SensorChipModel.DisplayName))
            {
                SensorRenamed?.Invoke(chip.SensorIndex, chip.DisplayName);
            }
        };

        chip.PropertyChanged += handler;
        _sensorChipHandlers[chip] = handler;
    }

    private void DetachSensorChipHandler(SensorChipModel chip)
    {
        if (_sensorChipHandlers.Remove(chip, out var handler))
        {
            chip.PropertyChanged -= handler;
        }
    }
}
