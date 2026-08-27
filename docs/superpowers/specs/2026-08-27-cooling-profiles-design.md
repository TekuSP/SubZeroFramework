# Cooling Profiles — Design

**Date:** 2026-08-27
**Status:** Approved for planning

## Goal

Let the user keep several named fan setups — "Silent", "Gaming", "Quiet night" — and switch
between them in one action. The service owns the library, the service is told which one is
selected, and every connected client learns about both over a stream rather than by polling.

A selected profile also tints the title bar and the navigation rail, so the machine's current
cooling mood is visible from any page without going to look for it.

## Why this is not just "turn the flag on"

Most of the client side already exists behind `DashboardModel.ProfilesEnabled`, which has always
shipped `false`: both ContentDialogs, `FanProfileCardModel`, `ApplyProfileAsync`,
`CaptureCurrentSetup`, and drift detection through `FanProfile.Matches`.

What does not exist is any service-side notion of a profile. Today the library lives in
`LocalFanProfileStore`, a per-user JSON file under `LocalApplicationData`, and
`SubZeroFramework.Core/Models/FanProfile.cs` documents that placement as deliberate:

> Deliberately client-side. A profile is a named batch of commands the service already accepts one
> at a time, so teaching the service about profiles would add a second place for fan intent to live
> and a way for the two to disagree.

That objection is correct, and this design does not override it — it satisfies it. See
**One source of truth** below.

## Decisions

| Question | Decision |
|---|---|
| What "active" means | The service persists the selected profile id. Clients still compute drift from live fan state. |
| How a curve is captured | The profile embeds its own curve points. |
| Starting profiles | Seeded once when the library is empty, then ordinary editable profiles. |
| Accent colour | Stored on the profile; tints both the title bar and the rail. |

## One source of truth

The service stores **a library and a label**, never a competing command.

- Live per-fan state remains the only authority on what the fans are doing.
- `ActiveCoolingProfileId` records which profile the user last selected. It is a *name for the
  current setup*, not an instruction that gets replayed.
- "Modified" is still derived: `FanProfile.Matches` compares the profile against live fan states,
  exactly as it does today. Change one fan by hand and the UI reads "Gaming (modified)".

This is what keeps the two places from disagreeing: only one of them ever issues commands.

## Non-goals

- No automatic switching on AC/battery, per-application, or on a schedule.
- No import/export or sharing of profiles between machines.
- Profiles capture fan behaviour only — not charge limits or any other power setting.
- No re-applying the active profile at service startup (see **Boot behaviour**).

## Naming

The existing per-fan `FanCurveProfile` — a numbered slot on one fan — keeps its name and its RPCs
unchanged. The new cross-fan concept is a **Cooling Profile**, which is already the vocabulary in
`DashboardModel`'s header comment. Nothing existing is renamed, so no in-flight work breaks.

## Data model

```
CoolingProfile
  Id                 string    stable across renames
  Name               string
  IconName           string?   null lets the UI derive one from the setup
  AccentColorArgb    uint?     null means no tint (see Accent colour)
  IsSeeded           bool
  Fans               CoolingProfileFanEntry[]

CoolingProfileFanEntry
  FanIndex               int
  Mode                   FanControlMode
  DutyPercent            double            Manual only
  AdaptiveTargetCelsius  double            Adaptive only
  CurvePoints            (int -> double)   CustomCurve only, EMBEDDED
  Aggregation            TemperatureAggregationMode
```

Fields that the entry's mode does not use are kept rather than validated away — a profile saved
while a fan was Manual keeps its duty even after being re-saved with that fan on Auto. This
preserves today's behaviour, where discarding them would make re-saving quietly destructive.

Driving sensors are deliberately **not** captured. As the current code puts it, the profile carries
the target and the fan keeps its own sensors; which sensors drive a fan is a property of the
hardware, not of the mood the user is in.

### The embedded-curve destination

`FrameworkFanControlStateStore.SetCustomCurve` saves into the fan's *currently active slot*. An
embedded curve therefore has nowhere non-destructive to land: applying a profile through that path
would silently overwrite whatever the user had in that slot.

**Resolution:** `MaxCurveProfileSlots` rises from 5 to 6, and the new highest slot is reserved as
the profile-applied slot. It is excluded from the per-fan slot UI and from the slot pickers, so the
user still sees five slots and profile application never destroys one of them.

This is the cost of embedding rather than referencing. It is accepted because a self-contained
profile survives someone overwriting a slot, which a slot reference does not.

## Persistence

Cooling profiles join `FrameworkServiceOptions`, the same options record that already carries
`FanControlStates[]` and is written to `service-settings.json`:

```
FrameworkServiceOptions
  + CoolingProfiles          CoolingProfileOptions[]
  + ActiveCoolingProfileId   string?
```

Reusing this path means profiles inherit the existing save, load, relocate, and backup behaviour of
`FrameworkServiceConfigurationStore` rather than growing a second persistence mechanism with its own
failure modes. The client's `LocalFanProfileStore` had a good reason for its own file — it was
guarding against a corrupt *client* settings file — but that reasoning does not carry over to a
service store that is already the authority for fan state.

## Service architecture

**`FrameworkCoolingProfileStore`** (new, `SubZeroFramework.Service/Services/`), modelled directly on
`FrameworkFanControlStateStore`:

- `SourceCache<CoolingProfile, string>` keyed by id.
- `Connect()` returns `IObservable<IChangeSet<CoolingProfile, string>>`.
- A `Lock` serialising every lookup → mutate → publish sequence, for the same reason the fan store
  has one: two concurrent read-modify-writes can otherwise republish stale fields.
- `ActiveProfileId` with a change notification.
- `SeedIfEmpty(fanIndices)` — writes the starting set once, only when the library is empty, so
  deleting a seeded profile is permanent.
- Persists through `FrameworkServiceConfigurationStore` on every mutation.

**`FrameworkFanControlGrpcService`** gains the profile mutations, and applies a selected profile by
calling the per-fan store methods it already uses for individual commands.

## Proto contract

Mutations join `FrameworkFanControlService`, alongside the existing per-fan profile RPCs:

```proto
rpc SaveCoolingProfile      (SaveCoolingProfileRequest)      returns (CoolingProfileOperationReply);
rpc DeleteCoolingProfile    (DeleteCoolingProfileRequest)    returns (CoolingProfileOperationReply);
rpc RenameCoolingProfile    (RenameCoolingProfileRequest)    returns (CoolingProfileOperationReply);
rpc SetActiveCoolingProfile (SetActiveCoolingProfileRequest) returns (CoolingProfileOperationReply);
```

The stream joins `FrameworkTelemetryService`, where `WatchFanControlStates` already lives:

```proto
rpc WatchCoolingProfiles (WatchCoolingProfilesRequest) returns (stream CoolingProfileChangeBatchReply);
```

Messages follow the established change-batch shape, so the client can reuse the existing
`TelemetryChangeKind` plumbing:

```proto
message CoolingProfileReply {
  string id = 1;
  string name = 2;
  string icon_name = 3;
  bool is_seeded = 4;
  repeated CoolingProfileFanEntryReply fans = 5;
  optional uint32 accent_color_argb = 6;
}

message CoolingProfileFanEntryReply {
  int32 fan_index = 1;
  FanControlModeValue mode = 2;
  double duty_percent = 3;
  double adaptive_target_celsius = 4;
  repeated FanCurvePointReply curve_points = 5;
  TemperatureAggregationModeValue aggregation = 6;
}

message CoolingProfileChangeReply {
  TelemetryChangeKind change_kind = 1;
  CoolingProfileReply profile = 2;
}

message CoolingProfileChangeBatchReply {
  repeated CoolingProfileChangeReply changes = 1;
  // Sent on every batch so a client that reconnects mid-session learns the selection without a
  // second round trip. Empty means no profile is selected.
  string active_profile_id = 2;
}

message CoolingProfileOperationReply {
  string profile_id = 1;
  bool succeeded = 2;
  string message = 3;
  // Fans the profile could not be applied to, by display name. Empty on success.
  repeated string failed_fan_names = 4;
}
```

## Apply semantics and errors

`SetActiveCoolingProfile` applies every entry to every fan **best-effort on the service side**: one
fan refusing must not abandon the rest half-applied. This is the contract `ApplyProfileAsync`
already implements in `DashboardModel`, moved down to the service so every client gets it.

- Entries are applied in ascending fan index, so the outcome does not depend on the order the
  profile happened to be written in.
- A fan named by the profile that no longer exists is skipped silently. A profile written while a
  module was attached should still apply once it is removed.
- Fans that refuse are collected and returned in `failed_fan_names`. The client surfaces them in an
  `InfoBar` with `Warning` severity — the profile did partly take effect, so `Error` would overstate
  it.
- The active id is set even on partial success, because the user did choose that profile; drift
  detection will independently show it as modified.

## Boot behaviour

The service does **not** re-apply the active profile at startup. It already restores per-fan state
from `FanControlStates[]`, and replaying the profile on top would clobber deliberate tweaks made
after the profile was applied. The remembered id is a label, so the UI comes back saying
"Gaming (modified)" rather than silently resetting the fans.

## Accent colour

A selected profile may tint the shell. Black — no tint — is the default and also what "no profile
selected" looks like, so the tint carries real information rather than being decoration.

**Blended, not layered.** The tint is composited over the sidebar colour in code, producing one
opaque `SolidColorBrush`. No extra element enters the visual tree, there are no hit-testing or
`NavigationView` pane-layering questions, and the resulting colour is directly assertable in a test.

**The user picks the hue; the app fixes the alpha.** A raw colour would let someone erase the rail's
own contrast — its icons are `#D7D8FF` and its selection indicator is `#0078D7`. So:

- A curated strip of eight tints drawn from the existing chart palette is the primary choice.
- A `ColorPicker` is available as the escape hatch, but only the hue is taken from it.
- Alpha is fixed by the app at 18%.
- Any blend whose result would drop icon contrast below a 4.5:1 ratio against `#D7D8FF` is clamped
  toward the sidebar colour until it passes.
- The curated palette contains no ambers, so a tint can never camouflage the amber update icon.

**Both surfaces.** `TitleBarHost` and the `NavigationView` both paint from `SidebarBackgroundBrush`
(`MainPage.xaml:26` and `:46`). Both are bound to the blended brush; tinting only one leaves a
visible seam in the top-left corner where they meet.

**Thread affinity.** The blended brush is constructed on the UI thread and is a fresh instance per
change, never a cached or shared one. A brush shared out of a static cache is what recently rendered
an entire rail item blank.

## Client architecture

**Deleted:** `ILocalFanProfileStore`, `LocalFanProfileStore`, `DashboardModel.ProfilesEnabled` and
its flag checks, and `SeedIfEmpty` on the client. `ProfilesEnabled` has always shipped `false`, so
no user has a `fan-profiles.json` worth migrating — the file is simply orphaned.

**Added:** `ICoolingProfileClient` / `GrpcCoolingProfileClient`, following the established
`IFanControlStateClient` shape:

```csharp
public interface ICoolingProfileClient
{
    IObservable<IChangeSet<CoolingProfile, string>> WatchCoolingProfiles();
    IObservable<string?> WatchActiveProfileId();

    Task<CoolingProfileCommandResult> SaveAsync(CoolingProfile profile, CancellationToken cancellationToken = default);
    Task<CoolingProfileCommandResult> DeleteAsync(string profileId, CancellationToken cancellationToken = default);
    Task<CoolingProfileCommandResult> RenameAsync(string profileId, string name, CancellationToken cancellationToken = default);
    Task<CoolingProfileCommandResult> SetActiveAsync(string profileId, CancellationToken cancellationToken = default);
}
```

**Changed:** `DashboardModel` swaps `ILocalFanProfileStore` for `ICoolingProfileClient` and drops
`ApplyProfileAsync`'s per-fan loop, which now lives in the service. Its drift computation is
untouched. `MainModel` gains an `AccentBrush` fed by the active profile, bound by both shell
surfaces.

`FanProfile` and `FanProfileEntry` are renamed to `CoolingProfile` and `CoolingProfileFanEntry`, so
the client model, the proto, and the Dashboard's vocabulary all say the same word. The type otherwise
only gains embedded points and a colour, so both dialogs keep working; `FanProfileNameDialog` gains
the palette strip.

## Testing

Everything here is testable without hardware and belongs in `SubZeroFramework.Tests`.

**Core:** `FanProfile.Matches` keeps its existing cases and gains embedded-curve ones; colour
blending and the contrast clamp get direct tests, including that a white tint is clamped and that
the curated palette all passes.

**Service:** `FrameworkCoolingProfileStore` round-trips through options; `SeedIfEmpty` seeds exactly
once and never re-seeds after a delete; applying a profile with one refusing fan still applies the
rest and reports the refusal; a profile naming a departed fan skips it silently.

**Contract:** the reserved profile slot is excluded from the slot pickers, and applying a profile
does not disturb slots 0–4.

## Risks

**Rail restyling.** The project has a standing constraint that the title bar and nav rail are not
restyled and only page content changes. The accent colour deliberately overrides it for this
feature. Nothing else about those surfaces changes.

**Slot budget.** Reserving a sixth slot is a consequence of embedding curves. If the reserved slot
later proves awkward, the fallback is the "embed plus slot hint" model, where the profile remembers
which slot its curve came from and refreshes that slot instead.

**Proto growth.** `framework_telemetry.proto` is already large. These messages are additive and
reserve no field numbers from existing messages, so old clients keep working.
