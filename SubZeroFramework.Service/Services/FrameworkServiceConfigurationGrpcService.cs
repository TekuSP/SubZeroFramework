using System.Threading.Channels;

using Grpc.Core;

using Microsoft.Extensions.Options;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkServiceConfigurationGrpcService : FrameworkServiceConfigurationService.FrameworkServiceConfigurationServiceBase
{
    private readonly FrameworkServiceConfigurationManager _configurationManager;
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;
    private readonly ILogger<FrameworkServiceConfigurationGrpcService> _logger;

    public FrameworkServiceConfigurationGrpcService(
        FrameworkServiceConfigurationManager configurationManager,
        IOptionsMonitor<FrameworkServiceOptions> optionsMonitor,
        ILogger<FrameworkServiceConfigurationGrpcService> logger)
    {
        ArgumentNullException.ThrowIfNull(configurationManager);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _configurationManager = configurationManager;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public override Task<FrameworkServiceConfigurationReply> GetServiceConfiguration(GetServiceConfigurationRequest request, ServerCallContext context)
    {
        _logger.LogDebug("Received GetServiceConfiguration request.");
        return Task.FromResult(MapConfiguration(_configurationManager.GetCurrentSnapshot()));
    }

    public override async Task WatchServiceConfiguration(WatchServiceConfigurationRequest request, IServerStreamWriter<FrameworkServiceConfigurationReply> responseStream, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Opening service configuration stream.");
            await responseStream.WriteAsync(MapConfiguration(_configurationManager.GetCurrentSnapshot()), context.CancellationToken).ConfigureAwait(false);

            var updates = Channel.CreateUnbounded<FrameworkServiceConfigurationReply>();
            using var optionsSubscription = _optionsMonitor.OnChange(_ =>
            {
                var snapshot = _configurationManager.GetCurrentSnapshot();
                updates.Writer.TryWrite(MapConfiguration(snapshot));
            });

            while (await updates.Reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
            {
                while (updates.Reader.TryRead(out var reply))
                {
                    await responseStream.WriteAsync(reply, context.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping service configuration stream because the request was cancelled.");
        }
    }

    public override async Task<UpdateServiceConfigurationReply> UpdateServiceConfiguration(UpdateServiceConfigurationRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "Received UpdateServiceConfiguration request. PollingIntervalMilliseconds={PollingIntervalMilliseconds}, HardwareInfoPollingIntervalMilliseconds={HardwareInfoPollingIntervalMilliseconds}, AllowFanControlCommands={AllowFanControlCommands}.",
            request.PollingIntervalMilliseconds,
            request.HardwareInfoPollingIntervalMilliseconds,
            request.AllowFanControlCommands);

        var result = await _configurationManager.UpdateAsync(
            new FrameworkServiceConfigurationUpdateRequest
            {
                PollingInterval = TimeSpan.FromMilliseconds(request.PollingIntervalMilliseconds),
                HardwareInfoPollingInterval = TimeSpan.FromMilliseconds(request.HardwareInfoPollingIntervalMilliseconds),
                AllowFanControlCommands = request.AllowFanControlCommands,
            },
            context.CancellationToken).ConfigureAwait(false);

        return new UpdateServiceConfigurationReply
        {
            Succeeded = result.Succeeded,
            Message = result.Message,
            Configuration = MapConfiguration(result.Configuration),
        };
    }

    private static FrameworkServiceConfigurationReply MapConfiguration(FrameworkServiceConfigurationSnapshot snapshot)
    {
        return new FrameworkServiceConfigurationReply
        {
            PollingIntervalMilliseconds = checked((long)Math.Round(snapshot.PollingInterval.TotalMilliseconds, MidpointRounding.AwayFromZero)),
            HardwareInfoPollingIntervalMilliseconds = checked((long)Math.Round(snapshot.HardwareInfoPollingInterval.TotalMilliseconds, MidpointRounding.AwayFromZero)),
            AllowFanControlCommands = snapshot.AllowFanControlCommands,
            PersistentConfigurationPath = snapshot.PersistentConfigurationPath,
        };
    }
}
