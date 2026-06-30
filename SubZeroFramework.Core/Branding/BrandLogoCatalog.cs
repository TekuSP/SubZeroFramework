namespace SubZeroFramework.Branding;

/// <summary>
/// Layer 2 of the brand-logo resolver: maps a hardware vendor/manufacturer string (as reported by HardwareInfo or
/// framework-dotnet, e.g. "AuthenticAMD", "NVIDIA Corporation", "Intel(R) Corporation", "Realtek Semiconductor")
/// to the Iconify id of the brand logo we ship, or <see langword="null"/> when we have no logo for it (the caller
/// then falls back to a Material glyph). Matching is case-insensitive and keyword-based because vendor strings are
/// inconsistent across sources.
/// <para>Only the ids declared as constants here are extracted as assets (see the <c>IconifyIcon</c> list in the UI
/// project). Adding a brand = add its <c>IconifyIcon</c> there + a constant + keyword(s) here.</para>
/// </summary>
public static class BrandLogoCatalog
{
    public const string Amd = "simple-icons:amd";
    public const string Nvidia = "simple-icons:nvidia";
    public const string Intel = "simple-icons:intel";
    public const string MediaTek = "simple-icons:mediatek";
    public const string TeamViewer = "simple-icons:teamviewer";
    public const string Framework = "simple-icons:framework";
    public const string Kingston = "simple-icons:kingstontechnology";
    public const string Microsoft = "logos:microsoft-icon";

    // Ordered keyword -> id. First contained keyword wins. Keep specific multi-word tokens before short ones.
    private static readonly (string Keyword, string Id)[] Map =
    [
        ("advanced micro", Amd),
        ("amd", Amd),
        ("nvidia", Nvidia),
        ("intel", Intel),
        ("mediatek", MediaTek),
        ("teamviewer", TeamViewer),
        ("kingston", Kingston),
        ("microsoft", Microsoft),
        ("framework", Framework),
    ];

    /// <summary>
    /// Resolves a vendor string to the Iconify id of its brand logo, or <see langword="null"/> if none is shipped
    /// (e.g. Realtek, or an unknown vendor) — caller should render a Material fallback glyph instead.
    /// </summary>
    public static string? ResolveIconifyId(string? vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            return null;
        }

        var normalized = vendor.ToLowerInvariant();
        foreach (var (keyword, id) in Map)
        {
            if (normalized.Contains(keyword, System.StringComparison.Ordinal))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Convenience: vendor string -> <c>ms-appx</c> URI of its brand logo, or <see langword="null"/> for a vendor
    /// with no shipped logo.
    /// </summary>
    public static string? ResolveLogoUri(string? vendor) =>
        IconAssetNaming.MsAppxUri(ResolveIconifyId(vendor));
}
