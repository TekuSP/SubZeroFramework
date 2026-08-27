# Claude Design prompt — Adaptive fan control, self-learning

Paste everything below the line into Claude Design.

---

Design the **Adaptive fan control** UI for SubZero — Framework Edition, a WinUI 3 / Uno Platform desktop app
that controls the fans on Framework laptops. Companion to the existing `design_handoff_subzero_fans` and
`design_handoff_adaptive_fans` bundles: reuse their brushes, shell rules and fan-editor anatomy. Same dark
palette (`AppBackgroundBrush #1b2727`, `CardBackgroundBrush #2e2e2e`, accent `#0078D7`, success `#6ccb5f`,
warning `#c5994e`, violet `#8AB7E8`, text `#fffffa` / `#D7D8FF`).

## The one thing that changed, and why it changes the screens

The previous handoff treats a one-time **hot-test calibration as a gate**: an uncalibrated fan cannot use
Adaptive, and the editor shows a lockout with a "Calibrate this fan" call to action.

**That is now inverted.** The controller identifies the machine's thermal model *from ordinary use* —
recursive least squares over settled operating points, fitting `T ≈ a + b·P − K·duty`. So:

- Adaptive can be armed **immediately**, on any fan, with no calibration.
- It starts from deliberately conservative defaults and **gets better the longer the machine runs**.
- The hot test still exists but is now an **optional accelerator** — "learn this in 4 minutes instead of
  over the next few days" — not a prerequisite.

So the central design problem is no longer a lockout. **It is representing confidence honestly over time**,
without making the user anxious about a fan that is working fine.

Design for these three states as a continuum, not as separate modes:

| State | What is true | What the user should feel |
|---|---|---|
| **Learning** | Running on safe defaults. Model not yet separable. | "It works. It is getting to know my machine." |
| **Converging** | Model identified, still refining. Confidence rising. | Quiet progress. Not a warning. |
| **Confident** | Model stable, many observations. | Nothing to do. Numbers are trustworthy. |

Explicitly avoid: progress bars that imply the fan is broken until full, percentage-complete framing,
badges that read like errors, and anything that makes "still learning" feel like a fault. A fan on defaults
is a *working fan*, not a degraded one.

## Screen 1 — Adaptive in the fan editor

Sits in the existing fan-editor detail pane, selected from the mode segmented control
(`Auto · Manual · Curve · Max · Adaptive`, Adaptive icon `tune-vertical-variant`).

### 1. Controller readout — the most important element

An adaptive fan that changes speed for reasons the user cannot see is indistinguishable from a broken one.
This card is the answer to "why did it just speed up?".

- Commanded setpoint (large, accent) and actual speed, both in RPM.
- Driving temperature vs target: `64 °C of 78 °C target`.
- **Stacked contribution bar**, height 16, radius 8, recessed track, 1px dividers. Four segments, each
  sized by its share:

  | Term | Colour | Plain meaning |
  |---|---|---|
  | Feed-forward | accent `#0078D7` | cooling for heat being produced right now |
  | PI trim | violet `#8AB7E8` | closing the remaining gap to target |
  | Lead | success `#6ccb5f` | temperature is still rising |
  | Throttle escalation | warning `#c5994e` | latched after the CPU reported throttling |

  Legend below: 10×10 radius-3 swatch + term + signed value in RPM (`3,880 RPM`, `+920 RPM`). **Omit
  zero-contribution terms entirely** — no empty legend rows.
- One-sentence explanation line (`lightbulb-on-outline`, tertiary text) that changes with what dominates.

### 2. Confidence — the new element, and the hard one

This is what you are really designing. It must sit near the controller readout without competing with it.

Bound data available: observation count, whether the model is identified yet, when it last improved, and
whether the fan has ever had a hot test. Show *what the machine has figured out*, not a completion metric.

Try several treatments and pick the one that reads as reassuring rather than pending. Some directions:
a sparkline of the model settling; a plain sentence that upgrades over time ("Still getting to know this fan"
→ "Learned from 40 quiet periods"); a subtle chip that only becomes prominent when something is *wrong*
(model contradicted, likely blocked vent). Do not default to a progress ring.

### 3. Target temperature

Card, `thermometer-check`. Slider 60–95 °C, step 1, current value large on the right. End labels
"60 °C · coolest" and "95 °C · quietest". One line: *"Adaptive holds the driving temperature here, using as
little airflow as it can. Lower runs cooler and louder; higher runs quieter and warmer."*

### 4. Response — the λ knob

**One tuning knob, and it must never look like a control-theory parameter.** Internally λ is the closed-loop
time constant, 2–16 s, default 8 s. The user must see consequences, not seconds:

- A slider from **Quick** to **Calm**.
- Two live readouts that update as it moves: *"Back on target within ~{n} s"* and *"Fan speed changes:
  restless / busy / steady / very calm"*.
- Optionally a small response-curve preview showing overshoot shrinking and settling lengthening.
- The raw value (`λ = 8 s`, derived `Kc`, `τᵢ`) belongs in a collapsed "advanced" disclosure at most.

### 5. Safety floor

Card, `shield-half-full`, toggle on the right. When on: slider 0–60 % plus caption bound to the *measured*
minimum spin — *"Below about {n} RPM this fan stalls."*

### 6. Throttle escalation — latched banner

Above the controller card when latched. Warning wash, warning border, **3px left accent bar**,
`lock-alert-outline`, title "Throttle escalation latched", small `HELD` badge (height 20, radius 10).
Message names the time it engaged and counts down the release. A subtle **Release now** button.

## Screen 2 — Optional calibration

Reframe entirely. This is **not** a gate and must not be presented as required setup.

- Entry point is an offer, not a warning: *"Adaptive is already learning this fan. A 4-minute test would
  teach it everything at once."*
- Consent screen must be honest that it **loads the CPU deliberately and runs the fan at maximum**, needs
  AC power, and that the user should leave the machine alone.
- Progress: current step, `Step {n} of 7`, estimated time remaining, and a live temperature/duty plot with
  the step change marked.
- Cancel is prominent at every moment; nothing is written until it completes.
- Result screen states what was learned in **plain language first**, jargon in a sub-line — e.g.
  "How much this fan cools · 0.42 °C per 1%" with the sub-line explaining it.
- Failure states must each say what went wrong, that **nothing was saved**, and that **the fans were
  restored** — and by whom, when the service did it.

## Screen 3 — Dashboard profiles

Profiles are **saved per-fan configurations**, not duty shortcuts. A profile stores each fan's mode and
settings (`Left → Curve`, `Right → Adaptive 78 °C`, `APU → Manual 45%`). Tiles show the real configuration
as a subtitle, not a mood. A dashed **New profile** tile creates one from the current state. When the live
configuration drifts from the applied profile, show a **Modified** chip and a **Save as profile** action,
with no tile highlighted.

Per-fan cards get all five modes as a compact icon segmented control, and the control row underneath
**changes with the selected mode** — the previous design showed a duty stepper permanently, which
contradicted whichever mode was active.

## Constraints

- **WinUI 3 / Uno XAML** — deliver as design references, not production markup.
- Every temperature, speed, power and percentage renders through the app's unit-formatting service,
  **including slider bounds** — a Fahrenheit user sees 140–203 °F on the target slider.
- Adaptive must fit the existing **Stage → Preview → Apply** model: target, λ, floor and the mode switch all
  stage. **Release now is immediate and never staged.**
- Do not restyle the title bar or the left nav rail.
- Pills are a fixed height with `CornerRadius = height/2`; `CornerRadius=999` without a height renders as an
  ellipse in Uno.
- Small charts need `TextSize="0"` and zero axis padding or they render nothing.
- Assume dark theme as primary.

## What I most need from you

1. The **confidence treatment** — several options, because this is the genuinely new design problem and the
   obvious answers (progress bar, percentage) are all wrong.
2. The **λ slider** presented as felt consequences rather than a number.
3. How **calibration reads as optional** without being so buried that nobody discovers it.
