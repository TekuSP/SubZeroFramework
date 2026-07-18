using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Service.Services.Hosting;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkFatalExitHandlerTests
{
    [Test]
    public void HandleFatalFault_WhileRunning_RestoresFansThenTerminatesNonZero()
    {
        var sequence = new List<string>();
        var provider = new SequenceRecordingProvider(sequence);
        using var coordinator = new FrameworkShutdownCoordinator(provider, NullLogger<FrameworkShutdownCoordinator>.Instance);
        var lifetime = new TestHostApplicationLifetime();
        var handler = new FrameworkFatalExitHandler(
            coordinator,
            lifetime,
            NullLogger<FrameworkFatalExitHandler>.Instance,
            exitCode => sequence.Add($"exit:{exitCode}"));

        handler.HandleFatalFault(new InvalidOperationException("Boom"), "test fault");

        // The fan-restore path (StopTelemetryLoops -> StopPolling) must run BEFORE the process terminates,
        // and the exit code must be non-zero so SCM/systemd recovery engages.
        Assert.That(sequence, Is.EqualTo(new[] { "stop-polling", $"exit:{FrameworkFatalExitHandler.FatalExitCode}" }));
    }

    [Test]
    public void HandleFatalFault_WhileHostIsStopping_OnlyLogs()
    {
        var sequence = new List<string>();
        var provider = new SequenceRecordingProvider(sequence);
        using var coordinator = new FrameworkShutdownCoordinator(provider, NullLogger<FrameworkShutdownCoordinator>.Instance);
        var lifetime = new TestHostApplicationLifetime();
        var handler = new FrameworkFatalExitHandler(
            coordinator,
            lifetime,
            NullLogger<FrameworkFatalExitHandler>.Instance,
            exitCode => sequence.Add($"exit:{exitCode}"));

        // A stream fault during normal shutdown must not turn the clean stop into an SCM "failure".
        lifetime.StopApplication();
        handler.HandleFatalFault(new InvalidOperationException("Boom"), "test fault");

        Assert.That(sequence, Is.Empty);
    }

    [Test]
    public void FatalExitCode_IsNonZero()
    {
        // The whole point of the handler: exit 0 reads as a normal stop and is never restarted.
        Assert.That(FrameworkFatalExitHandler.FatalExitCode, Is.Not.Zero);
    }

    // Re-implements the interface so StopPolling records into the shared sequence (the base stub's methods
    // are non-virtual; interface re-implementation redirects dispatch here).
    private sealed class SequenceRecordingProvider(List<string> sequence) : StubFrameworkDataProvider, SubZeroFramework.Services.IFrameworkDataProvider
    {
        public new bool StopPolling()
        {
            sequence.Add("stop-polling");
            return true;
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();
    }
}
