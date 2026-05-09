using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFrameworkFanControlClient : IFrameworkFanControlClient
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkFanControlService.FrameworkFanControlServiceClient _client;

    public GrpcFrameworkFanControlClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkFanControlService.FrameworkFanControlServiceClient(_channelFactory.Channel);
    }

    /// <summary>
    /// Sets the fan speed target in RPM.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="targetSpeedRpm">The requested fan speed in RPM.</param>
    public async Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetFanRpmAsync(new SetFanRpmRequest
        {
            FanIndex = fanIndex,
            TargetSpeedRpm = targetSpeedRpm,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanRpmCommandResult
        {
            FanIndex = reply.FanIndex,
            AppliedSpeedRpm = reply.AppliedSpeedRpm,
        };
    }

    /// <summary>
    /// Sets the fan duty cycle percent.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="dutyPercent">The requested duty cycle percent.</param>
    public async Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetFanDutyAsync(new SetFanDutyRequest
        {
            FanIndex = fanIndex,
            DutyPercent = dutyPercent,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanDutyCommandResult
        {
            FanIndex = reply.FanIndex,
            AppliedDutyPercent = reply.AppliedDutyPercent,
        };
    }

    /// <summary>
    /// Restores automatic fan control for the specified fan.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    public async Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.RestoreAutoFanControlAsync(new RestoreAutoFanControlRequest
        {
            FanIndex = fanIndex,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkRestoreAutoFanControlCommandResult
        {
            FanIndex = reply.FanIndex,
        };
    }
}
