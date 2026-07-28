using Microsoft.Extensions.Hosting;

namespace SubZeroFramework.Tests;

/// <summary>
/// Minimal <see cref="IHostApplicationLifetime"/> for tests that construct hosted services directly.
/// <see cref="StopApplication"/> signals <see cref="ApplicationStopping"/>, which is what the workers hook to
/// stop actuating before the shutdown restore runs.
/// </summary>
public sealed class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    /// <summary>True once <see cref="StopApplication"/> has been called, so tests can assert on it.</summary>
    public bool StopRequested => _stopping.IsCancellationRequested;

    public void StopApplication() => _stopping.Cancel();

    public void Dispose() => _stopping.Dispose();
}
