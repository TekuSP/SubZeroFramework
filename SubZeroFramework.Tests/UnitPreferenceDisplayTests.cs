using Material.Icons;

using NUnit.Framework;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Tests;

public sealed class UnitPreferenceDisplayTests
{
    private static readonly UnitPreferenceCatalog Catalog = new();

    [Test]
    public void IconName_ForEveryCatalogKind_IsAValidMaterialIconKind()
    {
        foreach (var definition in Catalog.Definitions)
        {
            var iconName = UnitPreferenceDisplay.IconName(definition.Kind);

            Assert.That(iconName, Is.Not.Empty, $"{definition.Kind} icon name");
            Assert.That(
                System.Enum.TryParse<MaterialIconKind>(iconName, out _),
                Is.True,
                $"{definition.Kind} icon name '{iconName}' is not a MaterialIconKind member");
        }
    }

    [Test]
    public void ShortDescription_ForEveryCatalogKind_IsNotEmpty()
    {
        foreach (var definition in Catalog.Definitions)
        {
            Assert.That(UnitPreferenceDisplay.ShortDescription(definition.Kind), Is.Not.Empty, $"{definition.Kind} short description");
        }
    }

    [Test]
    public void ShortOptionLabel_CoversEveryCatalogOption()
    {
        // Keys that legitimately match their label case-insensitively (rpm→RPM, cfm→CFM, auto→Auto).
        string[] caseOnlyKeys = ["rpm", "cfm", "auto"];

        foreach (var definition in Catalog.Definitions)
        {
            foreach (var option in definition.Options)
            {
                var label = UnitPreferenceDisplay.ShortOptionLabel(definition.Kind, option.Key);

                Assert.That(label, Is.Not.Empty, $"{definition.Kind}/{option.Key} label");

                if (!caseOnlyKeys.Contains(option.Key))
                {
                    Assert.That(
                        label,
                        Is.Not.EqualTo(option.Key),
                        $"{definition.Kind}/{option.Key} label fell back to the raw key");
                }
            }
        }
    }

    [Test]
    public void ShortOptionLabels_AreUniqueWithinEachKind()
    {
        foreach (var definition in Catalog.Definitions)
        {
            var labels = definition.Options
                .Select(option => UnitPreferenceDisplay.ShortOptionLabel(definition.Kind, option.Key))
                .ToArray();

            Assert.That(labels, Is.Unique, $"{definition.Kind} segment labels collide");
        }
    }
}
