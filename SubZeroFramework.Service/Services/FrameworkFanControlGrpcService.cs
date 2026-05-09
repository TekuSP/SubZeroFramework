using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkFanControlGrpcService : FrameworkFanControlService.FrameworkFanControlServiceBase
{
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;

    public FrameworkFanControlGrpcService(IFrameworkDataProvider frameworkDataProvider, FrameworkFanControlAuthorizationService authorizationService, FrameworkFanControlStateStore fanControlStateStore)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _fanControlStateStore = fanControlStateStore;
    }

    public override async Task<SetFanRpmReply> SetFanRpm(SetFanRpmRequest request, ServerCallContext context)
    {
        try
        {
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanRpmAsync(request.FanIndex, request.TargetSpeedRpm, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkManual(request.FanIndex);
            return new SetFanRpmReply
            {
                FanIndex = result.FanIndex,
                AppliedSpeedRpm = result.AppliedSpeedRpm,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<SetFanDutyReply> SetFanDuty(SetFanDutyRequest request, ServerCallContext context)
    {
        try
        {
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanDutyAsync(request.FanIndex, request.DutyPercent, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkManual(request.FanIndex);
            return new SetFanDutyReply
            {
                FanIndex = result.FanIndex,
                AppliedDutyPercent = result.AppliedDutyPercent,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<RestoreAutoFanControlReply> RestoreAutoFanControl(RestoreAutoFanControlRequest request, ServerCallContext context)
    {
        try
        {
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.RestoreAutoFanControlAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkAuto(request.FanIndex);
            return new RestoreAutoFanControlReply
            {
                FanIndex = result.FanIndex,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }
}
