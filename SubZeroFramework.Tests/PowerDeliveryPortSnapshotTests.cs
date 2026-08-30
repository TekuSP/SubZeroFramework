using FrameworkDotnet.Enums;

using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The three different "maximums" a USB-C port has, and which one a user should be shown.
/// </summary>
/// <remarks>
/// A port carries the board's capability, the live flow, and the negotiated contract. Only the three
/// together answer "why is this only charging at 45 W", and the arithmetic that picks between them is what
/// these tests pin down.
/// </remarks>
[TestFixture]
public class PowerDeliveryPortSnapshotTests
{
    [Test]
    public void EffectiveMaximumPowerWatts_PrefersTheNegotiatedContract()
    {
        var port = NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: 45d);

        Assert.That(port.EffectiveMaximumPowerWatts, Is.EqualTo(45d).Within(1e-9d));
    }

    /// <summary>
    /// A slot with no PD controller behind it reports no contract at all. The board capability is then the
    /// only honest number available.
    /// </summary>
    [Test]
    public void EffectiveMaximumPowerWatts_WithoutAContract_UsesTheBoardCapability()
    {
        var port = NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: null);

        Assert.That(port.EffectiveMaximumPowerWatts, Is.EqualTo(100d).Within(1e-9d));
    }

    /// <summary>
    /// The case worth surfacing: the machine could take more than it is being given.
    /// </summary>
    [Test]
    public void IsNegotiatingBelowCapability_IsTrueWhenTheContractIsMateriallyLower()
        => Assert.That(NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: 45d).IsNegotiatingBelowCapability, Is.True);

    [Test]
    public void IsNegotiatingBelowCapability_IsFalseWhenTheContractMatchesTheBoard()
        => Assert.That(NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: 100d).IsNegotiatingBelowCapability, Is.False);

    /// <summary>
    /// A charger advertising 99 W into a 100 W slot is doing its job. Flagging that would train the user to
    /// ignore the flag.
    /// </summary>
    [Test]
    public void IsNegotiatingBelowCapability_ToleratesASmallShortfall()
        => Assert.That(NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: 95d).IsNegotiatingBelowCapability, Is.False);

    [Test]
    public void IsNegotiatingBelowCapability_IsFalseWithoutAContractToCompare()
        => Assert.That(NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: null).IsNegotiatingBelowCapability, Is.False);

    /// <summary>
    /// An undocumented slot reports a zero capability. Dividing the comparison by that would flag every
    /// port on a machine whose capability matrix this app does not carry.
    /// </summary>
    [Test]
    public void IsNegotiatingBelowCapability_IsFalseWhenTheBoardCapabilityIsUnknown()
        => Assert.That(NewPort(maxChargeWatts: 0, negotiatedMaximumPowerWatts: 45d).IsNegotiatingBelowCapability, Is.False);

    private static PowerDeliveryPortSnapshot NewPort(int maxChargeWatts, double? negotiatedMaximumPowerWatts) => new()
    {
        SlotIndex = 0,
        PortSource = "Mainboard",
        PortPosition = "Left Back",
        PortIsLeft = true,
        IsPresent = true,
        IsActivePort = true,
        HasPowerDeliveryContract = true,
        CState = default,
        PowerRole = default,
        DataRole = default,
        CcPolarity = default,
        VoltageVolts = 20d,
        CurrentAmperes = 2.25d,
        IsVconnActive = false,
        IsEprActive = false,
        IsEprSupported = false,
        AltModeFlags = 0,
        CardType = FrameworkExpansionCardType.Unknown,
        DataLane = default,
        DisplayPortCapability = default,
        SupportsCharging = true,
        MaxChargeWatts = maxChargeWatts,
        UsbAHighPower = false,
        CapabilityDocumented = true,
        NegotiatedMaximumPowerWatts = negotiatedMaximumPowerWatts,
    };
}
