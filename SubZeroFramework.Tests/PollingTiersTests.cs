using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers the polling-tier bounds. These are settable from a config file and, for two of the three, over the
/// local socket, so they are the guard between a typo and a symptom nobody traces back to it.
/// </summary>
[TestFixture]
public class PollingTiersTests
{
    [Test]
    public void Clamp_LeavesAWorkableIntervalAlone()
    {
        // The shipped defaults must pass through untouched, or the bounds are wrong rather than the config.
        Assert.Multiple(() =>
        {
            Assert.That(PollingTiers.Primary.Clamp(TimeSpan.FromMilliseconds(150)), Is.EqualTo(TimeSpan.FromMilliseconds(150)));
            Assert.That(PollingTiers.Primary.Clamp(TimeSpan.FromSeconds(2)), Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(PollingTiers.Secondary.Clamp(TimeSpan.FromSeconds(1)), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(PollingTiers.Tertiary.Clamp(TimeSpan.FromSeconds(30)), Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
    }

    [Test]
    public void Clamp_RaisesAnIntervalThatWouldSaturateTheTier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PollingTiers.Primary.Clamp(TimeSpan.FromMilliseconds(1)), Is.EqualTo(PollingTiers.Primary.Minimum));
            Assert.That(PollingTiers.Secondary.Clamp(TimeSpan.Zero), Is.EqualTo(PollingTiers.Secondary.Minimum));

            // The tertiary floor matters most: this tier drives Hardware.Info, whose Linux memory and drive
            // lists each spawn a full lshw probe. Running those in a tight loop is issue #51.
            Assert.That(PollingTiers.Tertiary.Clamp(TimeSpan.FromMilliseconds(10)), Is.EqualTo(PollingTiers.Tertiary.Minimum));
        });
    }

    [Test]
    public void Clamp_LowersAnIntervalThatWouldStarveTheTier()
    {
        Assert.Multiple(() =>
        {
            // A fan controller acting on heat from ten minutes ago is not controlling anything.
            Assert.That(PollingTiers.Primary.Clamp(TimeSpan.FromMinutes(10)), Is.EqualTo(PollingTiers.Primary.Maximum));
            Assert.That(PollingTiers.Secondary.Clamp(TimeSpan.FromHours(1)), Is.EqualTo(PollingTiers.Secondary.Maximum));
            Assert.That(PollingTiers.Tertiary.Clamp(TimeSpan.FromDays(1)), Is.EqualTo(PollingTiers.Tertiary.Maximum));
        });
    }

    [Test]
    public void Clamp_AcceptsExactlyTheBounds()
    {
        foreach (var tier in new[] { PollingTiers.Primary, PollingTiers.Secondary, PollingTiers.Tertiary })
        {
            Assert.Multiple(() =>
            {
                Assert.That(tier.Clamp(tier.Minimum), Is.EqualTo(tier.Minimum), $"{tier.Name} minimum.");
                Assert.That(tier.Clamp(tier.Maximum), Is.EqualTo(tier.Maximum), $"{tier.Name} maximum.");
            });
        }
    }

    [Test]
    public void IsOutOfRange_AgreesWithClamp()
    {
        // The provider logs based on this, so a disagreement would either warn about a value it kept or keep
        // quiet about one it changed.
        foreach (var tier in new[] { PollingTiers.Primary, PollingTiers.Secondary, PollingTiers.Tertiary })
        {
            foreach (var candidate in new[] { TimeSpan.Zero, tier.Minimum, tier.Maximum, TimeSpan.FromDays(1) })
            {
                Assert.That(
                    tier.IsOutOfRange(candidate),
                    Is.EqualTo(tier.Clamp(candidate) != candidate),
                    $"{tier.Name} disagreed about {candidate}.");
            }
        }
    }

    [Test]
    public void Default_IsInsideItsOwnRange()
    {
        // The settings page's Default button writes this value straight into the draft. A default outside the
        // bounds would produce a button that types a number the very next validation pass rejects.
        foreach (var tier in new[] { PollingTiers.Primary, PollingTiers.Secondary, PollingTiers.Tertiary })
        {
            Assert.That(tier.IsOutOfRange(tier.Default), Is.False, $"{tier.Name} default {tier.Default} is outside {tier.Minimum}–{tier.Maximum}.");
        }
    }

    [Test]
    public void Defaults_AreOrderedFromFastestToSlowest()
    {
        // Same argument as the ranges: a secondary tier defaulting faster than the primary would mean the
        // display data was fresher than the data fan control acts on.
        Assert.Multiple(() =>
        {
            Assert.That(PollingTiers.Primary.Default, Is.LessThan(PollingTiers.Secondary.Default));
            Assert.That(PollingTiers.Secondary.Default, Is.LessThan(PollingTiers.Tertiary.Default));
        });
    }

    [Test]
    public void DefaultMilliseconds_IsTheWholeNumberTheSettingsPageWrites()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PollingTiers.Primary.DefaultMilliseconds, Is.EqualTo(150));
            Assert.That(PollingTiers.Secondary.DefaultMilliseconds, Is.EqualTo(1_000));
            Assert.That(PollingTiers.Tertiary.DefaultMilliseconds, Is.EqualTo(30_000));
        });
    }

    [Test]
    public void Tiers_AreOrderedFromFastestToSlowest()
    {
        // The names only mean something if the ranges actually escalate. If a later tier could be configured
        // faster than an earlier one, the whole split stops describing what it claims to.
        Assert.Multiple(() =>
        {
            Assert.That(PollingTiers.Primary.Minimum, Is.LessThan(PollingTiers.Secondary.Minimum));
            Assert.That(PollingTiers.Secondary.Minimum, Is.LessThan(PollingTiers.Tertiary.Minimum));
            Assert.That(PollingTiers.Primary.Maximum, Is.LessThan(PollingTiers.Secondary.Maximum));
            Assert.That(PollingTiers.Secondary.Maximum, Is.LessThan(PollingTiers.Tertiary.Maximum));
        });
    }
}
