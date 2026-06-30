namespace SubZeroFramework.Branding;

/// <summary>
/// Layer 1 of the brand-logo resolver: turns an Iconify id ("prefix:name", e.g. "simple-icons:amd") into the
/// rasterized asset's name and its runtime <c>ms-appx</c> URI.
/// <para>
/// The brand SVGs are extracted from the IconifyBundle.* packs at build time and rasterized by Uno.Resizetizer
/// (see <c>Directory.Build.targets</c>, the <c>ExtractIcondataIcons</c> task). The asset name produced here MUST
/// stay byte-for-byte identical to the build's <c>Sanitize(prefix + "_" + name)</c>, and <see cref="AssetFolder"/>
/// MUST match the build's output folder + <c>Link</c> — otherwise the UI asks for a path the build never produced
/// and the image silently renders blank. <see cref="SubZeroFramework.Branding.IconAssetNamingTests"/> pins both.
/// </para>
/// </summary>
public static class IconAssetNaming
{
    /// <summary>
    /// Logical asset folder (matches the <c>Link</c> the build assigns the extracted SVGs). The rasterized PNGs
    /// resolve at runtime under <c>ms-appx:///{AssetFolder}/{asset}.png</c>.
    /// </summary>
    public const string AssetFolder = "Assets/Iconify";

    /// <summary>
    /// Maps an Iconify id to its sanitized, pack-prefixed asset name (e.g. "simple-icons:amd" -> "simple_icons_amd").
    /// Returns <see langword="null"/> for a null/blank id.
    /// </summary>
    public static string? AssetName(string? iconifyId)
    {
        if (string.IsNullOrWhiteSpace(iconifyId))
        {
            return null;
        }

        var id = iconifyId.Trim();
        var colon = id.IndexOf(':');
        // The build sanitizes "prefix_name" so the asset is globally unique and never a single char.
        var combined = colon > 0 ? string.Concat(id[..colon], "_", id[(colon + 1)..]) : id;
        return Sanitize(combined);
    }

    /// <summary>
    /// Maps an Iconify id to the <c>ms-appx</c> URI of its rasterized PNG, or <see langword="null"/> for a
    /// null/blank id. Mirror of the build-time output path.
    /// </summary>
    public static string? MsAppxUri(string? iconifyId)
    {
        var asset = AssetName(iconifyId);
        return asset is null ? null : $"ms-appx:///{AssetFolder}/{asset}.png";
    }

    /// <summary>
    /// Exact mirror of the build's Uno.Resizetizer asset-name sanitizer: lowercase; every character that is not
    /// <c>a-z</c>/<c>0-9</c> becomes <c>_</c>; an empty result becomes "icon"; a non-letter first/last character is
    /// padded with <c>i</c> (Resizetizer requires names to start and end with a letter).
    /// </summary>
    public static string Sanitize(string name)
    {
        var chars = name.ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
            {
                chars[i] = '_';
            }
        }

        var s = new string(chars);
        if (s.Length == 0)
        {
            return "icon";
        }

        if (!(s[0] >= 'a' && s[0] <= 'z'))
        {
            s = "i" + s;
        }

        if (!(s[^1] >= 'a' && s[^1] <= 'z'))
        {
            s += "i";
        }

        return s;
    }
}
