using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using DynamicData;

using FrameworkDotnet.Enums;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <inheritdoc cref="ICoolingProfileClient" />
public sealed class GrpcCoolingProfileClient : ICoolingProfileClient, IDisposable
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _telemetryClient;
    private readonly FrameworkFanControlService.FrameworkFanControlServiceClient _fanControlClient;
    private readonly IObservable<IChangeSet<CoolingProfile, string>> _sharedProfiles;

    /// <summary>
    /// The last selection seen on the stream.
    /// </summary>
    /// <remarks>
    /// A BehaviorSubject so a view model that subscribes after the first batch still learns the current
    /// selection, rather than waiting for the next change to something it does not control.
    /// </remarks>
    private readonly BehaviorSubject<string?> _activeProfileId = new(null);

    private bool _disposed;

    public GrpcCoolingProfileClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _telemetryClient = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(_channelFactory.Channel);
        _fanControlClient = new FrameworkFanControlService.FrameworkFanControlServiceClient(_channelFactory.Channel);
        _sharedProfiles = _channelFactory.ShareLatest(CreateProfilesStream());
    }

    public IObservable<IChangeSet<CoolingProfile, string>> WatchCoolingProfiles()
    {
        ThrowIfDisposed();
        return _sharedProfiles;
    }

    public IObservable<string?> WatchActiveProfileId()
    {
        ThrowIfDisposed();

        // Subscribing to the profile stream keeps the connection alive for callers that only care about the
        // selection: without it, a shell watching only the tint would never open the stream that reports it.
        return _sharedProfiles
            .Select(static _ => (string?)null)
            .IgnoreElements()
            .Merge(_activeProfileId)
            .DistinctUntilChanged();
    }

    public Task<CoolingProfileCommandResult> SaveAsync(CoolingProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfDisposed();

        return InvokeAsync(
            () => _fanControlClient.SaveCoolingProfileAsync(
                new SaveCoolingProfileRequest { Profile = ToReply(profile) },
                cancellationToken: cancellationToken).ResponseAsync);
    }

    public Task<CoolingProfileCommandResult> DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return InvokeAsync(
            () => _fanControlClient.DeleteCoolingProfileAsync(
                new DeleteCoolingProfileRequest { ProfileId = profileId },
                cancellationToken: cancellationToken).ResponseAsync);
    }

    public Task<CoolingProfileCommandResult> RenameAsync(string profileId, string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return InvokeAsync(
            () => _fanControlClient.RenameCoolingProfileAsync(
                new RenameCoolingProfileRequest { ProfileId = profileId, Name = name },
                cancellationToken: cancellationToken).ResponseAsync);
    }

    public Task<CoolingProfileCommandResult> SetActiveAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return InvokeAsync(
            () => _fanControlClient.SetActiveCoolingProfileAsync(
                new SetActiveCoolingProfileRequest { ProfileId = profileId },
                cancellationToken: cancellationToken).ResponseAsync);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeProfileId.Dispose();
    }

    /// <summary>
    /// Runs one command and turns any transport failure into a result rather than an exception.
    /// </summary>
    /// <remarks>
    /// A profile command failing because the service is down is an ordinary thing that deserves an InfoBar,
    /// not a crash: every caller here is a button press.
    /// </remarks>
    private static async Task<CoolingProfileCommandResult> InvokeAsync(Func<Task<CoolingProfileOperationReply>> call)
    {
        try
        {
            var reply = await call().ConfigureAwait(false);

            return new CoolingProfileCommandResult(
                reply.Succeeded,
                reply.Message,
                [.. reply.FailedFanNames]);
        }
        catch (RpcException)
        {
            return CoolingProfileCommandResult.Unreachable;
        }
        catch (ObjectDisposedException)
        {
            return CoolingProfileCommandResult.Unreachable;
        }
    }

    private IObservable<IChangeSet<CoolingProfile, string>> CreateProfilesStream()
    {
        return Observable.Create<IChangeSet<CoolingProfile, string>>(observer =>
        {
            var profiles = new SourceCache<CoolingProfile, string>(static profile => profile.Id);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<CoolingProfileChangeBatchReply>? call = null;

                    try
                    {
                        call = _telemetryClient.WatchCoolingProfiles(new WatchCoolingProfilesRequest(), cancellationToken: cancellationSource.Token);

                        using var connection = profiles.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            var batch = call.ResponseStream.Current;

                            // Every batch carries the selection, including the selection-only batches the
                            // service sends when someone switches profile without editing the library.
                            _activeProfileId.OnNext(string.IsNullOrEmpty(batch.ActiveProfileId) ? null : batch.ActiveProfileId);

                            if (batch.Changes.Count == 0)
                            {
                                continue;
                            }

                            profiles.Edit(updater =>
                            {
                                foreach (var change in batch.Changes)
                                {
                                    ApplyProfileChange(updater, change);
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

                profiles.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private static void ApplyProfileChange(ISourceUpdater<CoolingProfile, string> profiles, CoolingProfileChangeReply change)
    {
        if (change.Profile is null)
        {
            return;
        }

        if (change.ChangeKind == TelemetryChangeKind.Remove)
        {
            // Removed outright rather than flagged unavailable: unlike a fan, a deleted profile is not
            // something that might come back when hardware reappears.
            profiles.RemoveKey(change.Profile.Id);
            return;
        }

        profiles.AddOrUpdate(ToProfile(change.Profile));
    }

    private static CoolingProfile ToProfile(CoolingProfileReply reply) => new()
    {
        Id = reply.Id,
        Name = reply.Name,
        IconName = string.IsNullOrWhiteSpace(reply.IconName) ? null : reply.IconName,
        AccentColorArgb = reply.HasAccentColorArgb ? reply.AccentColorArgb : null,
        IsSeeded = reply.IsSeeded,
        Fans = [.. reply.Fans.Select(static entry => new CoolingProfileFanEntry
        {
            FanIndex = entry.FanIndex,
            Mode = ToMode(entry.Mode),
            DutyPercent = entry.DutyPercent,
            AdaptiveTargetCelsius = entry.AdaptiveTargetCelsius,
            Aggregation = ToAggregation(entry.Aggregation),
            CurvePoints = entry.CurvePoints
                .GroupBy(static point => point.TemperatureCelsius)
                .ToImmutableSortedDictionary(
                    static group => group.Key,
                    static group => group.Last().FanDutyPercent),
            DrivingSensorIndices = [.. entry.DrivingSensorIndices],
        })],
    };

    private static CoolingProfileReply ToReply(CoolingProfile profile)
    {
        var reply = new CoolingProfileReply
        {
            Id = profile.Id,
            Name = profile.Name,
            IconName = profile.IconName ?? string.Empty,
            IsSeeded = profile.IsSeeded,
        };

        if (profile.AccentColorArgb is { } accent)
        {
            reply.AccentColorArgb = accent;
        }

        foreach (var entry in profile.Fans)
        {
            var entryReply = new CoolingProfileFanEntryReply
            {
                FanIndex = entry.FanIndex,
                Mode = ToModeValue(entry.Mode),
                DutyPercent = entry.DutyPercent,
                AdaptiveTargetCelsius = entry.AdaptiveTargetCelsius,
                Aggregation = ToAggregationValue(entry.Aggregation),
            };

            foreach (var point in entry.CurvePoints)
            {
                entryReply.CurvePoints.Add(new FanCurvePointReply
                {
                    TemperatureCelsius = point.Key,
                    FanDutyPercent = point.Value,
                });
            }

            entryReply.DrivingSensorIndices.AddRange(entry.DrivingSensorIndices);

            reply.Fans.Add(entryReply);
        }

        return reply;
    }

    private static FanControlMode ToMode(FanControlModeValue mode) => mode switch
    {
        FanControlModeValue.Manual => FanControlMode.Manual,
        FanControlModeValue.Max => FanControlMode.Max,
        FanControlModeValue.CustomCurve => FanControlMode.CustomCurve,
        FanControlModeValue.Adaptive => FanControlMode.Adaptive,
        _ => FanControlMode.Auto,
    };

    private static FanControlModeValue ToModeValue(FanControlMode mode) => mode switch
    {
        FanControlMode.Manual => FanControlModeValue.Manual,
        FanControlMode.Max => FanControlModeValue.Max,
        FanControlMode.CustomCurve => FanControlModeValue.CustomCurve,
        FanControlMode.Adaptive => FanControlModeValue.Adaptive,
        _ => FanControlModeValue.Auto,
    };

    private static TemperatureAggregationMode ToAggregation(TemperatureAggregationModeValue mode) => mode switch
    {
        TemperatureAggregationModeValue.Average => TemperatureAggregationMode.Average,
        TemperatureAggregationModeValue.Median => TemperatureAggregationMode.Median,
        TemperatureAggregationModeValue.Minimum => TemperatureAggregationMode.Minimum,
        _ => TemperatureAggregationMode.Maximum,
    };

    private static TemperatureAggregationModeValue ToAggregationValue(TemperatureAggregationMode mode) => mode switch
    {
        TemperatureAggregationMode.Average => TemperatureAggregationModeValue.Average,
        TemperatureAggregationMode.Median => TemperatureAggregationModeValue.Median,
        TemperatureAggregationMode.Minimum => TemperatureAggregationModeValue.Minimum,
        _ => TemperatureAggregationModeValue.Maximum,
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
