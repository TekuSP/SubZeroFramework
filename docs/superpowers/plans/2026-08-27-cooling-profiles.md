# Cooling Profiles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user keep several named fan setups, switch between them in one action, and see which one is active as a tint on the title bar and navigation rail — with the service owning the library and pushing changes to every client over a stream.

**Architecture:** The service gains a `FrameworkCoolingProfileStore` modelled on the existing `FrameworkFanControlStateStore` (DynamicData `SourceCache`, a serialising `Lock`, persistence through `FrameworkServiceOptions`). Mutations arrive on `FrameworkFanControlService`; the library streams from `FrameworkTelemetryService`. The client's `LocalFanProfileStore` is deleted and `DashboardModel` binds to a new `ICoolingProfileClient` instead. "Active" is a stored label, never a replayed command — live fan state stays the only authority on what the fans are doing.

**Tech Stack:** .NET 10, gRPC (protobuf), DynamicData, Uno Platform / WinUI 3, CommunityToolkit.Mvvm, NUnit.

**Spec:** `docs/superpowers/specs/2026-08-27-cooling-profiles-design.md`

## Global Constraints

- Edit source files with the Edit/Write tools only. Bash is for build, test, git, and grep — never for modifying source.
- All quantities go through `IUnitFormattingService` in both directions, including slider min/max/value.
- No manual backing fields: use auto-properties, `[ObservableProperty]`, or the `field` keyword.
- No revision counters. Store derived values and assign them in a `RefreshDerivedState` method.
- Never construct a `Brush` in a view-model field initialiser or a static field, and never hand out a brush from a shared/static cache. Brushes are created on the UI thread, fresh per change.
- Pills are a fixed `Height` plus `CornerRadius = Height / 2`. `CornerRadius="999"` without a height renders an ellipse.
- Accent alpha is fixed by the app at **18%**. Only the hue comes from the user.
- Any blended accent whose contrast against `#D7D8FF` falls below **4.5:1** is clamped toward the sidebar colour until it passes.
- The curated accent palette contains **no ambers**, so a tint can never camouflage the amber update icon.
- Profiles capture fan behaviour only — never charge limits or other power settings.
- Driving sensor indices are **not** captured in a profile. The profile carries the target; the fan keeps its own sensors.
- `MaxCurveProfileSlots` becomes **6**; slot **5** is reserved for profile-applied curves and is hidden from all per-fan slot UI.
- Tests use NUnit (`[TestFixture]`, `[TestCase]`, `Assert.That`). Hardware-touching tests are marked so CI can filter them with `TestCategory!=Hardware`.

---

## File Structure

**Core (`SubZeroFramework.Core`)**
- `Models/FanProfile.cs` → renamed to `Models/CoolingProfile.cs`. Holds `CoolingProfile` and `CoolingProfileFanEntry`, gains embedded curve points and an accent colour. Keeps `Matches` — drift detection is unchanged and stays client-side.
- `Services/Cooling/AccentBlend.cs` (new). Pure colour maths: blend a hue over the sidebar colour at fixed alpha, and clamp for contrast. No UI types, so it is testable without a UI thread.
- `Models/CoolingAccentPalette.cs` (new). The eight curated tints as ARGB constants.

**Service (`SubZeroFramework.Service`)**
- `Models/CoolingProfileOptions.cs` (new). The persisted shape, mirroring `FanControlStateOptions`.
- `Models/FrameworkServiceOptions.cs` (modify). Two new properties.
- `Services/FrameworkCoolingProfileStore.cs` (new). The library, the active id, seeding, and persistence.
- `Services/CoolingProfileApplier.cs` (new). Applies a profile across fans best-effort. Separate from the store so it can be tested without persistence, and so the store keeps one responsibility.
- `Services/FrameworkFanControlStateStore.cs` (modify). `MaxCurveProfileSlots` 5 → 6, plus a `ReservedProfileSlot` constant.
- `Services/FrameworkFanControlGrpcService.cs` (modify). Four new mutation RPCs.
- `Services/FrameworkTelemetryGrpcService.cs` (modify). One new stream.

**Contracts (`SubZeroFramework.GrpcContracts`)**
- `Protos/framework_telemetry.proto` (modify). Additive only.

**App (`SubZeroFramework`)**
- `Services/ICoolingProfileClient.cs`, `Services/GrpcCoolingProfileClient.cs` (new).
- `Services/LocalFanProfileStore.cs` (delete).
- `Presentation/MenuItems/Dashboard/DashboardModel.cs` (modify). Swap the store, drop the flag and the per-fan apply loop.
- `Presentation/MainModel.cs` (modify). `AccentBrush`.
- `Presentation/MainPage.xaml` (modify). Bind both shell surfaces.
- `Controls/Dashboard/Models/FanProfileNameDialogModel.cs` (modify). Palette selection.

---

## Task 1: Core model — rename, embed curves, carry a colour

**Files:**
- Rename: `SubZeroFramework.Core/Models/FanProfile.cs` → `SubZeroFramework.Core/Models/CoolingProfile.cs`
- Test: `SubZeroFramework.Tests/CoolingProfileTests.cs`

**Interfaces:**
- Produces: `CoolingProfile` (record: `Id`, `Name`, `IconName`, `AccentColorArgb`, `IsSeeded`, `Fans`), `CoolingProfileFanEntry` (record: `FanIndex`, `Mode`, `DutyPercent`, `CurvePoints`, `AdaptiveTargetCelsius`, `Aggregation`), and `bool CoolingProfile.Matches(IReadOnlyDictionary<int, FanControlStateSnapshot>)`.

The spec's client-architecture section refers to this type as `FanProfile`; this task renames it to `CoolingProfile` so the client model, the proto, and the Dashboard vocabulary all agree. That rename supersedes the spec on that one point.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Immutable;
using FrameworkDotnet.Enums;
using NUnit.Framework;
using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

[TestFixture]
public class CoolingProfileTests
{
    private static CoolingProfile CurveProfile(ImmutableSortedDictionary<int, double> points) => new()
    {
        Id = "p1",
        Name = "Gaming",
        Fans = [new CoolingProfileFanEntry
        {
            FanIndex = 0,
            Mode = FanControlMode.CustomCurve,
            CurvePoints = points,
        }],
    };

    [Test]
    public void Matches_IsTrue_WhenTheFansCurveEqualsTheEmbeddedCurve()
    {
        var points = ImmutableSortedDictionary.CreateRange([new KeyValuePair<int, double>(50, 40d)]);
        var states = new Dictionary<int, FanControlStateSnapshot>
        {
            [0] = new() { FanIndex = 0, Mode = FanControlMode.CustomCurve, CustomCurvePoints = points },
        };

        Assert.That(CurveProfile(points).Matches(states), Is.True);
    }

    [Test]
    public void Matches_IsFalse_WhenTheFanRunsADifferentCurve()
    {
        var profilePoints = ImmutableSortedDictionary.CreateRange([new KeyValuePair<int, double>(50, 40d)]);
        var livePoints = ImmutableSortedDictionary.CreateRange([new KeyValuePair<int, double>(50, 80d)]);
        var states = new Dictionary<int, FanControlStateSnapshot>
        {
            [0] = new() { FanIndex = 0, Mode = FanControlMode.CustomCurve, CustomCurvePoints = livePoints },
        };

        Assert.That(CurveProfile(profilePoints).Matches(states), Is.False);
    }

    [Test]
    public void Matches_IsFalse_WhenEveryFanTheProfileKnowsAboutHasGoneAway()
        => Assert.That(CurveProfile([]).Matches(new Dictionary<int, FanControlStateSnapshot>()), Is.False);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~CoolingProfileTests"`
Expected: FAIL — `CoolingProfile` does not exist.

- [ ] **Step 3: Rename the file and the types**

Rename `FanProfile.cs` to `CoolingProfile.cs`. Rename `FanProfile` → `CoolingProfile` and `FanProfileEntry` → `CoolingProfileFanEntry` throughout the solution (`DashboardModel`, `FanProfileManageDialogModel`, `FanProfileNameDialogModel`, `LocalFanProfileStore`). Delete the class-level remark that calls the placement "Deliberately client-side" — it no longer describes the design — and replace it with:

```csharp
/// <remarks>
/// <para>
/// The LIBRARY lives in the service; this is the client's view of one entry in it. What the fans are
/// actually doing is still the service's live state alone, which is why <see cref="Matches"/> exists:
/// "active" is not a stored flag but a comparison, so it stops being true the moment the user changes a
/// fan by hand — exactly when the UI needs to stop claiming a profile is in effect.
/// </para>
/// </remarks>
```

- [ ] **Step 4: Replace the curve-slot field with embedded points**

In `CoolingProfileFanEntry`, delete `public int CurveSlot { get; init; }` and add:

```csharp
    /// <summary>
    /// The curve for <see cref="FanControlMode.CustomCurve"/>, carried by the profile itself.
    /// </summary>
    /// <remarks>
    /// EMBEDDED rather than a slot reference, so the profile is self-contained: overwriting a fan's slot
    /// cannot silently change what this profile means.
    /// </remarks>
    public ImmutableSortedDictionary<int, double> CurvePoints { get; init; } = ImmutableSortedDictionary<int, double>.Empty;

    public TemperatureAggregationMode Aggregation { get; init; } = TemperatureAggregationMode.Maximum;
```

- [ ] **Step 5: Update the curve arm of `EntryMatches`**

Replace the `FanControlMode.CustomCurve` arm with a comparison against the embedded points. Tolerance, not equality, for the same reason the other arms use one — duty comes back through the EC rounded to whole percent:

```csharp
        FanControlMode.CustomCurve =>
            state.Mode == FanControlMode.CustomCurve
            && entry.CurvePoints.Count == state.CustomCurvePoints.Count
            && entry.CurvePoints.All(point =>
                state.CustomCurvePoints.TryGetValue(point.Key, out var live)
                && Math.Abs(live - point.Value) < 1.5d),
```

- [ ] **Step 6: Add the accent colour to `CoolingProfile`**

```csharp
    /// <summary>
    /// The tint this profile paints the shell with, or null for no tint.
    /// </summary>
    /// <remarks>
    /// ARGB rather than a Brush: a model that named a UI type would drag a presentation dependency into
    /// everything that touches a profile, and a Brush built off the UI thread fails silently besides. Null
    /// is also what "no profile selected" looks like, so the tint carries information rather than decoration.
    /// </remarks>
    public uint? AccentColorArgb { get; init; }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~CoolingProfileTests"`
Expected: PASS (3 tests).

- [ ] **Step 8: Commit**

```bash
git add SubZeroFramework.Core/Models/CoolingProfile.cs SubZeroFramework.Tests/CoolingProfileTests.cs
git commit -m "refactor: cooling profiles embed their curve and carry a tint"
```

---

## Task 2: Accent blending and the contrast clamp

**Files:**
- Create: `SubZeroFramework.Core/Services/Cooling/AccentBlend.cs`
- Create: `SubZeroFramework.Core/Models/CoolingAccentPalette.cs`
- Test: `SubZeroFramework.Tests/AccentBlendTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static uint AccentBlend.Blend(uint accentArgb, uint surfaceArgb)` returning an opaque ARGB, `static double AccentBlend.ContrastRatio(uint foregroundArgb, uint backgroundArgb)`, and `static ImmutableArray<uint> CoolingAccentPalette.Tints`.

Pure maths in Core, deliberately free of `Windows.UI.Color`, so it runs in the plain test project with no UI thread.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Cooling;

namespace SubZeroFramework.Tests;

[TestFixture]
public class AccentBlendTests
{
    /// <summary>The sidebar this app blends over: App.xaml's SidebarBackgroundBrush.</summary>
    private const uint Sidebar = 0xFF0B0B0Bu;

    /// <summary>The rail's icon colour, which every blend has to stay readable against.</summary>
    private const uint RailIcon = 0xFFD7D8FFu;

    [Test]
    public void Blend_IsOpaque_SoItCanBePaintedDirectly()
        => Assert.That(AccentBlend.Blend(0xFF0078D7u, Sidebar) >> 24, Is.EqualTo(0xFFu));

    [Test]
    public void Blend_StaysNearTheSurface_BecauseAlphaIsFixedLow()
    {
        // 18% of the way from near-black toward a mid blue must remain dark.
        var blended = AccentBlend.Blend(0xFF0078D7u, Sidebar);
        Assert.That(AccentBlend.ContrastRatio(RailIcon, blended), Is.GreaterThanOrEqualTo(4.5d));
    }

    [Test]
    public void Blend_ClampsAWhiteTint_RatherThanErasingTheIcons()
    {
        var blended = AccentBlend.Blend(0xFFFFFFFFu, Sidebar);
        Assert.That(AccentBlend.ContrastRatio(RailIcon, blended), Is.GreaterThanOrEqualTo(4.5d));
    }

    [Test]
    public void EveryCuratedTint_StaysReadable()
    {
        foreach (var tint in CoolingAccentPalette.Tints)
        {
            var blended = AccentBlend.Blend(tint, Sidebar);
            Assert.That(
                AccentBlend.ContrastRatio(RailIcon, blended),
                Is.GreaterThanOrEqualTo(4.5d),
                $"Tint {tint:X8} leaves the rail icons unreadable.");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~AccentBlendTests"`
Expected: FAIL — `AccentBlend` does not exist.

- [ ] **Step 3: Write the palette**

```csharp
using System.Collections.Immutable;

namespace SubZeroFramework.Models;

/// <summary>
/// The tints a cooling profile may paint the shell with.
/// </summary>
/// <remarks>
/// Drawn from the app's chart palette so a tinted shell still looks like this app. NO AMBERS: the rail's
/// update icon turns amber when a release is available, and a tint in that family would camouflage it.
/// </remarks>
public static class CoolingAccentPalette
{
    public static readonly ImmutableArray<uint> Tints =
    [
        0xFF8AB7E8, // chart accent blue
        0xFFD7D8FF, // chart primary periwinkle
        0xFF6CCB5F, // status success green
        0xFF8A5C5B, // chart error clay
        0xFF5D5E73, // chart muted slate
        0xFFD9706A, // severity critical red
        0xFF7E6BB0, // violet
        0xFF4E9C97, // teal
    ];
}
```

- [ ] **Step 4: Write the blend and clamp**

```csharp
namespace SubZeroFramework.Services.Cooling;

/// <summary>
/// Composites an accent tint over a surface colour and keeps the result readable.
/// </summary>
/// <remarks>
/// BLENDED rather than layered: producing one opaque colour keeps the visual tree free of an overlay
/// element, avoids hit-testing and NavigationView pane-layering questions, and makes the result something a
/// test can assert on directly. Deliberately free of Windows.UI.Color so it lives in Core and needs no UI
/// thread.
/// </remarks>
public static class AccentBlend
{
    /// <summary>How much of the tint reaches the surface. Fixed by the app, never by the user.</summary>
    private const double AccentAlpha = 0.18d;

    /// <summary>The readability floor a blended surface must clear against the rail's icon colour.</summary>
    private const double MinimumContrastRatio = 4.5d;

    private const uint RailIconArgb = 0xFFD7D8FFu;

    /// <summary>
    /// The opaque colour produced by laying <paramref name="accentArgb"/> over <paramref name="surfaceArgb"/>.
    /// </summary>
    /// <remarks>
    /// Steps the alpha back toward the surface until the rail's icons stay readable, so a user who picks
    /// white gets a barely-tinted rail rather than an unusable one. Only the hue is theirs; the strength is
    /// ours.
    /// </remarks>
    public static uint Blend(uint accentArgb, uint surfaceArgb)
    {
        var alpha = AccentAlpha;

        while (true)
        {
            var candidate = Mix(accentArgb, surfaceArgb, alpha);

            if (alpha <= 0d || ContrastRatio(RailIconArgb, candidate) >= MinimumContrastRatio)
            {
                return candidate;
            }

            alpha -= 0.02d;
        }
    }

    /// <summary>The WCAG contrast ratio between two opaque colours, from 1.0 to 21.0.</summary>
    public static double ContrastRatio(uint foregroundArgb, uint backgroundArgb)
    {
        var lighter = Math.Max(RelativeLuminance(foregroundArgb), RelativeLuminance(backgroundArgb));
        var darker = Math.Min(RelativeLuminance(foregroundArgb), RelativeLuminance(backgroundArgb));

        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static uint Mix(uint accentArgb, uint surfaceArgb, double alpha) =>
        0xFF000000u
        | ((uint)Math.Round((((accentArgb >> 16) & 0xFF) * alpha) + (((surfaceArgb >> 16) & 0xFF) * (1d - alpha))) << 16)
        | ((uint)Math.Round((((accentArgb >> 8) & 0xFF) * alpha) + (((surfaceArgb >> 8) & 0xFF) * (1d - alpha))) << 8)
        | (uint)Math.Round(((accentArgb & 0xFF) * alpha) + ((surfaceArgb & 0xFF) * (1d - alpha)));

    private static double RelativeLuminance(uint argb)
    {
        static double Channel(double raw)
        {
            var value = raw / 255d;
            return value <= 0.03928d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Channel((argb >> 16) & 0xFF))
            + (0.7152d * Channel((argb >> 8) & 0xFF))
            + (0.0722d * Channel(argb & 0xFF));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~AccentBlendTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add SubZeroFramework.Core/Services/Cooling/AccentBlend.cs SubZeroFramework.Core/Models/CoolingAccentPalette.cs SubZeroFramework.Tests/AccentBlendTests.cs
git commit -m "feat: blend cooling accents over the sidebar and keep them readable"
```

---

## Task 3: Reserve a curve slot for profile-applied curves

**Files:**
- Modify: `SubZeroFramework.Service/Services/FrameworkFanControlStateStore.cs:18`
- Test: `SubZeroFramework.Tests/ReservedProfileSlotTests.cs`

**Interfaces:**
- Produces: `FrameworkFanControlStateStore.MaxCurveProfileSlots == 6`, `FrameworkFanControlStateStore.ReservedProfileSlot == 5`, `FrameworkFanControlStateStore.UserVisibleCurveProfileSlots == 5`.

`SetCustomCurve` writes into the fan's *active* slot, so an embedded curve has nowhere safe to land. Slot 5 becomes that destination; the user still sees five.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class ReservedProfileSlotTests
{
    [Test]
    public void TheReservedSlot_SitsAboveEveryUserVisibleSlot()
    {
        Assert.That(FrameworkFanControlStateStore.ReservedProfileSlot, Is.EqualTo(FrameworkFanControlStateStore.UserVisibleCurveProfileSlots));
        Assert.That(FrameworkFanControlStateStore.MaxCurveProfileSlots, Is.EqualTo(FrameworkFanControlStateStore.UserVisibleCurveProfileSlots + 1));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~ReservedProfileSlotTests"`
Expected: FAIL — `ReservedProfileSlot` does not exist.

- [ ] **Step 3: Add the constants**

Replace the `MaxCurveProfileSlots` declaration with:

```csharp
    /// <summary>How many curve slots a fan shows the user.</summary>
    public const int UserVisibleCurveProfileSlots = 5;

    /// <summary>
    /// Where a cooling profile's embedded curve lands.
    /// </summary>
    /// <remarks>
    /// RESERVED because SetCustomCurve writes into whichever slot is active: without a destination of its
    /// own, applying a profile would silently overwrite a curve the user had built. Hidden from every slot
    /// picker, so the five the user knows about stay theirs.
    /// </remarks>
    public const int ReservedProfileSlot = UserVisibleCurveProfileSlots;

    /// <summary>Maximum number of unique curve profile slots a single fan can store.</summary>
    public const int MaxCurveProfileSlots = UserVisibleCurveProfileSlots + 1;
```

- [ ] **Step 4: Hide the reserved slot from the pickers**

Search for every consumer that enumerates slots for display:

```bash
grep -rn "MaxCurveProfileSlots" --include=*.cs --include=*.xaml . | grep -v "/obj/" | grep -v "/bin/"
```

In each site that builds a **user-facing** list of slots (the fan detail editor's slot picker and the curve-profile slot tabs), replace `MaxCurveProfileSlots` with `UserVisibleCurveProfileSlots`. Leave storage, validation, and array-sizing sites on `MaxCurveProfileSlots`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~ReservedProfileSlotTests"`
Expected: PASS.

- [ ] **Step 6: Build the whole solution to catch missed slot sites**

Run: `dotnet build SubZeroFramework.sln -v q -nologo`
Expected: `0 Error(s)`, `0 Warning(s)`.

- [ ] **Step 7: Commit**

```bash
git add SubZeroFramework.Service/Services/FrameworkFanControlStateStore.cs SubZeroFramework.Tests/ReservedProfileSlotTests.cs
git commit -m "feat: reserve a curve slot for profile-applied curves"
```

---

## Task 4: Persisted shape

**Files:**
- Create: `SubZeroFramework.Service/Models/CoolingProfileOptions.cs`
- Modify: `SubZeroFramework.Service/Models/FrameworkServiceOptions.cs:48`
- Test: `SubZeroFramework.Tests/CoolingProfileOptionsTests.cs`

**Interfaces:**
- Consumes: `CoolingProfile`, `CoolingProfileFanEntry` (Task 1).
- Produces: `CoolingProfileOptions`, `CoolingProfileFanEntryOptions`, `FrameworkServiceOptions.CoolingProfiles`, `FrameworkServiceOptions.ActiveCoolingProfileId`, and the mappers `CoolingProfileOptions.From(CoolingProfile)` / `CoolingProfileOptions.ToProfile()`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Immutable;
using FrameworkDotnet.Enums;
using NUnit.Framework;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;

namespace SubZeroFramework.Tests;

[TestFixture]
public class CoolingProfileOptionsTests
{
    [Test]
    public void RoundTrip_PreservesEverythingAProfileMeans()
    {
        var original = new CoolingProfile
        {
            Id = "p1",
            Name = "Gaming",
            IconName = "Rocket",
            AccentColorArgb = 0xFF8AB7E8u,
            IsSeeded = true,
            Fans =
            [
                new CoolingProfileFanEntry
                {
                    FanIndex = 1,
                    Mode = FanControlMode.CustomCurve,
                    DutyPercent = 42d,
                    AdaptiveTargetCelsius = 71d,
                    Aggregation = TemperatureAggregationMode.Average,
                    CurvePoints = ImmutableSortedDictionary.CreateRange([new KeyValuePair<int, double>(60, 55d)]),
                },
            ],
        };

        var restored = CoolingProfileOptions.From(original).ToProfile();

        Assert.That(restored, Is.EqualTo(original));
    }

    [Test]
    public void ToProfile_ToleratesAnOptionsBlobWithNoFans()
        => Assert.That(new CoolingProfileOptions { Id = "p1", Name = "Empty" }.ToProfile().Fans, Is.Empty);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~CoolingProfileOptionsTests"`
Expected: FAIL — `CoolingProfileOptions` does not exist.

- [ ] **Step 3: Write the options records and mappers**

```csharp
using System.Collections.Immutable;
using FrameworkDotnet.Enums;
using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Models;

/// <summary>
/// One cooling profile as it is written to service-settings.json.
/// </summary>
/// <remarks>
/// Mutable collection types and a parameterless shape because this is what the configuration binder reads
/// and writes; the immutable <see cref="CoolingProfile"/> is what the rest of the service works with.
/// </remarks>
public sealed record CoolingProfileOptions
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? IconName { get; init; }

    public uint? AccentColorArgb { get; init; }

    public bool IsSeeded { get; init; }

    public CoolingProfileFanEntryOptions[] Fans { get; init; } = [];

    public static CoolingProfileOptions From(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new CoolingProfileOptions
        {
            Id = profile.Id,
            Name = profile.Name,
            IconName = profile.IconName,
            AccentColorArgb = profile.AccentColorArgb,
            IsSeeded = profile.IsSeeded,
            Fans = [.. profile.Fans.Select(CoolingProfileFanEntryOptions.From)],
        };
    }

    public CoolingProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name,
        IconName = IconName,
        AccentColorArgb = AccentColorArgb,
        IsSeeded = IsSeeded,
        Fans = [.. Fans.Select(static entry => entry.ToEntry())],
    };
}

/// <inheritdoc cref="CoolingProfileOptions" />
public sealed record CoolingProfileFanEntryOptions
{
    public int FanIndex { get; init; }

    public FanControlMode Mode { get; init; } = FanControlMode.Auto;

    public double DutyPercent { get; init; }

    public double AdaptiveTargetCelsius { get; init; } = AdaptiveFanSettings.DefaultTargetCelsius;

    public TemperatureAggregationMode Aggregation { get; init; } = TemperatureAggregationMode.Maximum;

    public Dictionary<int, double> CurvePoints { get; init; } = [];

    public static CoolingProfileFanEntryOptions From(CoolingProfileFanEntry entry) => new()
    {
        FanIndex = entry.FanIndex,
        Mode = entry.Mode,
        DutyPercent = entry.DutyPercent,
        AdaptiveTargetCelsius = entry.AdaptiveTargetCelsius,
        Aggregation = entry.Aggregation,
        CurvePoints = new Dictionary<int, double>(entry.CurvePoints),
    };

    public CoolingProfileFanEntry ToEntry() => new()
    {
        FanIndex = FanIndex,
        Mode = Mode,
        DutyPercent = DutyPercent,
        AdaptiveTargetCelsius = AdaptiveTargetCelsius,
        Aggregation = Aggregation,
        CurvePoints = CurvePoints.ToImmutableSortedDictionary(),
    };
}
```

- [ ] **Step 4: Add the two properties to `FrameworkServiceOptions`**

After `FanControlStates`:

```csharp
    /// <summary>The user's saved cooling profiles.</summary>
    /// <remarks>
    /// Alongside FanControlStates rather than in a file of its own: this is already the authority for fan
    /// state, so profiles inherit its save, load, relocate and backup behaviour instead of growing a second
    /// persistence path with its own failure modes.
    /// </remarks>
    public CoolingProfileOptions[] CoolingProfiles { get; init; } = [];

    /// <summary>
    /// Which profile the user last selected, or null if none.
    /// </summary>
    /// <remarks>
    /// A LABEL, not a command. It is never replayed at startup — FanControlStates already restores what the
    /// fans were doing, and re-applying on top would clobber tweaks made after the profile was chosen.
    /// </remarks>
    public string? ActiveCoolingProfileId { get; init; }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~CoolingProfileOptionsTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add SubZeroFramework.Service/Models/CoolingProfileOptions.cs SubZeroFramework.Service/Models/FrameworkServiceOptions.cs SubZeroFramework.Tests/CoolingProfileOptionsTests.cs
git commit -m "feat: persist cooling profiles alongside fan control state"
```

---

## Task 5: The profile store

**Files:**
- Create: `SubZeroFramework.Service/Services/FrameworkCoolingProfileStore.cs`
- Modify: `SubZeroFramework.Service/Program.cs:204`
- Test: `SubZeroFramework.Tests/FrameworkCoolingProfileStoreTests.cs`

**Interfaces:**
- Consumes: `CoolingProfile` (Task 1), `CoolingProfileOptions` (Task 4).
- Produces: `FrameworkCoolingProfileStore` with `IObservable<IChangeSet<CoolingProfile, string>> Connect()`, `string? ActiveProfileId`, `IObservable<string?> ConnectActiveProfileId()`, `void Save(CoolingProfile)`, `void Delete(string id)`, `bool Rename(string id, string name)`, `void SetActive(string? id)`, `void SeedIfEmpty(IReadOnlyCollection<int> fanIndices)`.

- [ ] **Step 1: Write the failing test**

```csharp
using DynamicData;
using NUnit.Framework;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkCoolingProfileStoreTests
{
    private static CoolingProfile Profile(string id, string name) => new() { Id = id, Name = name };

    [Test]
    public void Save_PublishesTheProfileToSubscribers()
    {
        using var store = new FrameworkCoolingProfileStore(NullPersistence.Instance);
        using var observed = store.Connect().AsObservableCache();

        store.Save(Profile("p1", "Gaming"));

        Assert.That(observed.Lookup("p1").Value.Name, Is.EqualTo("Gaming"));
    }

    [Test]
    public void Rename_KeepsTheIdSoItReadsAsARenameRatherThanADeleteAndCreate()
    {
        using var store = new FrameworkCoolingProfileStore(NullPersistence.Instance);
        store.Save(Profile("p1", "Gaming"));

        Assert.That(store.Rename("p1", "Loud"), Is.True);
        using var observed = store.Connect().AsObservableCache();
        Assert.That(observed.Lookup("p1").Value.Name, Is.EqualTo("Loud"));
    }

    [Test]
    public void Delete_AlsoClearsTheSelection_WhenTheDeletedProfileWasTheActiveOne()
    {
        using var store = new FrameworkCoolingProfileStore(NullPersistence.Instance);
        store.Save(Profile("p1", "Gaming"));
        store.SetActive("p1");

        store.Delete("p1");

        Assert.That(store.ActiveProfileId, Is.Null);
    }

    [Test]
    public void SeedIfEmpty_SeedsOnce_AndNeverReSeedsAfterADelete()
    {
        using var store = new FrameworkCoolingProfileStore(NullPersistence.Instance);
        store.SeedIfEmpty([0, 1]);
        using var observed = store.Connect().AsObservableCache();
        var seededCount = observed.Count;
        Assert.That(seededCount, Is.GreaterThan(0));

        foreach (var seeded in observed.Items.ToList())
        {
            store.Delete(seeded.Id);
        }

        store.SeedIfEmpty([0, 1]);

        // Re-seeding what the user threw away reads as a bug, so an emptied library stays empty.
        Assert.That(observed.Count, Is.Zero);
    }
}
```

- [ ] **Step 2: Add the persistence seam the test needs**

The store must be constructible without a running configuration store. Define the seam and a null implementation in the same file as the store:

```csharp
/// <summary>Where the profile library is written. Abstracted so the store is testable without a config file.</summary>
public interface ICoolingProfilePersistence
{
    (IReadOnlyList<CoolingProfile> Profiles, string? ActiveProfileId) Load();

    void Save(IReadOnlyList<CoolingProfile> profiles, string? activeProfileId);
}

/// <summary>Persistence that forgets everything. For tests only.</summary>
public sealed class NullPersistence : ICoolingProfilePersistence
{
    public static readonly NullPersistence Instance = new();

    public (IReadOnlyList<CoolingProfile> Profiles, string? ActiveProfileId) Load() => ([], null);

    public void Save(IReadOnlyList<CoolingProfile> profiles, string? activeProfileId) { }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~FrameworkCoolingProfileStoreTests"`
Expected: FAIL — `FrameworkCoolingProfileStore` does not exist.

- [ ] **Step 4: Write the store**

```csharp
using System.Collections.Immutable;
using DynamicData;
using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// The user's saved cooling profiles and which one they last selected.
/// </summary>
/// <remarks>
/// Modelled on FrameworkFanControlStateStore, including its lock: SourceCache.AddOrUpdate is individually
/// thread-safe, but two concurrent read-modify-writes can interleave so the later publish resurrects the
/// earlier lookup's stale fields.
///
/// It stores a LIBRARY and a LABEL, never a command. What the fans are doing remains the fan state store's
/// answer alone, which is what keeps the two from ever disagreeing.
/// </remarks>
public sealed class FrameworkCoolingProfileStore : IDisposable
{
    private readonly SourceCache<CoolingProfile, string> _profiles = new(profile => profile.Id);
    private readonly BehaviorSubject<string?> _activeProfileId = new(null);
    private readonly ICoolingProfilePersistence _persistence;
    private readonly Lock _gate = new();
    private bool _disposed;

    public FrameworkCoolingProfileStore(ICoolingProfilePersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        _persistence = persistence;

        var (profiles, activeProfileId) = persistence.Load();
        _profiles.AddOrUpdate(profiles);
        _activeProfileId.OnNext(activeProfileId);
    }

    public IObservable<IChangeSet<CoolingProfile, string>> Connect() => _profiles.Connect();

    public IObservable<string?> ConnectActiveProfileId() => _activeProfileId;

    public string? ActiveProfileId => _activeProfileId.Value;

    public void Save(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfDisposed();

        lock (_gate)
        {
            _profiles.AddOrUpdate(profile);
            Persist();
        }
    }

    public void Delete(string profileId)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            _profiles.RemoveKey(profileId);

            // A selection pointing at nothing would leave the UI naming a profile the user just deleted.
            if (string.Equals(_activeProfileId.Value, profileId, StringComparison.Ordinal))
            {
                _activeProfileId.OnNext(null);
            }

            Persist();
        }
    }

    /// <returns>False when no profile has that id.</returns>
    public bool Rename(string profileId, string name)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            var existing = _profiles.Lookup(profileId);
            if (!existing.HasValue)
            {
                return false;
            }

            _profiles.AddOrUpdate(existing.Value with { Name = name });
            Persist();
            return true;
        }
    }

    public void SetActive(string? profileId)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            _activeProfileId.OnNext(profileId);
            Persist();
        }
    }

    /// <summary>
    /// Writes the starting set of profiles, once, if the user has none.
    /// </summary>
    /// <remarks>
    /// ONLY ever when the library is empty, so deleting a seeded profile is permanent. Seeded rather than
    /// shipped empty because an empty Profiles section teaches nothing — the feature is only legible once
    /// there is something on the shelf to apply and compare against.
    /// </remarks>
    public void SeedIfEmpty(IReadOnlyCollection<int> fanIndices)
    {
        ArgumentNullException.ThrowIfNull(fanIndices);
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_profiles.Count > 0)
            {
                return;
            }

            foreach (var seed in CoolingProfileSeeds.Build(fanIndices))
            {
                _profiles.AddOrUpdate(seed);
            }

            Persist();
        }
    }

    private void Persist() => _persistence.Save([.. _profiles.Items], _activeProfileId.Value);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        _disposed = true;
        _profiles.Dispose();
        _activeProfileId.Dispose();
    }
}
```

- [ ] **Step 5: Write the seeds**

Create `SubZeroFramework.Service/Services/CoolingProfileSeeds.cs`:

```csharp
using System.Collections.Immutable;
using FrameworkDotnet.Enums;
using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>The profiles a fresh install starts with.</summary>
/// <remarks>
/// Three, not more: enough to show what a profile is and to give the accent tint something to demonstrate,
/// few enough that the section reads as a shelf rather than a list to manage. Each takes its colour from
/// the curated palette so a fresh install already looks deliberate.
/// </remarks>
public static class CoolingProfileSeeds
{
    /// <summary>What the shipped "gaming" profile holds fans to, canonical Celsius.</summary>
    private const double GamingTargetCelsius = 72d;

    public static ImmutableArray<CoolingProfile> Build(IReadOnlyCollection<int> fanIndices) =>
    [
        new CoolingProfile
        {
            Id = "seed-quiet",
            Name = "Quiet",
            IconName = "VolumeOff",
            AccentColorArgb = CoolingAccentPalette.Tints[4],
            IsSeeded = true,
            Fans = [.. fanIndices.Select(static index => new CoolingProfileFanEntry
            {
                FanIndex = index,
                Mode = FanControlMode.Auto,
            })],
        },
        new CoolingProfile
        {
            Id = "seed-gaming",
            Name = "Gaming",
            IconName = "Rocket",
            AccentColorArgb = CoolingAccentPalette.Tints[0],
            IsSeeded = true,
            Fans = [.. fanIndices.Select(static index => new CoolingProfileFanEntry
            {
                FanIndex = index,
                Mode = FanControlMode.Adaptive,
                AdaptiveTargetCelsius = GamingTargetCelsius,
            })],
        },
        new CoolingProfile
        {
            Id = "seed-full",
            Name = "Full blast",
            IconName = "Fan",
            AccentColorArgb = CoolingAccentPalette.Tints[5],
            IsSeeded = true,
            Fans = [.. fanIndices.Select(static index => new CoolingProfileFanEntry
            {
                FanIndex = index,
                Mode = FanControlMode.Max,
            })],
        },
    ];
}
```

- [ ] **Step 6: Register the store**

In `SubZeroFramework.Service/Program.cs`, after `builder.Services.AddSingleton<FrameworkFanControlStateStore>();`:

```csharp
        builder.Services.AddSingleton<ICoolingProfilePersistence, ServiceOptionsCoolingProfilePersistence>();
        builder.Services.AddSingleton<FrameworkCoolingProfileStore>();
```

Create `SubZeroFramework.Service/Services/ServiceOptionsCoolingProfilePersistence.cs`, reading from `IOptionsMonitor<FrameworkServiceOptions>` and writing through `FrameworkServiceConfigurationStore` — the same pair `FrameworkFanControlStateStore` already uses to persist `FanControlStates`. Map with `CoolingProfileOptions.From` and `ToProfile` from Task 4.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~FrameworkCoolingProfileStoreTests"`
Expected: PASS (4 tests).

- [ ] **Step 8: Commit**

```bash
git add SubZeroFramework.Service/Services/FrameworkCoolingProfileStore.cs SubZeroFramework.Service/Services/CoolingProfileSeeds.cs SubZeroFramework.Service/Services/ServiceOptionsCoolingProfilePersistence.cs SubZeroFramework.Service/Program.cs SubZeroFramework.Tests/FrameworkCoolingProfileStoreTests.cs
git commit -m "feat: a service-owned cooling profile library"
```

---

## Task 6: Applying a profile

**Files:**
- Create: `SubZeroFramework.Service/Services/CoolingProfileApplier.cs`
- Test: `SubZeroFramework.Tests/CoolingProfileApplierTests.cs`

**Interfaces:**
- Consumes: `CoolingProfile`, `CoolingProfileFanEntry` (Task 1), `FrameworkFanControlStateStore.ReservedProfileSlot` (Task 3).
- Produces: `CoolingProfileApplier.Apply(CoolingProfile, IFanCommandTarget)` returning `ImmutableArray<string> failedFanNames`, and the seam `IFanCommandTarget` with `bool TrySetAuto(int)`, `bool TrySetMax(int)`, `bool TrySetDuty(int, double)`, `bool TrySetAdaptive(int, double)`, `bool TrySetCurve(int, IReadOnlyDictionary<int, double>, TemperatureAggregationMode)`, `string DisplayName(int)`, `bool Exists(int)`.

Kept out of the store so it can be tested without persistence, and so the store keeps one responsibility.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Immutable;
using FrameworkDotnet.Enums;
using NUnit.Framework;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class CoolingProfileApplierTests
{
    private sealed class FakeTarget : IFanCommandTarget
    {
        public HashSet<int> Present { get; init; } = [0, 1];
        public HashSet<int> Refusing { get; init; } = [];
        public List<int> Applied { get; } = [];

        public bool Exists(int fanIndex) => Present.Contains(fanIndex);
        public string DisplayName(int fanIndex) => $"Fan {fanIndex}";
        private bool Record(int fanIndex)
        {
            if (Refusing.Contains(fanIndex)) return false;
            Applied.Add(fanIndex);
            return true;
        }
        public bool TrySetAuto(int fanIndex) => Record(fanIndex);
        public bool TrySetMax(int fanIndex) => Record(fanIndex);
        public bool TrySetDuty(int fanIndex, double dutyPercent) => Record(fanIndex);
        public bool TrySetAdaptive(int fanIndex, double targetCelsius) => Record(fanIndex);
        public bool TrySetCurve(int fanIndex, IReadOnlyDictionary<int, double> points, TemperatureAggregationMode aggregation) => Record(fanIndex);
    }

    private static CoolingProfile TwoFanProfile() => new()
    {
        Id = "p1",
        Name = "Gaming",
        Fans =
        [
            new CoolingProfileFanEntry { FanIndex = 1, Mode = FanControlMode.Max },
            new CoolingProfileFanEntry { FanIndex = 0, Mode = FanControlMode.Auto },
        ],
    };

    [Test]
    public void Apply_AppliesInFanIndexOrder_SoTheOutcomeDoesNotDependOnWriteOrder()
    {
        var target = new FakeTarget();

        CoolingProfileApplier.Apply(TwoFanProfile(), target);

        Assert.That(target.Applied, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Apply_KeepsGoing_WhenOneFanRefuses()
    {
        var target = new FakeTarget { Refusing = [1] };

        var failed = CoolingProfileApplier.Apply(TwoFanProfile(), target);

        Assert.That(target.Applied, Is.EqualTo(new[] { 0 }));
        Assert.That(failed, Is.EqualTo(new[] { "Fan 1" }));
    }

    [Test]
    public void Apply_SkipsADepartedFanSilently_SoAProfileSurvivesAModuleBeingRemoved()
    {
        var target = new FakeTarget { Present = [0] };

        var failed = CoolingProfileApplier.Apply(TwoFanProfile(), target);

        Assert.That(target.Applied, Is.EqualTo(new[] { 0 }));
        Assert.That(failed, Is.Empty);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~CoolingProfileApplierTests"`
Expected: FAIL — `CoolingProfileApplier` does not exist.

- [ ] **Step 3: Write the applier**

```csharp
using System.Collections.Immutable;
using FrameworkDotnet.Enums;
using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>What a profile needs of the fan control store, narrowed so applying can be tested alone.</summary>
public interface IFanCommandTarget
{
    bool Exists(int fanIndex);

    string DisplayName(int fanIndex);

    bool TrySetAuto(int fanIndex);

    bool TrySetMax(int fanIndex);

    bool TrySetDuty(int fanIndex, double dutyPercent);

    bool TrySetAdaptive(int fanIndex, double targetCelsius);

    bool TrySetCurve(int fanIndex, IReadOnlyDictionary<int, double> points, TemperatureAggregationMode aggregation);
}

/// <summary>Puts every fan a profile mentions into the state that profile asks for.</summary>
public static class CoolingProfileApplier
{
    /// <returns>The display names of the fans that refused. Empty on complete success.</returns>
    /// <remarks>
    /// BEST EFFORT: one fan refusing must not abandon the rest half-applied, which would leave the machine
    /// in a state no profile describes. Ascending fan index so the outcome does not depend on the order the
    /// profile happened to be written in.
    /// </remarks>
    public static ImmutableArray<string> Apply(CoolingProfile profile, IFanCommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);

        var failed = ImmutableArray.CreateBuilder<string>();

        foreach (var entry in profile.Fans.OrderBy(static entry => entry.FanIndex))
        {
            // Not a failure worth reporting: a profile written while a module was attached should still
            // apply once it is removed, rather than complaining about a fan that is simply gone.
            if (!target.Exists(entry.FanIndex))
            {
                continue;
            }

            var applied = entry.Mode switch
            {
                FanControlMode.Auto => target.TrySetAuto(entry.FanIndex),
                FanControlMode.Max => target.TrySetMax(entry.FanIndex),
                FanControlMode.Manual => target.TrySetDuty(entry.FanIndex, entry.DutyPercent),
                FanControlMode.Adaptive => target.TrySetAdaptive(entry.FanIndex, entry.AdaptiveTargetCelsius),
                FanControlMode.CustomCurve => target.TrySetCurve(entry.FanIndex, entry.CurvePoints, entry.Aggregation),
                _ => target.TrySetAuto(entry.FanIndex),
            };

            if (!applied)
            {
                failed.Add(target.DisplayName(entry.FanIndex));
            }
        }

        return failed.ToImmutable();
    }
}
```

- [ ] **Step 4: Implement the seam over the real store**

Create `SubZeroFramework.Service/Services/FanControlStoreCommandTarget.cs`, wrapping `FrameworkFanControlStateStore`. `TrySetCurve` must write into the reserved slot rather than the active one, so applying a profile cannot destroy a user's curve:

```csharp
    public bool TrySetCurve(int fanIndex, IReadOnlyDictionary<int, double> points, TemperatureAggregationMode aggregation)
    {
        var existing = _store.GetState(fanIndex);
        if (existing is null)
        {
            return false;
        }

        // The RESERVED slot, never the active one: SetCustomCurve writes wherever the fan happens to be
        // pointing, which would overwrite a curve the user built.
        _store.SaveCurveProfile(
            fanIndex,
            FrameworkFanControlStateStore.ReservedProfileSlot,
            name: "Cooling profile",
            curvePoints: points,
            aggregationMode: aggregation,
            drivingSensorIndices: existing.DrivingSensorIndices,
            followFanIndex: null,
            activate: true);

        return true;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SubZeroFramework.Tests --filter "FullyQualifiedName~CoolingProfileApplierTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add SubZeroFramework.Service/Services/CoolingProfileApplier.cs SubZeroFramework.Service/Services/FanControlStoreCommandTarget.cs SubZeroFramework.Tests/CoolingProfileApplierTests.cs
git commit -m "feat: apply a cooling profile across fans, best effort"
```

---

## Task 7: Proto contract

**Files:**
- Modify: `SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto`

**Interfaces:**
- Produces: the generated types `CoolingProfileReply`, `CoolingProfileFanEntryReply`, `CoolingProfileChangeReply`, `CoolingProfileChangeBatchReply`, `CoolingProfileOperationReply`, and the five RPCs named below.

Additive only. No existing field numbers change, so an old client keeps working.

- [ ] **Step 1: Add the RPCs**

In `service FrameworkFanControlService`, after `ResetFanControlToFactoryDefaults`:

```proto
  // Cooling profiles: a NAMED CROSS-FAN setup, distinct from the per-fan curve slots above.
  rpc SaveCoolingProfile (SaveCoolingProfileRequest) returns (CoolingProfileOperationReply);
  rpc DeleteCoolingProfile (DeleteCoolingProfileRequest) returns (CoolingProfileOperationReply);
  rpc RenameCoolingProfile (RenameCoolingProfileRequest) returns (CoolingProfileOperationReply);
  rpc SetActiveCoolingProfile (SetActiveCoolingProfileRequest) returns (CoolingProfileOperationReply);
```

In `service FrameworkTelemetryService`, after `WatchModuleInventory`:

```proto
  rpc WatchCoolingProfiles (WatchCoolingProfilesRequest) returns (stream CoolingProfileChangeBatchReply);
```

- [ ] **Step 2: Add the messages**

Append at the end of the file:

```proto
message WatchCoolingProfilesRequest {}

message CoolingProfileFanEntryReply {
  int32 fan_index = 1;
  FanControlModeValue mode = 2;
  double duty_percent = 3;
  double adaptive_target_celsius = 4;
  // The curve travels WITH the profile, so overwriting a fan's slot cannot change what a profile means.
  repeated FanCurvePointReply curve_points = 5;
  TemperatureAggregationModeValue aggregation = 6;
}

message CoolingProfileReply {
  string id = 1;
  string name = 2;
  string icon_name = 3;
  bool is_seeded = 4;
  repeated CoolingProfileFanEntryReply fans = 5;
  // Tints the shell while this profile is selected. Unset means no tint.
  optional uint32 accent_color_argb = 6;
}

message CoolingProfileChangeReply {
  TelemetryChangeKind change_kind = 1;
  CoolingProfileReply profile = 2;
}

message CoolingProfileChangeBatchReply {
  repeated CoolingProfileChangeReply changes = 1;
  // On EVERY batch, so a client reconnecting mid-session learns the selection without a second round trip.
  // Empty means nothing is selected.
  string active_profile_id = 2;
}

message SaveCoolingProfileRequest {
  CoolingProfileReply profile = 1;
}

message DeleteCoolingProfileRequest {
  string profile_id = 1;
}

message RenameCoolingProfileRequest {
  string profile_id = 1;
  string name = 2;
}

message SetActiveCoolingProfileRequest {
  string profile_id = 1;
}

message CoolingProfileOperationReply {
  string profile_id = 1;
  bool succeeded = 2;
  string message = 3;
  // Fans the profile could not be applied to, by display name. Empty on success.
  repeated string failed_fan_names = 4;
}
```

- [ ] **Step 3: Build the contracts project to generate the types**

Run: `dotnet build SubZeroFramework.GrpcContracts -v q -nologo`
Expected: `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto
git commit -m "feat: cooling profile contract"
```

---

## Task 8: gRPC service endpoints

**Files:**
- Modify: `SubZeroFramework.Service/Services/FrameworkFanControlGrpcService.cs`
- Modify: `SubZeroFramework.Service/Services/FrameworkTelemetryGrpcService.cs`
- Create: `SubZeroFramework.Service/Services/CoolingProfileProtoMapper.cs`

**Interfaces:**
- Consumes: `FrameworkCoolingProfileStore` (Task 5), `CoolingProfileApplier` (Task 6), the generated proto types (Task 7).
- Produces: `CoolingProfileProtoMapper.ToReply(CoolingProfile)` and `CoolingProfileProtoMapper.ToProfile(CoolingProfileReply)`.

- [ ] **Step 1: Write the mapper**

Create `CoolingProfileProtoMapper` translating both ways, mapping `AccentColorArgb` through the proto's `optional uint32` and curve points through `FanCurvePointReply`. Follow the mapping style already used for `FanCurveProfileReply` in this project.

- [ ] **Step 2: Implement the four mutations**

In `FrameworkFanControlGrpcService`, take `FrameworkCoolingProfileStore` in the constructor and add the four handlers. `SetActiveCoolingProfile` applies then records:

```csharp
    public override Task<CoolingProfileOperationReply> SetActiveCoolingProfile(SetActiveCoolingProfileRequest request, ServerCallContext context)
    {
        var profile = _coolingProfileStore.Find(request.ProfileId);
        if (profile is null)
        {
            return Task.FromResult(new CoolingProfileOperationReply
            {
                ProfileId = request.ProfileId,
                Succeeded = false,
                Message = "That profile no longer exists.",
            });
        }

        var failed = CoolingProfileApplier.Apply(profile, _fanCommandTarget);

        // Recorded even on a PARTIAL apply: the user did choose this profile, and drift detection will show
        // it as modified on its own. Refusing to record it would leave the shell naming nothing at all.
        _coolingProfileStore.SetActive(profile.Id);

        var reply = new CoolingProfileOperationReply
        {
            ProfileId = profile.Id,
            Succeeded = failed.IsEmpty,
            Message = failed.IsEmpty ? string.Empty : "Some fans did not accept this profile.",
        };

        reply.FailedFanNames.AddRange(failed);
        return Task.FromResult(reply);
    }
```

Add `public CoolingProfile? Find(string profileId)` to `FrameworkCoolingProfileStore`, returning `_profiles.Lookup(profileId)` as a nullable.

- [ ] **Step 3: Implement the stream**

In `FrameworkTelemetryGrpcService`, add `WatchCoolingProfiles`, following the shape of the existing `WatchFanControlStates`: subscribe to `store.Connect()`, batch changes, and stamp `ActiveProfileId` on every batch from `store.ActiveProfileId`. Also push a batch when `ConnectActiveProfileId()` emits, so selecting a profile reaches clients even when no profile record changed.

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build SubZeroFramework.sln -v q -nologo && dotnet test --filter "TestCategory!=Hardware" -v q --nologo`
Expected: `0 Error(s)`, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add SubZeroFramework.Service/Services/
git commit -m "feat: serve cooling profiles over gRPC"
```

---

## Task 9: Client — stream in, commands out, local store gone

**Files:**
- Create: `SubZeroFramework/Services/ICoolingProfileClient.cs`, `SubZeroFramework/Services/GrpcCoolingProfileClient.cs`
- Delete: `SubZeroFramework/Services/LocalFanProfileStore.cs`
- Modify: `SubZeroFramework/App.xaml.cs`, `SubZeroFramework/Presentation/MenuItems/Dashboard/DashboardModel.cs`

**Interfaces:**
- Consumes: the proto types (Task 7), `CoolingProfile` (Task 1).
- Produces: `ICoolingProfileClient` with `IObservable<IChangeSet<CoolingProfile, string>> WatchCoolingProfiles()`, `IObservable<string?> WatchActiveProfileId()`, `Task<CoolingProfileCommandResult> SaveAsync(...)`, `DeleteAsync(...)`, `RenameAsync(...)`, `SetActiveAsync(...)`.

`ProfilesEnabled` has always shipped `false`, so no user has a `fan-profiles.json` worth migrating — the file is simply orphaned.

- [ ] **Step 1: Write the client interface**

```csharp
using DynamicData;
using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <summary>The service's cooling profile library, and the commands that change it.</summary>
public interface ICoolingProfileClient
{
    IObservable<IChangeSet<CoolingProfile, string>> WatchCoolingProfiles();

    /// <summary>Which profile the service has selected, or null for none.</summary>
    IObservable<string?> WatchActiveProfileId();

    Task<CoolingProfileCommandResult> SaveAsync(CoolingProfile profile, CancellationToken cancellationToken = default);

    Task<CoolingProfileCommandResult> DeleteAsync(string profileId, CancellationToken cancellationToken = default);

    Task<CoolingProfileCommandResult> RenameAsync(string profileId, string name, CancellationToken cancellationToken = default);

    Task<CoolingProfileCommandResult> SetActiveAsync(string profileId, CancellationToken cancellationToken = default);
}

/// <param name="FailedFanNames">Fans that refused. Non-empty with Succeeded false means a PARTIAL apply.</param>
public sealed record CoolingProfileCommandResult(bool Succeeded, string Message, IReadOnlyList<string> FailedFanNames);
```

- [ ] **Step 2: Implement `GrpcCoolingProfileClient`**

Follow `GrpcFanControlStateClient` exactly for the stream (reconnect, `IChangeSet` translation from `TelemetryChangeKind`) and `GrpcFrameworkFanControlClient` for the unary calls. Expose `WatchActiveProfileId` from the `active_profile_id` field stamped on every batch.

- [ ] **Step 3: Register in DI and delete the local store**

In `App.xaml.cs`, remove the `ILocalFanProfileStore` / `LocalFanProfileStore` registration and add:

```csharp
        services.AddSingleton<ICoolingProfileClient, GrpcCoolingProfileClient>();
```

Delete `SubZeroFramework/Services/LocalFanProfileStore.cs`.

- [ ] **Step 4: Rewire `DashboardModel`**

Replace the `ILocalFanProfileStore` dependency with `ICoolingProfileClient`. Delete `ProfilesEnabled`, `AreProfilesAvailable`, every `if (ProfilesEnabled)` guard, the `_profileStore.SeedIfEmpty(...)` call, and the per-fan loop inside `ApplyProfileAsync` — the service does that now. `ApplyProfileAsync` becomes:

```csharp
    /// <summary>Asks the service to switch to a profile.</summary>
    /// <returns>The fans it could not be applied to; empty on success.</returns>
    public async Task<IReadOnlyList<string>> ApplyProfileAsync(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var result = await _coolingProfileClient.SetActiveAsync(profile.Id).ConfigureAwait(true);
        return result.FailedFanNames;
    }
```

Keep `RecomputeProfileSelection` and the drift comparison exactly as they are — that logic is unchanged and stays client-side.

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build SubZeroFramework.sln -v q -nologo && dotnet test --filter "TestCategory!=Hardware" -v q --nologo`
Expected: `0 Error(s)`, `0 Warning(s)`, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add SubZeroFramework/Services/ SubZeroFramework/App.xaml.cs SubZeroFramework/Presentation/MenuItems/Dashboard/DashboardModel.cs
git commit -m "feat: the Dashboard reads profiles from the service"
```

---

## Task 10: Tint the shell

**Files:**
- Modify: `SubZeroFramework/Presentation/MainModel.cs`, `SubZeroFramework/Presentation/MainPage.xaml:26`, `:46`

**Interfaces:**
- Consumes: `AccentBlend.Blend` (Task 2), `ICoolingProfileClient.WatchActiveProfileId` and `WatchCoolingProfiles` (Task 9).
- Produces: `MainModel.ShellAccentBrush` (`Brush`, never null).

- [ ] **Step 1: Add the brush to `MainModel`**

```csharp
    /// <summary>
    /// The background both shell surfaces paint with: the sidebar colour, tinted by the active profile.
    /// </summary>
    /// <remarks>
    /// A FRESH SolidColorBrush per change, built here on the UI thread — never one handed out of
    /// AppThemeBrushes, whose cache returns the single instance App.xaml shares with every
    /// {StaticResource} consumer. Sharing one of those into a control's Foreground has already rendered a
    /// whole rail item blank once.
    /// </remarks>
    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.Brush ShellAccentBrush { get; private set; } =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(SidebarColor);
```

Subscribe to the active profile id, look up its `AccentColorArgb`, and assign on the dispatcher:

```csharp
    private void RefreshShellAccent(uint? accentArgb)
    {
        // No profile selected means NO tint: black has to keep meaning "nothing chosen", otherwise the
        // tint is decoration rather than information.
        var blended = accentArgb is { } accent
            ? AccentBlend.Blend(accent, SidebarArgb)
            : SidebarArgb;

        ShellAccentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToColor(blended));
    }
```

- [ ] **Step 2: Bind both surfaces**

In `MainPage.xaml`, replace `Background="{StaticResource SidebarBackgroundBrush}"` on **both** the `TitleBarHost` Grid (line 26) and the `NavigationView` (line 46) with:

```xml
Background="{x:Bind ViewModel.ShellAccentBrush, Mode=OneWay}"
```

Add above the `TitleBarHost` Grid:

```xml
        <!--
            BOTH surfaces take the tint. They meet at the top-left corner, so tinting one and not the other
            leaves a visible seam exactly where the eye lands first.
        -->
```

- [ ] **Step 3: Build and verify by eye**

Run: `dotnet build SubZeroFramework/SubZeroFramework.csproj -f net10.0-desktop -v q -nologo`
Expected: `0 Error(s)`, `0 Warning(s)`.

Launch the app, select a seeded profile from the Dashboard, and confirm the title bar and rail tint together with no seam, the rail icons stay readable, and deselecting returns both to black.

- [ ] **Step 4: Commit**

```bash
git add SubZeroFramework/Presentation/MainModel.cs SubZeroFramework/Presentation/MainPage.xaml
git commit -m "feat: the active cooling profile tints the shell"
```

---

## Task 11: Palette in the create dialog

**Files:**
- Modify: `SubZeroFramework/Controls/Dashboard/Models/FanProfileNameDialogModel.cs`, `SubZeroFramework/Presentation/MenuItems/Dashboard/FanProfileNameDialog.xaml`

**Interfaces:**
- Consumes: `CoolingAccentPalette.Tints` (Task 2).
- Produces: `FanProfileNameDialogModel.SelectedAccentArgb` (`uint?`).

- [ ] **Step 1: Expose the palette**

Add to `FanProfileNameDialogModel` a `Swatches` collection built from `CoolingAccentPalette.Tints` (each carrying its ARGB and a UI-thread-built brush), plus `[ObservableProperty] public partial uint? SelectedAccentArgb { get; set; }` defaulting to null — no tint.

- [ ] **Step 2: Add the strip to the dialog**

Below the name field, an `ItemsRepeater` of round swatches. Each swatch is a fixed `Height`/`Width` of 28 with `CornerRadius="14"` — a fixed size with the radius at half of it, never `CornerRadius="999"`, which renders an ellipse without a height. Include a "No tint" swatch first, selected by default.

- [ ] **Step 3: Carry the choice through**

In `DashboardModel.CaptureCurrentSetup(string name)`, add an `accentArgb` parameter and set `AccentColorArgb` on the returned `CoolingProfile`. Update the call site to pass `dialog.ViewModel.SelectedAccentArgb`.

- [ ] **Step 4: Build and verify by eye**

Run: `dotnet build SubZeroFramework/SubZeroFramework.csproj -f net10.0-desktop -v q -nologo`
Expected: `0 Error(s)`, `0 Warning(s)`.

Create a profile with a tint, confirm the shell adopts it on apply, and that "No tint" leaves the shell black.

- [ ] **Step 5: Commit**

```bash
git add SubZeroFramework/Controls/Dashboard/Models/FanProfileNameDialogModel.cs SubZeroFramework/Presentation/MenuItems/Dashboard/FanProfileNameDialog.xaml SubZeroFramework/Presentation/MenuItems/Dashboard/DashboardModel.cs
git commit -m "feat: choose a profile's tint when creating it"
```

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: one source of truth → Tasks 5 and 6 (label recorded, commands issued only through the applier); data model → Task 1; embedded-curve destination → Tasks 3 and 6; persistence → Tasks 4 and 5; service architecture → Tasks 5, 6, 8; proto → Task 7; apply semantics and errors → Task 6; boot behaviour → Task 4, where `ActiveCoolingProfileId` is documented as never replayed; accent colour → Tasks 2, 10, 11; client architecture → Task 9; testing → the test step of every task.

**Placeholders.** None. Task 8 steps 1–3 and Task 9 step 2 describe following an existing file's shape rather than pasting a full implementation, because those are mechanical translations of patterns already in the repo (`FanCurveProfileReply` mapping, `GrpcFanControlStateClient` streaming); the named file and the exact behaviour to match are given in each.

**Type consistency.** `CoolingProfile` / `CoolingProfileFanEntry` are used identically from Task 1 onward; `CurvePoints` is `ImmutableSortedDictionary<int, double>` in the model and `Dictionary<int, double>` only in the options record, converted at the boundary in Task 4; `ReservedProfileSlot` is defined in Task 3 and consumed in Task 6; `CoolingProfileCommandResult.FailedFanNames` matches the proto's `failed_fan_names` and the applier's return.

**One gap fixed inline:** Task 8 step 2 adds `FrameworkCoolingProfileStore.Find`, which Task 5 did not define.
