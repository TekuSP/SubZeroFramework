using System.Reactive.Linq;

using DynamicData;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkTelemetryGrpcService : FrameworkTelemetryService.FrameworkTelemetryServiceBase
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly FrameworkCoolingProfileStore _coolingProfileStore;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<FrameworkTelemetryGrpcService> _logger;

    public FrameworkTelemetryGrpcService(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlStateStore fanControlStateStore,
        FrameworkCoolingProfileStore coolingProfileStore,
        IHostApplicationLifetime applicationLifetime,
        ILogger<FrameworkTelemetryGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _fanControlStateStore = fanControlStateStore;
        _coolingProfileStore = coolingProfileStore;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public override async Task WatchTelemetryChannels(WatchTelemetryChannelsRequest request, IServerStreamWriter<TelemetryChannelChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening telemetry channel stream.");
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        await GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectTelemetryChannels(),
            responseStream,
            TelemetryGrpcMapper.MapChannelChange,
            TelemetryGrpcMapper.MapChannelBatch,
            streamCancellation.Token,
            _logger,
            "telemetry channel stream").ConfigureAwait(false);
    }

    public override async Task WatchFanCapabilities(WatchFanCapabilitiesRequest request, IServerStreamWriter<FanCapabilityChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening fan capability stream.");
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        await GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectFanCapabilities(),
            responseStream,
            TelemetryGrpcMapper.MapFanCapabilityChange,
            TelemetryGrpcMapper.MapFanCapabilityBatch,
            streamCancellation.Token,
            _logger,
            "fan capability stream").ConfigureAwait(false);
    }

    public override async Task WatchFanControlStates(WatchFanControlStatesRequest request, IServerStreamWriter<FanControlStateChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening fan control state stream.");
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        await GrpcChangeSetWriter.WriteAsync(
            _fanControlStateStore.Connect(),
            responseStream,
            TelemetryGrpcMapper.MapFanControlStateChange,
            TelemetryGrpcMapper.MapFanControlStateBatch,
            streamCancellation.Token,
            _logger,
            "fan control state stream").ConfigureAwait(false);
    }

    /// <summary>
    /// Streams the cooling profile library and which profile is selected.
    /// </summary>
    /// <remarks>
    /// A bespoke loop rather than <see cref="GrpcChangeSetWriter"/> because TWO things have to reach the
    /// client: the library, and the selection. Selecting a profile changes no profile record, so a
    /// change-set-only stream would leave every other client still showing the previous selection — and
    /// still tinted by it — until something unrelated happened to the library.
    /// </remarks>
    public override async Task WatchCoolingProfiles(WatchCoolingProfilesRequest request, IServerStreamWriter<CoolingProfileChangeBatchReply> responseStream, ServerCallContext context)
    {
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        var streamToken = streamCancellation.Token;

        try
        {
            _logger.LogInformation("Opening cooling profile stream.");

            var library = _coolingProfileStore.Connect().Select(BuildCoolingProfileBatch);

            // The selection subject replays its current value on subscribe, so this is also what gives a
            // newly connected client the selection without a separate request.
            var selection = _coolingProfileStore.ConnectActiveProfileId()
                .Select(activeProfileId => new CoolingProfileChangeBatchReply
                {
                    ActiveProfileId = activeProfileId ?? string.Empty,
                });

            var reader = ObservableChannelBridge.CreateBoundedReader(
                library.Merge(selection), streamToken, _logger, "cooling profile stream");

            while (await reader.WaitToReadAsync(streamToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var batch))
                {
                    await responseStream.WriteAsync(batch, streamToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping cooling profile stream because the request was cancelled or the service is stopping.");
        }
    }

    /// <summary>
    /// One change set as a batch, stamped with the current selection.
    /// </summary>
    /// <remarks>
    /// Every batch carries the selection so a client that reconnects mid-session learns it from the first
    /// message it receives, whichever kind that turns out to be.
    /// </remarks>
    private CoolingProfileChangeBatchReply BuildCoolingProfileBatch(IChangeSet<CoolingProfile, string> changes)
    {
        var batch = new CoolingProfileChangeBatchReply
        {
            ActiveProfileId = _coolingProfileStore.ActiveProfileId ?? string.Empty,
        };

        foreach (var change in changes)
        {
            batch.Changes.Add(new CoolingProfileChangeReply
            {
                ChangeKind = change.Reason == ChangeReason.Remove
                    ? TelemetryChangeKind.Remove
                    : TelemetryChangeKind.Upsert,

                // Current carries the removed profile on a Remove too, which is what lets the client match it
                // by id without keeping a shadow copy of the library.
                Profile = CoolingProfileProtoMapper.ToReply(change.Current),
            });
        }

        return batch;
    }

    /// <summary>
    /// Reads the battery pack's own registers, on demand.
    /// </summary>
    /// <remarks>
    /// Unary rather than streamed, and deliberately so: the read costs many I2C round trips and holds the
    /// passthrough while it runs, so it must happen only when a person asks for it. The provider rate-limits
    /// repeats, so a leaned-on refresh button cannot queue reads behind each other.
    /// </remarks>
    public override async Task<SmartBatteryReply> GetSmartBattery(GetSmartBatteryRequest request, ServerCallContext context)
    {
        var pack = await _frameworkDataProvider.ReadSmartBatteryAsync(context.CancellationToken).ConfigureAwait(false);

        if (pack is null)
        {
            // Not an RPC error: a machine whose pack cannot be read is not a broken service, and reporting it
            // as a fault would put an error banner on a page whose other half is working fine.
            return new SmartBatteryReply { IsAvailable = false };
        }

        var reply = new SmartBatteryReply
        {
            IsAvailable = true,
            SerialNumber = pack.SerialNumber,
            DeviceName = pack.DeviceName,
            ManufacturerName = pack.ManufacturerName,
            Chemistry = pack.Chemistry,
            TemperatureCelsius = pack.TemperatureCelsius,
            VoltageVolts = pack.VoltageVolts,
            CurrentAmperes = pack.CurrentAmperes,
            CycleCount = pack.CycleCount,
            RelativeStateOfChargePercent = pack.RelativeStateOfChargePercent,
            CellVoltageVolts1 = pack.CellVoltageVolts1,
            CellVoltageVolts2 = pack.CellVoltageVolts2,
            CellVoltageVolts3 = pack.CellVoltageVolts3,
            CellVoltageVolts4 = pack.CellVoltageVolts4,
            ChargingVoltageVolts = pack.ChargingVoltageVolts,
            ChargingCurrentAmperes = pack.ChargingCurrentAmperes,
            IsUnsealed = pack.IsUnsealed,
            CutoffState = pack.CutoffState.ToString(),
            IsCharging = pack.IsCharging,
            IsAcPresent = pack.IsAcPresent,
            ObservedAtUnixTimeMilliseconds = pack.ObservedAt.ToUnixTimeMilliseconds(),
        };

        if (pack.ManufactureDate is { } manufactured)
        {
            reply.ManufactureDateDayNumber = manufactured.DayNumber;
        }

        if (pack.StateOfHealthEnergyWattHours is double stateOfHealth)
        {
            reply.StateOfHealthEnergyWattHours = stateOfHealth;
        }

        return reply;
    }

    public override async Task WatchPowerDelivery(WatchPowerDeliveryRequest request, IServerStreamWriter<PowerDeliveryReply> responseStream, ServerCallContext context)
    {
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        var streamToken = streamCancellation.Token;

        try
        {
            _logger.LogInformation("Opening power delivery stream.");

            // The provider's snapshot stream replays the latest value on subscribe, so no separate initial write.
            var reader = ObservableChannelBridge.CreateBoundedReader(
                _frameworkDataProvider.PowerDeliverySnapshots, streamToken, _logger, "power delivery stream");

            while (await reader.WaitToReadAsync(streamToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var snapshot))
                {
                    await responseStream.WriteAsync(MapPowerDelivery(snapshot), streamToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping power delivery stream because the request was cancelled.");
        }
    }

    private static PowerDeliveryReply MapPowerDelivery(PowerDeliverySnapshot snapshot)
    {
        var reply = new PowerDeliveryReply();
        foreach (var port in snapshot.Ports)
        {
            var state = new PowerDeliveryPortState
            {
                SlotIndex = port.SlotIndex,
                IsPresent = port.IsPresent,
                IsActivePort = port.IsActivePort,
                HasPowerDeliveryContract = port.HasPowerDeliveryContract,
                CState = port.CState.ToString(),
                PowerRole = port.PowerRole.ToString(),
                DataRole = port.DataRole.ToString(),
                CcPolarity = port.CcPolarity.ToString(),
                VoltageVolts = port.VoltageVolts,
                CurrentAmperes = port.CurrentAmperes,
                IsVconnActive = port.IsVconnActive,
                IsEprActive = port.IsEprActive,
                IsEprSupported = port.IsEprSupported,
                AltModeFlags = port.AltModeFlags,
                CardType = port.CardType.ToString(),
                DataLane = port.DataLane.ToString(),
                DisplayPortCapability = port.DisplayPortCapability.ToString(),
                CapabilitySupportsCharging = port.SupportsCharging,
                MaxChargeWatts = port.MaxChargeWatts,
                UsbAHighPower = port.UsbAHighPower,
                CapabilityDocumented = port.CapabilityDocumented,
                PortSource = port.PortSource,
                PortPosition = port.PortPosition,
                PortIsLeft = port.PortIsLeft,
                SupportsDualRole = port.SupportsDualRole,
                UsbPowerRole = port.UsbPowerRole.ToString(),
                ChargingType = port.ChargingType.ToString(),
            };

            // Assigned after construction because the generated setters for proto3 `optional double` take a
            // plain double: an unset field is how "no contract reported" is expressed on the wire, and a slot
            // with no PD controller behind it must reach the client as absent rather than as zero volts.
            if (port.NegotiatedMaximumVoltageVolts is double maximumVoltage)
            {
                state.NegotiatedMaximumVoltageVolts = maximumVoltage;
            }

            if (port.NegotiatedMaximumCurrentAmperes is double maximumCurrent)
            {
                state.NegotiatedMaximumCurrentAmperes = maximumCurrent;
            }

            if (port.NegotiatedMaximumPowerWatts is double maximumPower)
            {
                state.NegotiatedMaximumPowerWatts = maximumPower;
            }

            if (port.CurrentLimitAmperes is double currentLimit)
            {
                state.CurrentLimitAmperes = currentLimit;
            }

            reply.Ports.Add(state);
        }

        return reply;
    }

    public override async Task WatchModuleInventory(WatchModuleInventoryRequest request, IServerStreamWriter<ModuleInventoryReply> responseStream, ServerCallContext context)
    {
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        var streamToken = streamCancellation.Token;

        try
        {
            _logger.LogInformation("Opening module inventory stream.");

            // The provider's snapshot stream replays the latest value on subscribe, so no separate initial write.
            var reader = ObservableChannelBridge.CreateBoundedReader(
                _frameworkDataProvider.ModuleInventorySnapshots, streamToken, _logger, "module inventory stream");

            while (await reader.WaitToReadAsync(streamToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var snapshot))
                {
                    await responseStream.WriteAsync(MapModuleInventory(snapshot), streamToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping module inventory stream because the request was cancelled.");
        }
    }

    private static ModuleInventoryReply MapModuleInventory(ModuleInventorySnapshot snapshot)
    {
        var reply = new ModuleInventoryReply
        {
            HasExpansionBay = snapshot.ExpansionBayModule is not null,
            ExpansionBayBoard = snapshot.ExpansionBayBoard.ToString(),
            ExpansionBayVendor = snapshot.ExpansionBayVendor.ToString(),
            ExpansionBaySerial = snapshot.ExpansionBaySerialNumber,
        };

        foreach (var slot in snapshot.UsbCSlots)
        {
            reply.UsbCSlots.Add(MapModuleDescriptor(slot));
        }

        foreach (var module in snapshot.InputDeckModules)
        {
            reply.InputDeck.Add(MapModuleDescriptor(module));
        }

        foreach (var module in snapshot.InternalModules)
        {
            reply.InternalFixed.Add(MapModuleDescriptor(module));
        }

        foreach (var module in snapshot.DetachedModules)
        {
            reply.Detached.Add(MapModuleDescriptor(module));
        }

        if (snapshot.ExpansionBayModule is { } bayModule)
        {
            reply.ExpansionBay = MapModuleDescriptor(bayModule);
        }

        return reply;
    }

    private static ModuleDescriptor MapModuleDescriptor(ModuleDescriptorSnapshot descriptor) => new()
    {
        Identity = descriptor.Identity.ToString(),
        Bus = descriptor.Bus.ToString(),
        SlotKind = descriptor.SlotKind.ToString(),
        Confidence = descriptor.Confidence.ToString(),
        IsPresent = descriptor.IsPresent,
        SlotIndex = descriptor.SlotIndex,
        Flags = (uint)descriptor.Flags,
        VendorId = descriptor.VendorId,
        ProductId = descriptor.ProductId,
        BoardId = descriptor.BoardId,
        Position = descriptor.Position.ToString(),
        CardType = descriptor.CardType.ToString(),
        CardConfidence = descriptor.CardConfidence.ToString(),
    };

    public override async Task WatchFanStates(WatchFanStatesRequest request, IServerStreamWriter<FanStateChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening fan state stream.");
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        await GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectFanStates(),
            responseStream,
            TelemetryGrpcMapper.MapFanStateChange,
            TelemetryGrpcMapper.MapFanStateBatch,
            streamCancellation.Token,
            _logger,
            "fan state stream").ConfigureAwait(false);
    }

    public override async Task WatchCurrentTelemetryValues(WatchCurrentTelemetryValuesRequest request, IServerStreamWriter<CurrentTelemetryValueChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening current telemetry value stream.");
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        await GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectCurrentTelemetryValues(),
            responseStream,
            TelemetryGrpcMapper.MapCurrentValueChange,
            TelemetryGrpcMapper.MapCurrentValueBatch,
            streamCancellation.Token,
            _logger,
            "current telemetry value stream").ConfigureAwait(false);
    }

    public override async Task WatchTelemetrySeries(WatchTelemetrySeriesRequest request, IServerStreamWriter<TelemetrySeriesPointChangeBatchReply> responseStream, ServerCallContext context)
    {
        if (!TelemetryGrpcMapper.TryParseTelemetryArea(request.Area, out var area)
            || !TelemetryGrpcMapper.TryParseTelemetryEntityKind(request.EntityKind, out var entityKind)
            || !TelemetryGrpcMapper.TryParseTelemetryMetric(request.Metric, out var metric))
        {
            _logger.LogWarning("Rejected telemetry series request because the requested channel was invalid. Area={Area}, EntityKind={EntityKind}, Metric={Metric}, Index={Index}.", request.Area, request.EntityKind, request.Metric, request.Index);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The requested telemetry channel is invalid."));
        }

        var requestedHistoryWindow = TimeSpan.FromSeconds(request.HistoryWindowSeconds);
        if (requestedHistoryWindow <= TimeSpan.Zero || requestedHistoryWindow > TelemetryHistoryLimits.MaximumHistoryWindow)
        {
            _logger.LogWarning("Rejected telemetry series request because the requested history window {HistoryWindowSeconds}s is outside the supported range.", request.HistoryWindowSeconds);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The telemetry history window must be between 1 second and 1 hour."));
        }

        var channelId = new TelemetryChannelId(area, entityKind, request.Index, metric);
        _logger.LogInformation("Opening telemetry series stream for {ChannelId} with history window {HistoryWindowSeconds}s.", channelId, request.HistoryWindowSeconds);
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        await GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectTelemetrySeries(channelId, requestedHistoryWindow),
            responseStream,
            TelemetryGrpcMapper.MapTelemetryPointChange,
            TelemetryGrpcMapper.MapTelemetryPointBatch,
            streamCancellation.Token,
            _logger,
            $"telemetry series stream for {channelId}").ConfigureAwait(false);
    }
}
