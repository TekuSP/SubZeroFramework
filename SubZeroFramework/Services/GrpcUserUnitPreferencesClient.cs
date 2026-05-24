using System.Reactive.Disposables;
using System.Reactive.Linq;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Services;

public sealed class GrpcUserUnitPreferencesClient : IUserUnitPreferencesClient, IDisposable
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly UnitPreferenceCatalog _catalog;
    private readonly FrameworkUserPreferencesService.FrameworkUserPreferencesServiceClient _client;
    private readonly IObservable<UserUnitPreferencesSnapshot> _sharedPreferencesStream;
    private readonly CompositeDisposable _subscriptions = new();
    private UserUnitPreferencesSnapshot _currentPreferences;
    private string _preferencesFilePath = string.Empty;

    public GrpcUserUnitPreferencesClient(FrameworkGrpcChannelFactory channelFactory, UnitPreferenceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);
        ArgumentNullException.ThrowIfNull(catalog);

        _channelFactory = channelFactory;
        _catalog = catalog;
        _client = new FrameworkUserPreferencesService.FrameworkUserPreferencesServiceClient(_channelFactory.Channel);
        _currentPreferences = catalog.CreateDefaultSnapshot();
        _sharedPreferencesStream = _channelFactory.ShareLatest(CreatePreferencesStream());
        _subscriptions.Add(_sharedPreferencesStream.Subscribe(snapshot => _currentPreferences = snapshot));
    }

    public string PreferencesFilePath => _preferencesFilePath;

    public UserUnitPreferencesSnapshot CurrentPreferences => _currentPreferences;

    public async Task<UserUnitPreferencesSnapshot> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.GetUserPreferencesAsync(new GetUserPreferencesRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        var snapshot = MapSnapshot(reply);
        _currentPreferences = snapshot;
        _preferencesFilePath = reply.PreferencesPath ?? string.Empty;
        return snapshot;
    }

    public async Task<UserPreferencesOperationResult> ApplyPreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var normalized = _catalog.Normalize(snapshot);
        var request = new ApplyUserPreferencesRequest
        {
            SchemaVersion = normalized.SchemaVersion,
        };

        foreach (var entry in normalized.Entries)
        {
            request.Entries.Add(new UserPreferenceEntryReply
            {
                Kind = entry.Kind.ToString(),
                OptionKey = entry.OptionKey ?? string.Empty,
            });
        }

        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.ApplyUserPreferencesAsync(request, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapResult(reply);
    }

    public async Task<UserPreferencesOperationResult> SavePreferencesAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SaveUserPreferencesAsync(new SaveUserPreferencesRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapResult(reply);
    }

    public async Task<UserPreferencesOperationResult> LoadPreferencesAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.LoadUserPreferencesAsync(new LoadUserPreferencesRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapResult(reply);
    }

    public async Task<UserPreferencesOperationResult> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.ResetUserPreferencesAsync(new ResetUserPreferencesRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapResult(reply);
    }

    public async Task<UserPreferencesOperationResult> RelocatePreferencesStoreAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.RelocateUserPreferencesAsync(
            new RelocateUserPreferencesRequest
            {
                TargetDirectory = targetDirectory ?? string.Empty,
            },
            cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapResult(reply);
    }

    public IObservable<UserUnitPreferencesSnapshot> WatchPreferences() => _sharedPreferencesStream;

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private IObservable<UserUnitPreferencesSnapshot> CreatePreferencesStream()
    {
        return Observable.Create<UserUnitPreferencesSnapshot>(observer =>
        {
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<UserPreferencesReply>? call = null;

                    try
                    {
                        call = _client.WatchUserPreferences(new WatchUserPreferencesRequest(), cancellationToken: cancellationSource.Token);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            var reply = call.ResponseStream.Current;
                            _preferencesFilePath = reply.PreferencesPath ?? string.Empty;
                            observer.OnNext(MapSnapshot(reply));
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

                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private UserPreferencesOperationResult MapResult(UserPreferencesOperationReply reply)
    {
        var snapshot = reply.Preferences is null ? _currentPreferences : MapSnapshot(reply.Preferences);
        var path = reply.Preferences?.PreferencesPath ?? _preferencesFilePath;
        _currentPreferences = snapshot;
        _preferencesFilePath = path ?? string.Empty;

        return new UserPreferencesOperationResult
        {
            Succeeded = reply.Succeeded,
            Message = reply.Message,
            Preferences = snapshot,
            PreferencesPath = _preferencesFilePath,
        };
    }

    private UserUnitPreferencesSnapshot MapSnapshot(UserPreferencesReply reply)
    {
        var snapshot = new UserUnitPreferencesSnapshot
        {
            SchemaVersion = reply.SchemaVersion > 0 ? reply.SchemaVersion : UserUnitPreferencesSnapshot.CurrentSchemaVersion,
            Entries =
            [
                .. reply.Entries
                    .Where(entry => Enum.TryParse<UnitQuantityKind>(entry.Kind, ignoreCase: true, out _))
                    .Select(entry => new UserUnitPreferenceEntry(
                        Enum.Parse<UnitQuantityKind>(entry.Kind, ignoreCase: true),
                        entry.OptionKey ?? string.Empty))
            ],
        };

        return _catalog.Normalize(snapshot);
    }
}
