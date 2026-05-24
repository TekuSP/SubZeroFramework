using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkFanControlGrpcService : FrameworkFanControlService.FrameworkFanControlServiceBase
{
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly FrameworkServiceConfigurationStore _configurationStore;
    private readonly ILogger<FrameworkFanControlGrpcService> _logger;

    public FrameworkFanControlGrpcService(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlAuthorizationService authorizationService,
        FrameworkFanControlStateStore fanControlStateStore,
        FrameworkServiceConfigurationStore configurationStore,
        ILogger<FrameworkFanControlGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _fanControlStateStore = fanControlStateStore;
        _configurationStore = configurationStore;
        _logger = logger;
    }

    public override async Task<SetFanRpmReply> SetFanRpm(SetFanRpmRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanRpm command for fan {FanIndex} with target {TargetSpeedRpm} RPM.", request.FanIndex, request.TargetSpeedRpm);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanRpmAsync(request.FanIndex, request.TargetSpeedRpm, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkManual(request.FanIndex);
            await TryRemovePersistedCurveAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied SetFanRpm command for fan {FanIndex}. AppliedSpeedRpm={AppliedSpeedRpm}.", result.FanIndex, result.AppliedSpeedRpm);
            return new SetFanRpmReply
            {
                FanIndex = result.FanIndex,
                AppliedSpeedRpm = result.AppliedSpeedRpm,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanRpm command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanRpm command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<SetFanDutyReply> SetFanDuty(SetFanDutyRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanDuty command for fan {FanIndex} with target duty {DutyPercent}%.", request.FanIndex, request.DutyPercent);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanDutyAsync(request.FanIndex, request.DutyPercent, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkManual(request.FanIndex);
            _fanControlStateStore.RecordAppliedDuty(request.FanIndex, result.AppliedDutyPercent);
            await TryRemovePersistedCurveAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied SetFanDuty command for fan {FanIndex}. AppliedDutyPercent={AppliedDutyPercent}.", result.FanIndex, result.AppliedDutyPercent);
            return new SetFanDutyReply
            {
                FanIndex = result.FanIndex,
                AppliedDutyPercent = result.AppliedDutyPercent,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanDuty command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanDuty command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<SetFanMaxReply> SetFanMax(SetFanMaxRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanMax command for fan {FanIndex}.", request.FanIndex);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanDutyAsync(request.FanIndex, 100d, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkMax(request.FanIndex);
            _fanControlStateStore.RecordAppliedDuty(request.FanIndex, result.AppliedDutyPercent);
            await TryRemovePersistedCurveAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied SetFanMax command for fan {FanIndex}. AppliedDutyPercent={AppliedDutyPercent}.", result.FanIndex, result.AppliedDutyPercent);
            return new SetFanMaxReply
            {
                FanIndex = result.FanIndex,
                AppliedDutyPercent = result.AppliedDutyPercent,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanMax command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanMax command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<SetFanCustomCurveReply> SetFanCustomCurve(SetFanCustomCurveRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanCustomCurve command for fan {FanIndex} with {PointCount} points and {SensorCount} driving sensors.", request.FanIndex, request.CurvePoints.Count, request.DrivingSensorIndices.Count);
            _authorizationService.EnsureCommandAccess();

            if (request.CurvePoints.Count < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(request.CurvePoints), "Custom fan curve requires at least two points.");
            }

            if (!TelemetryGrpcMapper.TryParseTemperatureAggregationMode(request.DrivingTemperatureAggregation, out var aggregationMode))
            {
                aggregationMode = TemperatureAggregationMode.Average;
            }

            var points = request.CurvePoints.ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var sensors = request.DrivingSensorIndices.ToArray();

            _fanControlStateStore.SetCustomCurve(request.FanIndex, points, aggregationMode, sensors);

            try
            {
                await _configurationStore.UpsertFanControlStateAsync(
                    new FanControlStateOptions
                    {
                        FanIndex = request.FanIndex,
                        Mode = FanControlMode.CustomCurve,
                        CustomCurvePoints = points,
                        DrivingTemperatureAggregation = aggregationMode,
                        DrivingSensorIndices = sensors,
                    },
                    context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception persistenceException)
            {
                _logger.LogWarning(persistenceException, "Applied SetFanCustomCurve for fan {FanIndex} but failed to persist the curve to the service configuration store. The curve will not survive a service restart.", request.FanIndex);
            }

            _logger.LogInformation("Applied SetFanCustomCurve command for fan {FanIndex}.", request.FanIndex);

            return new SetFanCustomCurveReply
            {
                FanIndex = request.FanIndex,
                Succeeded = true,
                Message = string.Empty,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanCustomCurve command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanCustomCurve command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<RestoreAutoFanControlReply> RestoreAutoFanControl(RestoreAutoFanControlRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received RestoreAutoFanControl command for fan {FanIndex}.", request.FanIndex);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.RestoreAutoFanControlAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkAuto(request.FanIndex);
            await TryRemovePersistedCurveAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied RestoreAutoFanControl command for fan {FanIndex}.", result.FanIndex);
            return new RestoreAutoFanControlReply
            {
                FanIndex = result.FanIndex,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected RestoreAutoFanControl command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected RestoreAutoFanControl command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    private async Task TryRemovePersistedCurveAsync(int fanIndex, CancellationToken cancellationToken)
    {
        try
        {
            await _configurationStore.RemoveFanControlStateAsync(fanIndex, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to remove persisted fan control state for fan {FanIndex} after a mode change. A stale curve may be restored on the next service start.", fanIndex);
        }
    }
}
