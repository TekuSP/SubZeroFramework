using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class InMemoryServiceLogBufferTests
{
    [Test]
    public void Snapshot_ReturnsEntriesOldestFirst()
    {
        InMemoryServiceLogBuffer buffer = new();
        buffer.Add(Entry("first"));
        buffer.Add(Entry("second"));

        var (entries, dropped) = buffer.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(static e => e.Message), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(dropped, Is.Zero);
        });
    }

    [Test]
    public void Add_BeyondCapacity_DropsTheOldestAndCountsIt()
    {
        // The whole reason the reply carries a dropped count: past capacity this is the most recent slice,
        // not the full history, and the UI must be able to say so.
        InMemoryServiceLogBuffer buffer = new();
        for (var i = 0; i < InMemoryServiceLogBuffer.Capacity + 5; i++)
        {
            buffer.Add(Entry($"entry-{i}"));
        }

        var (entries, dropped) = buffer.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Length.EqualTo(InMemoryServiceLogBuffer.Capacity), "The buffer must stay bounded.");
            Assert.That(dropped, Is.EqualTo(5));
            Assert.That(entries[0].Message, Is.EqualTo("entry-5"), "The oldest entries are the ones dropped.");
            Assert.That(entries[^1].Message, Is.EqualTo($"entry-{InMemoryServiceLogBuffer.Capacity + 4}"));
        });
    }

    [Test]
    public void Clear_EmptiesTheBufferAndTheDroppedCount()
    {
        InMemoryServiceLogBuffer buffer = new();
        for (var i = 0; i < InMemoryServiceLogBuffer.Capacity + 1; i++)
        {
            buffer.Add(Entry($"entry-{i}"));
        }

        buffer.Clear();

        var (entries, dropped) = buffer.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Is.Empty);
            Assert.That(dropped, Is.Zero, "A cleared buffer must not keep claiming it dropped history.");
        });
    }

    [Test]
    public void Provider_RecordsWhatWasLogged()
    {
        InMemoryServiceLogBuffer buffer = new();
        using InMemoryServiceLogProvider provider = new(buffer);

        var logger = provider.CreateLogger("SubZeroFramework.Service.Services.Example");
        logger.LogWarning(new InvalidOperationException("boom"), "Something went {State}.", "wrong");

        var (entries, _) = buffer.Snapshot();

        Assert.That(entries, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entries[0].Category, Is.EqualTo("SubZeroFramework.Service.Services.Example"));
            Assert.That(entries[0].Message, Is.EqualTo("Something went wrong."), "The message must arrive formatted, not as a template.");
            Assert.That(entries[0].Exception, Does.Contain("boom"));
        });
    }

    [Test]
    public void Provider_WhenTheFormatterThrows_RecordsAPlaceholderInsteadOfPropagating()
    {
        // A log line must never be able to take the service down.
        InMemoryServiceLogBuffer buffer = new();
        using InMemoryServiceLogProvider provider = new(buffer);
        var logger = provider.CreateLogger("Example");

        Assert.DoesNotThrow(() => logger.Log<object>(
            LogLevel.Information,
            default,
            new object(),
            exception: null,
            static (_, _) => throw new InvalidOperationException("formatter failed")));

        var (entries, _) = buffer.Snapshot();

        Assert.That(entries, Has.Length.EqualTo(1));
        Assert.That(entries[0].Message, Does.Contain("could not be formatted"));
    }

    [Test]
    public void Provider_LeavesTheRealLoggingPipelineAlone()
    {
        // Sanity: the provider is additive. NullLogger stands in for the platform sinks here — the point is
        // that creating our logger does not require or replace them.
        InMemoryServiceLogBuffer buffer = new();
        using InMemoryServiceLogProvider provider = new(buffer);

        Assert.That(provider.CreateLogger("Example"), Is.Not.SameAs(NullLogger.Instance));
        Assert.That(buffer.Snapshot().Entries, Is.Empty);
    }

    private static ServiceLogEntry Entry(string message) => new()
    {
        ObservedAt = DateTimeOffset.UnixEpoch,
        Level = LogLevel.Information,
        Category = "Example",
        Message = message,
    };
}
