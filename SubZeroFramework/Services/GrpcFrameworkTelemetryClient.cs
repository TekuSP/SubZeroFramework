using FrameworkDotnet.Enums;

using DynamicData;
using System.Reactive.Linq;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFrameworkTelemetryClient : IFrameworkTelemetryClient, IDisposable
{
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

        if (historyWindow <= TimeSpan.Zero || historyWindow > TelemetryHistoryLimits.MaximumHistoryWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(historyWindow), $"History window must be between {TimeSpan.Zero} and {TelemetryHistoryLimits.MaximumHistoryWindow}.");
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
                            var changes = call.ResponseStream.Current.Changes;
                            if (changes.Count == 0)
                            {
                                continue;
                            }

                            channels.Edit(updater =>
                            {
                                foreach (var change in changes)
                                {
                                    ApplyTelemetryChannelChange(updater, change);
                                }
                            });
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
                        await Task.Delay(GrpcTransportDefaults.StreamReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
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
                            var changes = call.ResponseStream.Current.Changes;
                            if (changes.Count == 0)
                            {
                                continue;
                            }

                            currentValues.Edit(updater =>
                            {
                                foreach (var change in changes)
                                {
                                    ApplyCurrentTelemetryValueChange(updater, change);
                                }
                            });
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
                        await Task.Delay(GrpcTransportDefaults.StreamReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
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
                        await Task.Delay(GrpcTransportDefaults.StreamReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
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

    private static void ApplyTelemetryChannelChange(ISourceUpdater<TelemetryChannel, TelemetryChannelId> channels, TelemetryChannelChangeReply reply)
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

    private static void ApplyCurrentTelemetryValueChange(ISourceUpdater<CurrentTelemetryValue, TelemetryChannelId> currentValues, CurrentTelemetryValueChangeReply reply)
    {
        var value = new CurrentTelemetryValue
        {
            ChannelId = MapChannelId(reply.ChannelId),
            DisplayName = reply.DisplayName,
            UnitSymbol = string.IsNullOrEmpty(reply.UnitSymbol) ? null : reply.UnitSymbol,
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            NumericValue = reply.HasNumericValue ? reply.NumericValue : null,
            TemperatureState = ParseTemperatureState(reply.TemperatureState),
            SensorName = ParseSensorName(reply.SensorName),
            FanName = ParseFanName(reply.FanName),
            PowerSourceState = ParsePowerSourceState(reply.PowerSourceState),
            BatteryState = ParseBatteryState(reply.BatteryState),
            BatteryManufacturer = string.IsNullOrEmpty(reply.BatteryManufacturer) ? null : reply.BatteryManufacturer,
            BatteryModelNumber = string.IsNullOrEmpty(reply.BatteryModelNumber) ? null : reply.BatteryModelNumber,
            BatterySerialNumber = string.IsNullOrEmpty(reply.BatterySerialNumber) ? null : reply.BatterySerialNumber,
            BatteryType = string.IsNullOrEmpty(reply.BatteryType) ? null : reply.BatteryType,
            BatteryRemainingCapacityAmpereHours = reply.HasBatteryRemainingCapacityAmpereHours ? reply.BatteryRemainingCapacityAmpereHours : null,
            BatteryDesignCapacityAmpereHours = reply.HasBatteryDesignCapacityAmpereHours ? reply.BatteryDesignCapacityAmpereHours : null,
            BatteryLastFullChargeCapacityAmpereHours = reply.HasBatteryLastFullChargeCapacityAmpereHours ? reply.BatteryLastFullChargeCapacityAmpereHours : null,
            BatteryDesignVoltageVolts = reply.HasBatteryDesignVoltageVolts ? reply.BatteryDesignVoltageVolts : null,
            BatteryCycleCount = reply.HasBatteryCycleCount ? reply.BatteryCycleCount : null,
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

    // FD0001 (platform-specific enum members) is intentionally suppressed: we translate whatever fan name the
    // device itself reported, so only the cases valid for the running platform are ever hit; the rest are inert.
#pragma warning disable FD0001
    private static FrameworkFanName? ParseFanName(FanNameValue value)
    {
        return value switch
        {
            FanNameValue.Generic => FrameworkFanName.Generic,
            FanNameValue.ApuFan => FrameworkFanName.ApuFan,
            FanNameValue.LeftFan => FrameworkFanName.LeftFan,
            FanNameValue.RightFan => FrameworkFanName.RightFan,
            FanNameValue.FrontFan => FrameworkFanName.FrontFan,
            FanNameValue.ThirdFan => FrameworkFanName.ThirdFan,
            _ => null,
        };
    }
#pragma warning restore FD0001

    private static FrameworkSensorName? ParseSensorName(TemperatureSensorNameValue value)
    {
        return value switch
        {
            TemperatureSensorNameValue.Generic => FrameworkSensorName.Generic,
            TemperatureSensorNameValue.F75303Local => FrameworkSensorName.F75303Local,
            TemperatureSensorNameValue.F75303Cpu => FrameworkSensorName.F75303Cpu,
            TemperatureSensorNameValue.F75303Ddr => FrameworkSensorName.F75303Ddr,
            TemperatureSensorNameValue.Battery => FrameworkSensorName.Battery,
            TemperatureSensorNameValue.Peci => FrameworkSensorName.Peci,
            TemperatureSensorNameValue.F57397VccGt => FrameworkSensorName.F57397VccGt,
            TemperatureSensorNameValue.F75303Skin => FrameworkSensorName.F75303Skin,
            TemperatureSensorNameValue.ChargerIc => FrameworkSensorName.ChargerIc,
            TemperatureSensorNameValue.Apu => FrameworkSensorName.Apu,
            TemperatureSensorNameValue.DgpuVr => FrameworkSensorName.DgpuVr,
            TemperatureSensorNameValue.DgpuVram => FrameworkSensorName.DgpuVram,
            TemperatureSensorNameValue.DgpuAmb => FrameworkSensorName.DgpuAmb,
            TemperatureSensorNameValue.DgpuTemp => FrameworkSensorName.DgpuTemp,
            TemperatureSensorNameValue.F75303Apu => FrameworkSensorName.F75303Apu,
            TemperatureSensorNameValue.F75303Amb => FrameworkSensorName.F75303Amb,
            TemperatureSensorNameValue.Virtual => FrameworkSensorName.Virtual,
            _ => null,
        };
    }

    private static FrameworkPowerSourceState? ParsePowerSourceState(PowerSourceStateValue value)
    {
        return value switch
        {
            PowerSourceStateValue.None => FrameworkPowerSourceState.None,
            PowerSourceStateValue.AcOnly => FrameworkPowerSourceState.AcOnly,
            PowerSourceStateValue.BatteryOnly => FrameworkPowerSourceState.BatteryOnly,
            PowerSourceStateValue.AcAndBattery => FrameworkPowerSourceState.AcAndBattery,
            _ => null,
        };
    }

    private static FrameworkBatteryState? ParseBatteryState(BatteryStateValue value)
    {
        return value switch
        {
            BatteryStateValue.NotPresent => FrameworkBatteryState.NotPresent,
            BatteryStateValue.Idle => FrameworkBatteryState.Idle,
            BatteryStateValue.Charging => FrameworkBatteryState.Charging,
            BatteryStateValue.Discharging => FrameworkBatteryState.Discharging,
            BatteryStateValue.ChargingAndDischarging => FrameworkBatteryState.ChargingAndDischarging,
            BatteryStateValue.Critical => FrameworkBatteryState.Critical,
            _ => null,
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
