using FrameworkDotnet.Enums;

using DynamicData;
using System.Reactive.Linq;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFrameworkTelemetryClient : IFrameworkTelemetryClient, IDisposable
{
    private static readonly TimeSpan MaximumHistoryWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _client;
    private readonly IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> _sharedChannels;
    private readonly IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> _sharedCurrentValues;
    private readonly RefCountedObservableCache<TelemetrySeriesStreamKey, IChangeSet<TelemetryPoint, long>> _seriesStreams = new();
    private bool _disposed;

    public GrpcFrameworkTelemetryClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(_channelFactory.Channel);
        _sharedChannels = _channelFactory.ShareLatest(CreateChannelsStream());
        _sharedCurrentValues = _channelFactory.ShareLatest(CreateCurrentValuesStream());
    }

    /// <summary>
    /// Watches the available telemetry channels.
    /// </summary>
    public IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> WatchTelemetryChannels()
    {
        ThrowIfDisposed();
        return _sharedChannels;
    }

    /// <summary>
    /// Watches the latest current telemetry values.
    /// </summary>
    public IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> WatchCurrentTelemetryValues()
    {
        ThrowIfDisposed();
        return _sharedCurrentValues;
    }

    /// <summary>
    /// Watches a retained telemetry series for the requested channel and history window.
    /// </summary>
    /// <param name="channelId">The logical telemetry channel identifier.</param>
    /// <param name="historyWindow">The requested history window.</param>
    public IObservable<IChangeSet<TelemetryPoint, long>> WatchTelemetrySeries(TelemetryChannelId channelId, TimeSpan historyWindow)
    {
        ThrowIfDisposed();

        if (historyWindow <= TimeSpan.Zero || historyWindow > MaximumHistoryWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(historyWindow), $"History window must be between {TimeSpan.Zero} and {MaximumHistoryWindow}.");
        }

        return _seriesStreams.GetOrAdd(new TelemetrySeriesStreamKey(channelId, historyWindow), () => CreateSeriesStream(channelId, historyWindow));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> CreateChannelsStream()
    {
        return Observable.Create<IChangeSet<TelemetryChannel, TelemetryChannelId>>(observer =>
        {
            var channels = new SourceCache<TelemetryChannel, TelemetryChannelId>(channel => channel.Id);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<TelemetryChannelChangeBatchReply>? call = null;

                    try
                    {
                        call = _client.WatchTelemetryChannels(new WatchTelemetryChannelsRequest(), cancellationToken: cancellationSource.Token);

                        using var connection = channels.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            foreach (var change in call.ResponseStream.Current.Changes)
                            {
                                ApplyTelemetryChannelChange(channels, change);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    catch (Exception) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        call?.Dispose();
                    }

                    try
                    {
                        await Task.Delay(ReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                channels.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> CreateCurrentValuesStream()
    {
        return Observable.Create<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>>(observer =>
        {
            var currentValues = new SourceCache<CurrentTelemetryValue, TelemetryChannelId>(value => value.ChannelId);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<CurrentTelemetryValueChangeBatchReply>? call = null;

                    try
                    {
                        call = _client.WatchCurrentTelemetryValues(new WatchCurrentTelemetryValuesRequest(), cancellationToken: cancellationSource.Token);

                        using var connection = currentValues.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            foreach (var change in call.ResponseStream.Current.Changes)
                            {
                                ApplyCurrentTelemetryValueChange(currentValues, change);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    catch (Exception) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        call?.Dispose();
                    }

                    try
                    {
                        await Task.Delay(ReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                currentValues.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private IObservable<IChangeSet<TelemetryPoint, long>> CreateSeriesStream(TelemetryChannelId channelId, TimeSpan historyWindow)
    {
        return Observable.Create<IChangeSet<TelemetryPoint, long>>(observer =>
        {
            var points = new SourceCache<TelemetryPoint, long>(point => point.SampleId);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<TelemetrySeriesPointChangeBatchReply>? call = null;

                    try
                    {
                        call = _client.WatchTelemetrySeries(new WatchTelemetrySeriesRequest
                        {
                            Area = MapTelemetryArea(channelId.Area),
                            EntityKind = MapTelemetryEntityKind(channelId.EntityKind),
                            Index = channelId.Index,
                            Metric = MapTelemetryMetric(channelId.Metric),
                            HistoryWindowSeconds = checked((int)Math.Ceiling(historyWindow.TotalSeconds)),
                        }, cancellationToken: cancellationSource.Token);

                        using var connection = points.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            foreach (var change in call.ResponseStream.Current.Changes)
                            {
                                ApplyTelemetryPointChange(points, change);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    catch (Exception) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        call?.Dispose();
                    }

                    try
                    {
                        await Task.Delay(ReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                points.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private static void ApplyTelemetryChannelChange(SourceCache<TelemetryChannel, TelemetryChannelId> channels, TelemetryChannelChangeReply reply)
    {
        var channel = new TelemetryChannel
        {
            Id = MapChannelId(reply.ChannelId),
            DisplayName = reply.DisplayName,
            UnitSymbol = string.IsNullOrEmpty(reply.UnitSymbol) ? null : reply.UnitSymbol,
            FirstObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.FirstObservedAtUnixTimeMilliseconds),
            LastObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.LastObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
        };

        if (reply.ChangeKind == TelemetryChangeKind.Remove)
        {
            var existingChannel = channels.Lookup(channel.Id);
            if (existingChannel.HasValue)
            {
                channels.AddOrUpdate(existingChannel.Value with { IsAvailable = false });
            }

            return;
        }

        channels.AddOrUpdate(channel);
    }

    private static void ApplyCurrentTelemetryValueChange(SourceCache<CurrentTelemetryValue, TelemetryChannelId> currentValues, CurrentTelemetryValueChangeReply reply)
    {
        var value = new CurrentTelemetryValue
        {
            ChannelId = MapChannelId(reply.ChannelId),
            DisplayName = reply.DisplayName,
            UnitSymbol = string.IsNullOrEmpty(reply.UnitSymbol) ? null : reply.UnitSymbol,
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            NumericValue = reply.HasNumericValue ? reply.NumericValue : null,
            TemperatureState = ParseTemperatureState(reply.TemperatureState),
            IsAvailable = reply.IsAvailable,
        };

        if (reply.ChangeKind == TelemetryChangeKind.Remove)
        {
            var existingValue = currentValues.Lookup(value.ChannelId);
            if (existingValue.HasValue)
            {
                currentValues.AddOrUpdate(existingValue.Value with { IsAvailable = false });
            }

            return;
        }

        currentValues.AddOrUpdate(value);
    }

    private static void ApplyTelemetryPointChange(SourceCache<TelemetryPoint, long> points, TelemetrySeriesPointChangeReply reply)
    {
        if (reply.ChangeKind == TelemetryChangeKind.Remove)
        {
            points.Remove(reply.SampleId);
            return;
        }

        points.AddOrUpdate(new TelemetryPoint(
            SampleId: reply.SampleId,
            ChannelId: MapChannelId(reply.ChannelId),
            ObservedAt: DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            NumericValue: reply.NumericValue));
    }

    private static TelemetryChannelId MapChannelId(TelemetryChannelIdReply reply)
    {
        if (!TryParseTelemetryArea(reply.Area, out var area)
            || !TryParseTelemetryEntityKind(reply.EntityKind, out var entityKind)
            || !TryParseTelemetryMetric(reply.Metric, out var metric))
        {
            throw new InvalidOperationException("The service returned an invalid telemetry channel identifier.");
        }

        return new TelemetryChannelId(area, entityKind, reply.Index, metric);
    }

    private static TelemetryAreaValue MapTelemetryArea(TelemetryArea area)
    {
        return area switch
        {
            TelemetryArea.Thermal => TelemetryAreaValue.Thermal,
            TelemetryArea.Power => TelemetryAreaValue.Power,
            _ => TelemetryAreaValue.Unspecified,
        };
    }

    private static TelemetryEntityKindValue MapTelemetryEntityKind(TelemetryEntityKind entityKind)
    {
        return entityKind switch
        {
            TelemetryEntityKind.TemperatureSensor => TelemetryEntityKindValue.TemperatureSensor,
            TelemetryEntityKind.Fan => TelemetryEntityKindValue.Fan,
            TelemetryEntityKind.Battery => TelemetryEntityKindValue.Battery,
            _ => TelemetryEntityKindValue.Unspecified,
        };
    }

    private static TelemetryMetricValue MapTelemetryMetric(TelemetryMetric metric)
    {
        return metric switch
        {
            TelemetryMetric.TemperatureCelsius => TelemetryMetricValue.TemperatureCelsius,
            TelemetryMetric.FanSpeedRpm => TelemetryMetricValue.FanSpeedRpm,
            TelemetryMetric.BatteryChargePercent => TelemetryMetricValue.BatteryChargePercent,
            TelemetryMetric.BatteryPresentRateAmperes => TelemetryMetricValue.BatteryPresentRateAmperes,
            TelemetryMetric.BatteryPresentVoltageVolts => TelemetryMetricValue.BatteryPresentVoltageVolts,
            _ => TelemetryMetricValue.Unspecified,
        };
    }

    private static bool TryParseTelemetryArea(TelemetryAreaValue value, out TelemetryArea area)
    {
        area = value switch
        {
            TelemetryAreaValue.Thermal => TelemetryArea.Thermal,
            TelemetryAreaValue.Power => TelemetryArea.Power,
            _ => default,
        };

        return value is not TelemetryAreaValue.Unspecified;
    }

    private static bool TryParseTelemetryEntityKind(TelemetryEntityKindValue value, out TelemetryEntityKind entityKind)
    {
        entityKind = value switch
        {
            TelemetryEntityKindValue.TemperatureSensor => TelemetryEntityKind.TemperatureSensor,
            TelemetryEntityKindValue.Fan => TelemetryEntityKind.Fan,
            TelemetryEntityKindValue.Battery => TelemetryEntityKind.Battery,
            _ => default,
        };

        return value is not TelemetryEntityKindValue.Unspecified;
    }

    private static bool TryParseTelemetryMetric(TelemetryMetricValue value, out TelemetryMetric metric)
    {
        metric = value switch
        {
            TelemetryMetricValue.TemperatureCelsius => TelemetryMetric.TemperatureCelsius,
            TelemetryMetricValue.FanSpeedRpm => TelemetryMetric.FanSpeedRpm,
            TelemetryMetricValue.BatteryChargePercent => TelemetryMetric.BatteryChargePercent,
            TelemetryMetricValue.BatteryPresentRateAmperes => TelemetryMetric.BatteryPresentRateAmperes,
            TelemetryMetricValue.BatteryPresentVoltageVolts => TelemetryMetric.BatteryPresentVoltageVolts,
            _ => default,
        };

        return value is not TelemetryMetricValue.Unspecified;
    }

    private static FrameworkTemperatureState? ParseTemperatureState(TemperatureStateValue value)
    {
        return value switch
        {
            TemperatureStateValue.Ok => FrameworkTemperatureState.Ok,
            TemperatureStateValue.NotPresent => FrameworkTemperatureState.NotPresent,
            TemperatureStateValue.Error => FrameworkTemperatureState.Error,
            TemperatureStateValue.NotPowered => FrameworkTemperatureState.NotPowered,
            TemperatureStateValue.NotCalibrated => FrameworkTemperatureState.NotCalibrated,
            _ => null,
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
