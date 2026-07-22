using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using SubZeroFramework.Controls.FanCurveProfiles.Models;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Owns the editable custom-curve points (the draggable temperature → duty pairs) and all their mutation /
/// validity rules. Persistent (held by the coordinator, not the per-navigation mode VM, so in-progress edits
/// survive mode switches), it manages the per-point change handlers internally and raises <see cref="Changed"/>
/// once on any add / remove / move so the coordinator can rebuild the chart series and recompute dirty /
/// prediction. The view binds the points through the coordinator's <c>CurvePoints</c> pass-through.
/// </summary>
public sealed class FanCurveDraftModel : IDisposable
{
    private const int MinTemperatureCelsius = 0;
    private const int MaxTemperatureCelsius = 130;

    /// <summary>A curve needs two points; <see cref="Remove"/> refuses to go below this.</summary>
    public const int MinimumPoints = 2;

    private readonly ObservableCollection<CurvePointModel> _points = [];
    private bool _suppressChanged;

    public FanCurveDraftModel()
    {
        CurvePoints = new ReadOnlyObservableCollection<CurvePointModel>(_points);
        _points.CollectionChanged += OnCollectionChanged;
    }

    /// <summary>Raised after any point is added, removed, or edited (temperature / duty changed).</summary>
    public event Action? Changed;

    public ReadOnlyObservableCollection<CurvePointModel> CurvePoints { get; }

    public int Count => _points.Count;

    /// <summary>The points as canonical (temperature, duty) pairs, ascending by temperature.</summary>
    public (int Temperature, double Duty)[] ToOrderedPairs() =>
        _points.Select(static p => (p.TemperatureCelsius, p.DutyPercent))
            .OrderBy(static p => p.TemperatureCelsius)
            .ToArray();

    /// <summary>Replaces all points with the given pairs (ordered by temperature). One <see cref="Changed"/>.</summary>
    public void Load(IEnumerable<(int Temperature, double Duty)> points)
    {
        _suppressChanged = true;
        try
        {
            DetachAll();
            _points.Clear();
            foreach (var (temperature, duty) in points.OrderBy(static p => p.Temperature))
            {
                _points.Add(new CurvePointModel(temperature, duty));
            }
        }
        finally
        {
            _suppressChanged = false;
        }

        Changed?.Invoke();
    }

    /// <summary>Adds a point, clamped to 0–130 °C / 0–100 % (temperature rounded to a whole degree).</summary>
    public void Add(double temperatureCelsius, double dutyPercent) =>
        _points.Add(new CurvePointModel(ClampTemperature(temperatureCelsius), ClampDuty(dutyPercent)));

    /// <summary>Moves an existing point, clamped to 0–130 °C / 0–100 %.</summary>
    public void Update(CurvePointModel point, double temperatureCelsius, double dutyPercent)
    {
        point.TemperatureCelsius = ClampTemperature(temperatureCelsius);
        point.DutyPercent = ClampDuty(dutyPercent);
    }

    /// <summary>Removes a point, keeping at least two (a curve needs two points).</summary>
    public void Remove(CurvePointModel point)
    {
        if (_points.Count <= MinimumPoints)
        {
            return;
        }

        _points.Remove(point);
    }

    /// <summary>The point within the given normalized temperature/duty tolerance nearest to the target, or null.</summary>
    public CurvePointModel? FindNearest(double temperatureCelsius, double dutyPercent, double maxTemperatureDelta, double maxDutyDelta)
    {
        CurvePointModel? best = null;
        var bestDistanceSquared = double.PositiveInfinity;
        foreach (var point in _points)
        {
            var dt = (point.TemperatureCelsius - temperatureCelsius) / maxTemperatureDelta;
            var dd = (point.DutyPercent - dutyPercent) / maxDutyDelta;
            var distanceSquared = (dt * dt) + (dd * dd);
            if (distanceSquared <= 1d && distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = point;
            }
        }

        return best;
    }

    public void Dispose()
    {
        _points.CollectionChanged -= OnCollectionChanged;
        DetachAll();
    }

    private static int ClampTemperature(double value) =>
        (int)Math.Round(Math.Clamp(value, MinTemperatureCelsius, MaxTemperatureCelsius));

    private static double ClampDuty(double value) => Math.Clamp(value, 0d, 100d);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (CurvePointModel point in e.OldItems)
            {
                point.PropertyChanged -= OnPointChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (CurvePointModel point in e.NewItems)
            {
                point.PropertyChanged += OnPointChanged;
            }
        }

        Raise();
    }

    private void OnPointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CurvePointModel.TemperatureCelsius) or nameof(CurvePointModel.DutyPercent))
        {
            Raise();
        }
    }

    private void Raise()
    {
        if (!_suppressChanged)
        {
            Changed?.Invoke();
        }
    }

    private void DetachAll()
    {
        foreach (var point in _points)
        {
            point.PropertyChanged -= OnPointChanged;
        }
    }
}
