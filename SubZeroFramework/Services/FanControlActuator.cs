using FrameworkDotnet.Enums;

using Microsoft.Extensions.Logging;

namespace SubZeroFramework.Services;

/// <inheritdoc cref="IFanControlActuator" />
public sealed class FanControlActuator : IFanControlActuator, IDisposable
{
    private readonly IFrameworkFanControlClient _client;
    private readonly ILogger<FanControlActuator> _logger;

    // One hold per fan in the previewed group; cancelling them (commit / revert / fan switch / dispose) makes the
    // service revert any fan whose preview was never committed.
    private readonly Dictionary<int, CancellationTokenSource> _previewHolds = [];

    public FanControlActuator(IFrameworkFanControlClient client, ILogger<FanControlActuator> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task ActuateSimpleAsync(int fanIndex, FanControlMode mode, double manualDutyPercent, bool preview, CancellationToken cancellationToken = default)
    {
        switch (mode)
        {
            case FanControlMode.Manual:
                await _client.SetFanDutyAsync(fanIndex, Math.Clamp(manualDutyPercent, 0d, 100d), preview, cancellationToken).ConfigureAwait(false);
                break;
            case FanControlMode.Max:
                await _client.SetFanMaxAsync(fanIndex, preview, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await _client.RestoreAutoFanControlAsync(fanIndex, preview, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public Task<FrameworkFanCustomCurveCommandResult> ActuateCurveAsync(
        int fanIndex,
        IReadOnlyDictionary<int, double> curvePoints,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregation,
        bool preview,
        CancellationToken cancellationToken = default)
        => _client.SetCustomCurveAsync(fanIndex, curvePoints, drivingSensorIndices, aggregation, preview, cancellationToken);

    public async Task OpenPreviewHoldsAsync(IReadOnlyCollection<int> fanIndices)
    {
        CancelPreviewHold();

        foreach (var fanIndex in fanIndices)
        {
            var cts = new CancellationTokenSource();
            _previewHolds[fanIndex] = cts;

            try
            {
                await _client.OpenPreviewHoldAsync(fanIndex, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Best-effort safety net; the client-side revert still works if the hold can't open.
                _logger.LogWarning(ex, "Failed to open preview safety hold for fan {FanIndex}", fanIndex);
            }
        }
    }

    public void CancelPreviewHold()
    {
        foreach (var cts in _previewHolds.Values)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        _previewHolds.Clear();
    }

    public void Dispose()
    {
        foreach (var cts in _previewHolds.Values)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        _previewHolds.Clear();
    }
}
