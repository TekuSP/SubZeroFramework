using NUnit.Framework;

using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers the PDH <c>Processor Information</c> instance-name filter. Counting a rollup as a real processor
/// would add a phantom core whose load is the average of all the others — once for the machine, and again for
/// every processor group — so the rejection cases matter more than the acceptance ones.
/// </summary>
[TestFixture]
public class ProcessorInstanceNameTests
{
    [Test]
    public void TryParse_ReadsGroupAndProcessor()
    {
        var parsed = ProcessorInstanceName.TryParse("0,5", out var group, out var processor);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(group, Is.Zero);
            Assert.That(processor, Is.EqualTo(5));
        });
    }

    [Test]
    public void TryParse_ReadsProcessorsInHigherGroups()
    {
        var parsed = ProcessorInstanceName.TryParse("3,12", out var group, out var processor);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(group, Is.EqualTo(3));
            Assert.That(processor, Is.EqualTo(12));
        });
    }

    [Test]
    public void TryParse_RejectsTheMachineTotal()
    {
        Assert.That(ProcessorInstanceName.TryParse("_Total", out _, out _), Is.False);
    }

    [Test]
    public void TryParse_RejectsPerGroupTotals()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessorInstanceName.TryParse("0,_Total", out _, out _), Is.False);
            Assert.That(ProcessorInstanceName.TryParse("1,_Total", out _, out _), Is.False);
        });
    }

    [Test]
    public void TryParse_IsCaseInsensitiveAboutTotals()
    {
        // The casing of the rollup name is not something to bet a phantom core on.
        Assert.Multiple(() =>
        {
            Assert.That(ProcessorInstanceName.TryParse("_total", out _, out _), Is.False);
            Assert.That(ProcessorInstanceName.TryParse("0,_TOTAL", out _, out _), Is.False);
        });
    }

    [Test]
    public void TryParse_AcceptsBareIndexesFromTheOlderCounterSet()
    {
        var parsed = ProcessorInstanceName.TryParse("7", out var group, out var processor);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(group, Is.Zero);
            Assert.That(processor, Is.EqualTo(7));
        });
    }

    [Test]
    public void TryParse_RejectsGarbage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessorInstanceName.TryParse(string.Empty, out _, out _), Is.False);
            Assert.That(ProcessorInstanceName.TryParse("cpu0", out _, out _), Is.False);
            Assert.That(ProcessorInstanceName.TryParse("0,", out _, out _), Is.False);
            Assert.That(ProcessorInstanceName.TryParse(",0", out _, out _), Is.False);
        });
    }

    [Test]
    public void IsMachineTotal_MatchesOnlyTheMachineRollup()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessorInstanceName.IsMachineTotal("_Total"), Is.True);
            Assert.That(ProcessorInstanceName.IsMachineTotal("_total"), Is.True);
            Assert.That(ProcessorInstanceName.IsMachineTotal("0,_Total"), Is.False, "A per-group rollup is not the machine total.");
            Assert.That(ProcessorInstanceName.IsMachineTotal("0,1"), Is.False);
        });
    }

    [Test]
    public void ToOrdinal_SortsGroupsThenProcessors()
    {
        var ordered = new[]
        {
            (Group: 1, Processor: 0),
            (Group: 0, Processor: 10),
            (Group: 0, Processor: 2),
            (Group: 1, Processor: 3),
        }
            .OrderBy(entry => ProcessorInstanceName.ToOrdinal(entry.Group, entry.Processor))
            .ToArray();

        // Numeric, not lexicographic: "10" must not sort between "1" and "2" the way a string sort would.
        Assert.That(ordered, Is.EqualTo(new[]
        {
            (Group: 0, Processor: 2),
            (Group: 0, Processor: 10),
            (Group: 1, Processor: 0),
            (Group: 1, Processor: 3),
        }));
    }
}
