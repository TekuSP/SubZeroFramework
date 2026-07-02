using FrameworkDotnet.Enums;

using Material.Icons;

using NUnit.Framework;

using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

// FD0001 (platform-specific enum members) is intentionally suppressed: these tests deliberately enumerate every
// member to prove the presentation catalog is total, regardless of the platform the tests run on.
#pragma warning disable FD0001

public sealed class FrameworkModuleDisplayTests
{
    [Test]
    public void For_CoversEveryIdentity_WithCompleteMetadata()
    {
        foreach (FrameworkModuleIdentity identity in System.Enum.GetValues<FrameworkModuleIdentity>())
        {
            var info = FrameworkModuleDisplay.For(identity);

            Assert.That(info.DisplayName, Is.Not.Empty, $"{identity} display name");
            Assert.That(info.Category, Is.Not.Empty, $"{identity} category");
            Assert.That(info.IconName, Is.Not.Empty, $"{identity} icon");
            Assert.That(info.Interface, Is.Not.Empty, $"{identity} interface");
            Assert.That(info.Bus, Is.Not.Empty, $"{identity} bus");
            Assert.That(info.PowerDelivery, Is.Not.Empty, $"{identity} power delivery");
            Assert.That(info.Description, Is.Not.Empty, $"{identity} description");
            Assert.That(info.Serviceability, Is.Not.Empty, $"{identity} serviceability");
        }
    }

    [Test]
    public void For_EveryIconName_IsAValidMaterialIconKind()
    {
        foreach (FrameworkModuleIdentity identity in System.Enum.GetValues<FrameworkModuleIdentity>())
        {
            var info = FrameworkModuleDisplay.For(identity);
            Assert.That(
                System.Enum.TryParse<MaterialIconKind>(info.IconName, out _),
                Is.True,
                $"{identity} icon name '{info.IconName}' is not a MaterialIconKind member");
        }
    }

    [Test]
    public void For_EveryRealIdentity_HasItsOwnEntry_NotTheNoneFallback()
    {
        foreach (FrameworkModuleIdentity identity in System.Enum.GetValues<FrameworkModuleIdentity>())
        {
            if (identity == FrameworkModuleIdentity.None)
            {
                continue;
            }

            Assert.That(
                FrameworkModuleDisplay.For(identity).DisplayName,
                Is.Not.EqualTo("None"),
                $"{identity} fell through to the None fallback entry");
        }
    }

    [TestCase(FrameworkModuleIdentity.UsbCExpansionCard, "USB-C", "Expansion cards", "100 W")]
    [TestCase(FrameworkModuleIdentity.Framework16KeyboardModule, "Keyboard module", "Input deck modules", "—")]
    [TestCase(FrameworkModuleIdentity.ExpansionBayAmdGpu, "AMD GPU", "Expansion bay", "—")]
    public void For_MapsKnownIdentities(FrameworkModuleIdentity identity, string name, string category, string powerDelivery)
    {
        var info = FrameworkModuleDisplay.For(identity);

        Assert.That(info.DisplayName, Is.EqualTo(name));
        Assert.That(info.Category, Is.EqualTo(category));
        Assert.That(info.PowerDelivery, Is.EqualTo(powerDelivery));
    }

    [Test]
    public void For_GpuModules_CarryBrandLogoVendors()
    {
        Assert.That(FrameworkModuleDisplay.For(FrameworkModuleIdentity.ExpansionBayAmdGpu).LogoVendor, Is.EqualTo("AMD"));
        Assert.That(FrameworkModuleDisplay.For(FrameworkModuleIdentity.ExpansionBayNvidiaGpu).LogoVendor, Is.EqualTo("Nvidia"));
    }

    [Test]
    public void ForCardType_CoversEveryCardType()
    {
        foreach (FrameworkExpansionCardType cardType in System.Enum.GetValues<FrameworkExpansionCardType>())
        {
            var info = FrameworkModuleDisplay.ForCardType(cardType);
            Assert.That(info.DisplayName, Is.Not.Empty, $"{cardType} display name");
            Assert.That(info.Category, Is.Not.Empty, $"{cardType} category");
        }
    }

    [TestCase(FrameworkExpansionCardType.UsbC, "USB-C")]
    [TestCase(FrameworkExpansionCardType.Hdmi, "HDMI")]
    [TestCase(FrameworkExpansionCardType.Unknown, "Unknown USB-C occupant")]
    public void ForCardType_MapsKnownCardTypes(FrameworkExpansionCardType cardType, string expectedName)
    {
        Assert.That(FrameworkModuleDisplay.ForCardType(cardType).DisplayName, Is.EqualTo(expectedName));
    }

    [Test]
    public void SlotKindLabels_CoverEverySlotKind()
    {
        foreach (FrameworkModuleSlotKind slotKind in System.Enum.GetValues<FrameworkModuleSlotKind>())
        {
            Assert.That(FrameworkModuleDisplay.SlotKindLabel(slotKind), Is.Not.Empty, $"{slotKind} label");
            Assert.That(FrameworkModuleDisplay.SlotKindDescription(slotKind), Is.Not.Empty, $"{slotKind} description");
        }
    }

    [TestCase(FrameworkModuleSlotKind.UsbCExpansionCardSlot, "Expansion-card slot")]
    [TestCase(FrameworkModuleSlotKind.InputDeckTopRow, "Input deck · top row")]
    [TestCase(FrameworkModuleSlotKind.InternalFixed, "Internal · fixed")]
    public void SlotKindLabel_MapsKnownKinds(FrameworkModuleSlotKind slotKind, string expected)
    {
        Assert.That(FrameworkModuleDisplay.SlotKindLabel(slotKind), Is.EqualTo(expected));
    }

    [Test]
    public void ConfidenceLabels_CoverEveryConfidence()
    {
        foreach (FrameworkModuleConfidence confidence in System.Enum.GetValues<FrameworkModuleConfidence>())
        {
            Assert.That(FrameworkModuleDisplay.ConfidenceLabel(confidence), Is.Not.Empty, $"{confidence} label");
        }
    }

    [TestCase(FrameworkModuleConfidence.Direct, "Confirmed")]
    [TestCase(FrameworkModuleConfidence.DerivedStrong, "Likely")]
    [TestCase(FrameworkModuleConfidence.DerivedWeak, "Inferred")]
    [TestCase(FrameworkModuleConfidence.Unknown, "Unknown")]
    public void ConfidenceLabel_MapsKnownConfidences(FrameworkModuleConfidence confidence, string expected)
    {
        Assert.That(FrameworkModuleDisplay.ConfidenceLabel(confidence), Is.EqualTo(expected));
    }
}
