using System.Threading.Channels;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkServiceConfigurationGrpcService : FrameworkServiceConfigurationService.FrameworkServiceConfigurationServiceBase
{
    private readonly FrameworkServiceConfigurationManager _configurationManager;
    private readonly InMemoryLogBuffer _logBuffer;
    private readonly ILogger<FrameworkServiceConfigurationGrpcService> _logger;

    public FrameworkServiceConfigurationGrpcService(
        FrameworkServiceConfigurationManager configurationManager,
        InMemoryLogBuffer logBuffer,
        ILogger<FrameworkServiceConfigurationGrpcService> logger)
    {
        ArgumentNullException.ThrowIfNull(configurationManager);
        ArgumentNullException.ThrowIfNull(logBuffer);
        ArgumentNullException.ThrowIfNull(logger);

        _configurationManager = configurationManager;
        _logBuffer = logBuffer;
        _logger = logger;
    }

    /// <summary>
    /// Returns the service's own log since it started, so the app can show it without the user opening Event
    /// Viewer or journalctl. Read-only, and deliberately NOT logged at Information: logging every fetch would
    /// add an entry to the very buffer being fetched.
    /// </summary>
    public override Task<ServiceLogsReply> GetServiceLogs(GetServiceLogsRequest request, ServerCallContext context)
    {
        var (entries, droppedCount) = _logBuffer.Snapshot();
        var minimumLevel = (LogLevel)Math.Clamp(request.MinimumLevel, (int)LogLevel.Trace, (int)LogLevel.Critical);

        var reply = new ServiceLogsReply
        {
            DroppedCount = droppedCount,
            BufferCapacity = InMemoryLogBuffer.Capacity,
        };

        foreach (var entry in entries)
        {
            if (entry.Level < minimumLevel)
            {
                continue;
            }

            reply.Entries.Add(new ServiceLogEntryReply
            {
                ObservedAtUnixTimeMilliseconds = entry.ObservedAt.ToUnixTimeMilliseconds(),
                Level = (int)entry.Level,
                Category = entry.Category,
                Message = entry.Message,
                Exception = entry.Exception,
            });
        }

        return Task.FromResult(reply);
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

            var updates = Channel.CreateUnbounded<FrameworkServiceConfigurationReply>();
            using var subscription = _configurationManager.WatchSnapshot().Subscribe(snapshot =>
            {
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

    public override async Task<ServiceConfigurationOperationReply> ApplyServiceConfiguration(ApplyServiceConfigurationRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "Received ApplyServiceConfiguration request. PollingIntervalMilliseconds={PollingIntervalMilliseconds}, HardwareInfoPollingIntervalMilliseconds={HardwareInfoPollingIntervalMilliseconds}, AllowFanControlCommands={AllowFanControlCommands}.",
            request.PollingIntervalMilliseconds,
            request.HardwareInfoPollingIntervalMilliseconds,
            request.AllowFanControlCommands);

        var result = await _configurationManager.ApplyAsync(
            new FrameworkServiceConfigurationApplyRequest
            {
                PollingInterval = TimeSpan.FromMilliseconds(request.PollingIntervalMilliseconds),
                HardwareInfoPollingInterval = TimeSpan.FromMilliseconds(request.HardwareInfoPollingIntervalMilliseconds),
                AllowFanControlCommands = request.AllowFanControlCommands,
            },
            context.CancellationToken).ConfigureAwait(false);

        return MapResult(result);
    }

    public override async Task<ServiceConfigurationOperationReply> SaveServiceConfiguration(SaveServiceConfigurationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received SaveServiceConfiguration request.");
        var result = await _configurationManager.SaveAsync(context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    public override async Task<ServiceConfigurationOperationReply> LoadServiceConfiguration(LoadServiceConfigurationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received LoadServiceConfiguration request.");
        var result = await _configurationManager.LoadAsync(context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    public override async Task<ServiceConfigurationOperationReply> RelocateServiceConfiguration(RelocateServiceConfigurationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received RelocateServiceConfiguration request. TargetDirectory={TargetDirectory}.", request.TargetDirectory);
        var result = await _configurationManager.RelocateAsync(request.TargetDirectory ?? string.Empty, context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    private static ServiceConfigurationOperationReply MapResult(FrameworkServiceConfigurationOperationResult result)
    {
        return new ServiceConfigurationOperationReply
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
