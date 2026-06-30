# Framework USB-C expansion-slot capability matrix

Authoritative source: Framework KB "Expansion Card Slot Functionality"
(https://knowledgebase.frame.work/en_us/expansion-card-slot-functionality-r1Wsh4_oWg) — the per-platform
diagrams (JS-rendered; captured from the images, not re-fetchable). These are **static board-design facts** the
EC does NOT report, so they must live in a hardcoded per-Platform table. Capability keys on the **specific
Platform/CPU**, not just `PlatformFamily`.

Legend: **C-data** = USB-C data lane · **DP** = DisplayPort alt-mode + version · **Chg** = charging · **A** = USB-A
card support (⚠ = "higher power consumption" note). "—" = not supported on that slot.

## Framework Laptop 16 — AMD Ryzen 7040 Series (6 slots)
| Slot | C-data | DP | Chg | USB-A |
|---|---|---|---|---|
| 1 | USB4   | DP (ver n/s) | 240W | ⚠ |
| 2 | USB3.2 | DP (ver n/s) | 240W | yes |
| 3 | USB3.2 | — | — (900mA) | yes |
| 4 | USB4   | DP (ver n/s) | 240W | ⚠ |
| 5 | USB3.2 | — | 240W | yes |
| 6 | USB3.2 | — | — (900mA) | yes |

## Framework Laptop 16 — AMD Ryzen AI 300 Series (6 slots)  ← the user's machine
| Slot | C-data | DP | Chg | USB-A |
|---|---|---|---|---|
| 1 | USB4   | DP 2.1 UHBR10 | 240W | ⚠ |
| 2 | USB3.2 | DP 2.1 UHBR10 | 240W | yes |
| 3 | USB3.2 | — | — (900mA) | yes |
| 4 | USB4   | DP 2.1 UHBR10 | 240W | ⚠ |
| 5 | USB3.2 | DP 1.4 HBR3 | 240W | yes |
| 6 | USB3.2 | — | — (900mA) | yes |

## FW16 Expansion Bay (GPU module) — ALSO exposes USB-C ports with DP + PD/charging!
The Framework 16 expansion bay (the rear graphics/spacer module) is **not** just a GPU — its module presents
USB-C ports that support **DisplayPort and Power Delivery/charging**. So the data model must represent
expansion-bay ports as capability-bearing USB-C ports, not just the 6 side slots. (This is what the user meant by
"my NVIDIA module should be a PD port.")

**Each GPU module exposes exactly ONE rear USB-C port** — the diagram's boxes are the *functions* of that single
port (joined by one connector line), not separate ports.

| Module | Ports | C-data | DP | Chg |
|---|---|---|---|---|
| NVIDIA GeForce RTX 5070 | 1 | USB 2.0 | DP 2.1 | up to 240W |
| Radeon RX 7700S | 1 | USB 2.0 | DP 2.1 | — (no charging) |

**Open question for the data model:** does the EC enumerate the bay/GPU-module USB-C ports within the same
`get_pd_info` port set as the 6 side slots, or are they separate? Needs confirming against the EC before deciding
whether they extend the slot array or attach to the `ExpansionBay` descriptor.

## Framework Laptop 13 — AMD Ryzen 7040 Series (4 slots)
| Slot | C-data | DP | Chg | USB-A |
|---|---|---|---|---|
| 1 | USB4   | DP (ver n/s) | yes | ⚠ |
| 2 | USB3.2 | — | yes | yes |
| 3 | USB4   | DP (ver n/s) | yes | ⚠ |
| 4 | USB3.2 | DP (ver n/s) | yes | yes |

## Framework Laptop 13 — AMD Ryzen AI 300 Series  (and **13 Pro AMD AI 300** = identical) (4 slots)
| Slot | C-data | DP | Chg | USB-A |
|---|---|---|---|---|
| 1 | USB4   | DP 2.0 UHBR20 | yes | ⚠ |
| 2 | USB3.2 | DP 1.4 HBR3   | yes | yes |
| 3 | USB4   | DP 2.0 UHBR20 | yes | ⚠ |
| 4 | USB3.2 | DP 2.0 UHBR10 | yes | yes |

## Framework Laptop 13 Pro — Intel Core Ultra Series 3 (4 slots)
| Slot | C-data | DP | Chg | USB-A |
|---|---|---|---|---|
| 1-4 | Thunderbolt 4 | DP 2.1 UHBR20 | up to 140W | yes |

## Framework Laptop 12 — 13th Gen Intel Core (4 slots)
Charging = **64 W official** (≈74 W observed max). All four slots charge.
| Slot | C-data | DP | Chg | USB-A |
|---|---|---|---|---|
| 1 | USB3.2 Gen2x1 | DP 1.4 HBR3 | 64W | yes |
| 2 | USB3.2 Gen2x1 | DP 1.4 HBR3 | 64W | yes |
| 3 | USB3.2 Gen2x2 | DP 1.4 HBR3 | 64W | yes |
| 4 | USB3.2 Gen2x2 | DP 1.4 HBR3 | 64W | yes |

## Framework Desktop — AMD Ryzen AI Max 300 Series (2 **front** expansion slots only)
| Slot | C-data | USB-A |
|---|---|---|
| 1 | USB3.2 | USB3.2 Gen2x1 |
| 2 | USB3.2 | USB3.2 Gen2x1 |
(no charging / no DP on the two front expansion-card slots; rear fixed I/O is separate.)

## Notes for the FFI/data-model work
- "(ver n/s)" = the diagram showed a DP icon but the version text wasn't legible in that image; confirm before
  encoding the exact DP version.
- Slot index in the FFI == diagram slot − 1 (confirmed on the user's FW16: charging on app USB-C 4 = slot 3 = a
  240W-capable port; the "broken" app USB-C 6 = slot 5 = the 900mA non-PD port).
- The 900mA non-PD slots (FW16 slots 3 & 6) are why those ports read `Invalid` today — PD-state query on a
  non-PD port returns garbage. The capability table is what lets us stop showing bogus PD on them.
