using System.Threading.Channels;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkUserPreferencesGrpcService : FrameworkUserPreferencesService.FrameworkUserPreferencesServiceBase
{
    private readonly FrameworkUserPreferencesManager _manager;
    private readonly ILogger<FrameworkUserPreferencesGrpcService> _logger;

    public FrameworkUserPreferencesGrpcService(
        FrameworkUserPreferencesManager manager,
        ILogger<FrameworkUserPreferencesGrpcService> logger)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(logger);

        _manager = manager;
        _logger = logger;
    }

    public override Task<UserPreferencesReply> GetUserPreferences(GetUserPreferencesRequest request, ServerCallContext context)
    {
        _logger.LogDebug("Received GetUserPreferences request.");
        return Task.FromResult(MapState(_manager.GetCurrentState()));
    }

    public override async Task WatchUserPreferences(WatchUserPreferencesRequest request, IServerStreamWriter<UserPreferencesReply> responseStream, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Opening user preferences stream.");

            var updates = Channel.CreateUnbounded<UserPreferencesReply>();
            using var subscription = _manager.WatchState().Subscribe(state =>
            {
                updates.Writer.TryWrite(MapState(state));
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
            _logger.LogDebug("Stopping user preferences stream because the request was cancelled.");
        }
    }

    public override async Task<UserPreferencesOperationReply> ApplyUserPreferences(ApplyUserPreferencesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received ApplyUserPreferences request with {EntryCount} entries.", request.Entries.Count);

        var snapshot = new UserUnitPreferencesSnapshot
        {
            SchemaVersion = request.SchemaVersion > 0 ? request.SchemaVersion : UserUnitPreferencesSnapshot.CurrentSchemaVersion,
            Entries =
            [
                .. request.Entries
                    .Where(entry => TryParseKind(entry.Kind, out _))
                    .Select(entry => new UserUnitPreferenceEntry(ParseKind(entry.Kind), entry.OptionKey ?? string.Empty))
            ],
        };

        var result = await _manager.ApplyAsync(snapshot, context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    public override async Task<UserPreferencesOperationReply> SaveUserPreferences(SaveUserPreferencesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received SaveUserPreferences request.");
        var result = await _manager.SaveAsync(context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    public override async Task<UserPreferencesOperationReply> LoadUserPreferences(LoadUserPreferencesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received LoadUserPreferences request.");
        var result = await _manager.LoadAsync(context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    public override async Task<UserPreferencesOperationReply> ResetUserPreferences(ResetUserPreferencesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received ResetUserPreferences request.");
        var result = await _manager.ResetToDefaultsAsync(context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    public override async Task<UserPreferencesOperationReply> RelocateUserPreferences(RelocateUserPreferencesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received RelocateUserPreferences request. TargetDirectory={TargetDirectory}.", request.TargetDirectory);
        var result = await _manager.RelocateAsync(request.TargetDirectory ?? string.Empty, context.CancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    private static UserPreferencesOperationReply MapResult(UserPreferencesOperationResult result)
    {
        return new UserPreferencesOperationReply
        {
            Succeeded = result.Succeeded,
            Message = result.Message,
            Preferences = MapSnapshot(result.Preferences, result.PreferencesPath),
        };
    }

    private static UserPreferencesReply MapState(UserPreferencesState state)
    {
        return MapSnapshot(state.Preferences, state.PreferencesPath);
    }

    private static UserPreferencesReply MapSnapshot(UserUnitPreferencesSnapshot snapshot, string preferencesPath)
    {
        var reply = new UserPreferencesReply
        {
            SchemaVersion = snapshot.SchemaVersion,
            PreferencesPath = preferencesPath ?? string.Empty,
        };

        foreach (var entry in snapshot.Entries)
        {
            reply.Entries.Add(new UserPreferenceEntryReply
            {
                Kind = entry.Kind.ToString(),
                OptionKey = entry.OptionKey ?? string.Empty,
            });
        }

        return reply;
    }

    private static bool TryParseKind(string? kind, out UnitQuantityKind parsed)
        => Enum.TryParse(kind, ignoreCase: true, out parsed);

    private static UnitQuantityKind ParseKind(string? kind)
        => Enum.TryParse<UnitQuantityKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : UnitQuantityKind.Temperature;
}
