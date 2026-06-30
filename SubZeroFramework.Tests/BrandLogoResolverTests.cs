using NUnit.Framework;

using SubZeroFramework.Branding;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the UI-side brand-logo resolver. The asset-name cases here MUST equal the filenames produced by the
/// build-time extractor in <c>Directory.Build.targets</c> (the <c>ExtractIcondataIcons</c> task) — if the two
/// diverge, the UI asks for an asset the build never created and the logo silently renders blank.
/// </summary>
public sealed class BrandLogoResolverTests
{
    // The exact asset names emitted by the build for the shipped IconifyIcon list (verified against the
    // rasterized output under Assets/Iconify/). Treat these as the contract.
    [TestCase("simple-icons:amd", "simple_icons_amd")]
    [TestCase("simple-icons:nvidia", "simple_icons_nvidia")]
    [TestCase("simple-icons:intel", "simple_icons_intel")]
    [TestCase("simple-icons:mediatek", "simple_icons_mediatek")]
    [TestCase("simple-icons:teamviewer", "simple_icons_teamviewer")]
    [TestCase("simple-icons:framework", "simple_icons_framework")]
    [TestCase("simple-icons:kingstontechnology", "simple_icons_kingstontechnology")]
    [TestCase("logos:microsoft-icon", "logos_microsoft_icon")]
    public void AssetName_MatchesBuildOutput(string iconifyId, string expected)
    {
        Assert.That(IconAssetNaming.AssetName(iconifyId), Is.EqualTo(expected));
    }

    [TestCase("simple-icons:amd", "ms-appx:///Assets/Iconify/simple_icons_amd.png")]
    [TestCase("logos:microsoft-icon", "ms-appx:///Assets/Iconify/logos_microsoft_icon.png")]
    public void MsAppxUri_BuildsAssetsIconifyPath(string iconifyId, string expected)
    {
        Assert.That(IconAssetNaming.MsAppxUri(iconifyId), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void AssetName_NullOrBlank_ReturnsNull(string? iconifyId)
    {
        Assert.That(IconAssetNaming.AssetName(iconifyId), Is.Null);
        Assert.That(IconAssetNaming.MsAppxUri(iconifyId), Is.Null);
    }

    // Mirror of the build sanitizer rules: non-alnum -> '_'; only a NON-letter first/last char is padded with
    // 'i' (a trailing letter is left alone); an empty result -> "icon".
    [TestCase("simple-icons_amd", "simple_icons_amd")]
    [TestCase("100tb", "i100tb")]
    [TestCase("amd99", "amd99i")]
    [TestCase("0x0", "i0x0i")]
    [TestCase("a-b-c", "a_b_c")]
    [TestCase("ABC", "abc")]
    [TestCase("", "icon")]
    public void Sanitize_MatchesBuildRules(string input, string expected)
    {
        Assert.That(IconAssetNaming.Sanitize(input), Is.EqualTo(expected));
    }

    [Test]
    public void AssetFolder_IsAssetsIconify()
    {
        Assert.That(IconAssetNaming.AssetFolder, Is.EqualTo("Assets/Iconify"));
    }

    [TestCase("AuthenticAMD", BrandLogoCatalog.Amd)]
    [TestCase("Advanced Micro Devices, Inc.", BrandLogoCatalog.Amd)]
    [TestCase("NVIDIA Corporation", BrandLogoCatalog.Nvidia)]
    [TestCase("Intel(R) Corporation", BrandLogoCatalog.Intel)]
    [TestCase("GenuineIntel", BrandLogoCatalog.Intel)]
    [TestCase("MediaTek Inc.", BrandLogoCatalog.MediaTek)]
    [TestCase("TeamViewer", BrandLogoCatalog.TeamViewer)]
    [TestCase("Kingston Technology", BrandLogoCatalog.Kingston)]
    [TestCase("Microsoft Corporation", BrandLogoCatalog.Microsoft)]
    [TestCase("Framework Computer Inc.", BrandLogoCatalog.Framework)]
    public void ResolveIconifyId_MapsKnownVendors(string vendor, string expected)
    {
        Assert.That(BrandLogoCatalog.ResolveIconifyId(vendor), Is.EqualTo(expected));
    }

    [TestCase("Realtek Semiconductor Corp.")]
    [TestCase("Broadcom Inc.")]
    [TestCase("Some Unknown OEM")]
    [TestCase(null)]
    [TestCase("")]
    public void ResolveIconifyId_UnknownVendor_ReturnsNull(string? vendor)
    {
        Assert.That(BrandLogoCatalog.ResolveIconifyId(vendor), Is.Null);
        Assert.That(BrandLogoCatalog.ResolveLogoUri(vendor), Is.Null);
    }

    [Test]
    public void ResolveLogoUri_KnownVendor_BuildsAssetUri()
    {
        Assert.That(
            BrandLogoCatalog.ResolveLogoUri("AuthenticAMD"),
            Is.EqualTo("ms-appx:///Assets/Iconify/simple_icons_amd.png"));
    }
}
