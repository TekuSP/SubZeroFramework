---
name: scroll-affordance
description: 'Give every scrollable area in SubZeroFramework its scroll-direction chevrons via ScrollHint. Use when adding a ScrollViewer, ListView, GridView or any other scrolling control, when a page gains scrollable content, or when auditing whether existing scrollable areas all carry the affordance.'
argument-hint: 'Describe the scrollable control or page being added or audited.'
---
# Scroll Affordance

Every scrollable area shows a muted chevron at each end while there is more content that way: **up** when
scrolled down from the top, **down** when there is more below. Without it a page that overflows looks
finished, and users stop at the fold.

## The rule

Any control that can scroll gets `controls:ScrollHint.IsEnabled="True"`. One attribute, nothing else.

```xml
xmlns:controls="using:SubZeroFramework.Controls"

<ScrollViewer controls:ScrollHint.IsEnabled="True" VerticalScrollBarVisibility="Auto">
```

It works on `ScrollViewer` and equally on controls that scroll **inside their own template** — `ListView`,
`GridView`. Applying it to something that turns out not to scroll costs nothing: the chevrons are driven by
live scrollable height, so they simply never appear.

## Finding what needs it

`ScrollViewer` is not the only scrolling control, and a naive grep misses multi-line tags. Both of these
mistakes have been made in this repo. Use:

```bash
grep -rlE "<(ScrollViewer|ScrollView|ListView|GridView|ItemsView|ListBox|TreeView)(\s|>|$)" \
  --include=*.xaml SubZeroFramework/ | grep -v /obj/ | grep -v /bin/
```

Then check each file also contains `ScrollHint.IsEnabled`. `ItemsRepeater` needs nothing of its own — it does
not scroll; its ancestor `ScrollViewer` does.

## How it works, and what that constrains

`ScrollHint` is an attached property that **wraps** at load: it puts a `Grid` where the element was and drops
the element plus the two chevrons into it, moving `Grid.Row` / `Grid.Column` / spans / `Margin` / alignment
onto the wrapper so the page lays out exactly as before.

It wraps rather than overlaying because the ScrollViewers here sit inside Grids, Borders and deeper nestings
in roughly equal measure — a sibling overlay would have needed every page restructured first. It wraps rather
than retemplating `ScrollViewer` because that would mean copying the whole default template and pinning the
app to one Uno version of it.

Consequences worth knowing:

- **The wrapper is real.** Code that walks up from a ScrollViewer expecting its XAML parent will find
  `Grid` named `ScrollHintWrapper` instead. Nothing does this today; keep it that way.
- **A parent it cannot rearrange is left alone.** Only `Panel`, `ContentControl` and `Border` parents are
  handled. Anything else no-ops rather than risking a layout shift — a missing hint beats a moved page.
- **For `ListView` / `GridView` the wrapper goes around the CONTROL**, and the scroll state is read from the
  `ScrollViewer` inside the applied template. Never reparent a template part.
- **Attaching is idempotent.** `Loaded` fires again on every re-parent (navigation caches pages), so the
  wrap checks for itself first.

## When adding a new scrolling control

1. Add the attribute and the `controls` xmlns.
2. If the control scrolls inside its own template and is NOT a `ListView` / `GridView`, check that
   `FindDescendantScrollViewer` reaches its scroller — it takes the shallowest `ScrollViewer` descendant,
   deliberately, so an item's own scroller cannot drive the hint.
3. If content grows after load (telemetry cards arriving), nothing extra is needed: the hint already watches
   the content's `SizeChanged`, not just scrolling and viewer resize.

## Do not

- Do not add a chevron by hand in page XAML. One implementation, one look.
- Do not gate the attribute on whether you think the area scrolls — let the runtime decide.
- Do not put it on `ItemsRepeater`; put it on the `ScrollViewer` that hosts it.
