using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkFanControlGrpcService : FrameworkFanControlService.FrameworkFanControlServiceBase
{
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly ILogger<FrameworkFanControlGrpcService> _logger;

    public FrameworkFanControlGrpcService(IFrameworkDataProvider frameworkDataProvider, FrameworkFanControlAuthorizationService authorizationService, FrameworkFanControlStateStore fanControlStateStore, ILogger<FrameworkFanControlGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _fanControlStateStore = fanControlStateStore;
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

    public override async Task<RestoreAutoFanControlReply> RestoreAutoFanControl(RestoreAutoFanControlRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received RestoreAutoFanControl command for fan {FanIndex}.", request.FanIndex);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.RestoreAutoFanControlAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkAuto(request.FanIndex);
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
}
