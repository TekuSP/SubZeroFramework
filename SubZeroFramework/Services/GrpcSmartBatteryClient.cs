using FrameworkDotnet.Enums;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

/// <summary>
/// gRPC consumer of <c>GetSmartBattery</c>.
/// </summary>
/// <remarks>
/// Unary, unlike its neighbours in this folder, because the pack read is expensive and must happen only when
/// a person asks for it. There is deliberately no observable here to subscribe to: exposing one would invite
/// exactly the polling this is meant to avoid.
/// </remarks>
public sealed class GrpcSmartBatteryClient : ISmartBatteryClient
{
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _client;

    public GrpcSmartBatteryClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _client = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(channelFactory.Channel);
    }

    public async Task<SmartBatteryStatus> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var reply = await _client
                .GetSmartBatteryAsync(new GetSmartBatteryRequest(), cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            if (!reply.IsAvailable)
            {
                return new SmartBatteryStatus { IsAvailable = false };
            }

            return new SmartBatteryStatus
            {
                IsAvailable = true,
                SerialNumber = (ushort)reply.SerialNumber,
                ManufactureDate = reply.HasManufactureDateDayNumber
                    ? DateOnly.FromDayNumber(reply.ManufactureDateDayNumber)
                    : null,
                DeviceName = reply.DeviceName,
                ManufacturerName = reply.ManufacturerName,
                Chemistry = reply.Chemistry,
                TemperatureCelsius = reply.TemperatureCelsius,
                VoltageVolts = reply.VoltageVolts,
                CurrentAmperes = reply.CurrentAmperes,
                CycleCount = reply.CycleCount,
                RelativeStateOfChargePercent = reply.RelativeStateOfChargePercent,
                CellVoltageVolts1 = reply.CellVoltageVolts1,
                CellVoltageVolts2 = reply.CellVoltageVolts2,
                CellVoltageVolts3 = reply.CellVoltageVolts3,
                CellVoltageVolts4 = reply.CellVoltageVolts4,
                ChargingVoltageVolts = reply.ChargingVoltageVolts,
                ChargingCurrentAmperes = reply.ChargingCurrentAmperes,
                IsUnsealed = reply.IsUnsealed,
                StateOfHealthEnergyWattHours = reply.HasStateOfHealthEnergyWattHours
                    ? reply.StateOfHealthEnergyWattHours
                    : null,

                // Parsed by name, with a fallback rather than a throw: a newer service naming a state this
                // build does not know must not break a page that only displays it.
                CutoffState = Enum.TryParse<FrameworkBatteryCutoffState>(reply.CutoffState, out var cutoff)
                    ? cutoff
                    : FrameworkBatteryCutoffState.Unknown,
                IsCharging = reply.IsCharging,
                IsAcPresent = reply.IsAcPresent,
                ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            };
        }
        catch (RpcException)
        {
            // An unreachable service is an unavailable pack, not a crash. The caller renders the same empty
            // state it would for a machine whose battery genuinely cannot be read.
            return new SmartBatteryStatus { IsAvailable = false };
        }
    }
}
