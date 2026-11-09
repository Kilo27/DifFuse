---
name: DifFuse
description: Mustang amp editor drawn as a midnight transit diagram; the sound is a route, the amp is the interchange.
colors:
  enamel: "#0A1230"
  chrome: "#070D25"
  sidebar: "#0B1434"
  panel: "#0F1A40"
  panel-line: "#26325C"
  track: "#23305E"
  detent: "#5A6799"
  porcelain: "#F3F1EA"
  text-secondary: "#B6BED6"
  text-muted: "#8A95B8"
  input-bg: "#08102A"
  input-border: "#33406E"
  input-border-hover: "#5A6799"
  hover-bg: "#17234D"
  selected-bg: "#1D2C5E"
  popup-bg: "#0E1839"
  line-amp: "#FFC20E"
  line-stomp: "#F0383A"
  line-mod: "#3F7BFF"
  line-delay: "#2FBF5B"
  line-reverb: "#A978FF"
  connected: "#3DD47A"
  danger-fill: "#C4262A"
typography:
  unit-title:
    fontFamily: "Bahnschrift"
    fontSize: "17px"
    fontWeight: 700
    fontStretch: "condensed"
  preset-name:
    fontFamily: "Bahnschrift"
    fontSize: "25px"
    fontWeight: 700
    fontStretch: "condensed"
  station-label:
    fontFamily: "Bahnschrift"
    fontSize: "14px"
    fontWeight: 700
    fontStretch: "condensed"
  control-label:
    fontFamily: "Bahnschrift"
    fontSize: "11.5px"
    fontWeight: 600
    fontStretch: "condensed"
  control-value:
    fontFamily: "Bahnschrift"
    fontSize: "13px"
    fontWeight: 400
    fontStretch: "semi-condensed"
  body:
    fontFamily: "Bahnschrift"
    fontSize: "13px"
    fontWeight: 400
  section:
    fontFamily: "Bahnschrift"
    fontSize: "11.5px"
    fontWeight: 600
    fontStretch: "semi-condensed"
rounded:
  input: "6px"
  panel: "10px"
  chip: "11px"
spacing:
  unit-gap: "8px"
  unit-padding: "16px 8px"
  cell-gutter: "2px"
components:
  button-primary:
    backgroundColor: "{colors.porcelain}"
    textColor: "{colors.enamel}"
    rounded: "{rounded.input}"
    padding: "6px 14px"
    height: "32px"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.porcelain}"
    rounded: "{rounded.input}"
    padding: "6px 14px"
    height: "32px"
  button-secondary-hover:
    backgroundColor: "{colors.hover-bg}"
  button-danger:
    backgroundColor: "{colors.danger-fill}"
    textColor: "{colors.porcelain}"
    rounded: "{rounded.input}"
  unit-panel:
    backgroundColor: "{colors.panel}"
    rounded: "{rounded.panel}"
  combo:
    backgroundColor: "{colors.input-bg}"
    textColor: "{colors.porcelain}"
    rounded: "{rounded.input}"
    height: "30px"
  knob:
    size: "42px"
---

# Design System: DifFuse

## Overview

A Mustang amp editor drawn as a metro map fired in midnight-blue enamel. Guitar to speaker runs as one
porcelain line across the top; each effect is a station in its own line ink, the amp is the ringed
interchange, and the units below are the stations opened up. The world is flat enamel and porcelain line
work: no photographs of gear, no screws, no brushed metal, no gradients, no glow.

The governing idea is that **a control's shape says what it does**: continuous amounts turn, a handful of
named positions are stops on a line, off/on is a switch, many fixed positions click between detents. Every
control also explains itself in one plain line in the hint bar. The source of truth is
`src/DifFuse.App/Themes/` (Colors, Controls, Templates) and the drawn controls in `src/DifFuse.App/Controls/`
(`Ink.cs` mirrors the colours for code-drawn parts).

## Colors

### Primary

- **Porcelain** (`#F3F1EA`): all line work, rings, the signal route, primary text, focus rings, and the fill
  of the one primary button. It is the "white" of the system; never pure white except as a hover lift.

### Line inks (one per unit)

- **Amp amber** `#FFC20E`, **Stomp scarlet** `#F0383A`, **Modulation cobalt** `#3F7BFF`,
  **Delay green** `#2FBF5B`, **Reverb violet** `#A978FF`.
- An ink marks its unit everywhere it appears: the station on the route, the knob arcs, stop selections, the
  power ring, the before/after dot, switch fills, group-header dots. Amber doubles as the brand accent (the
  "Fuse" in the wordmark, the EDITED and simulated chips, the "you are here" dot in the slot list).

### Neutral

- **Enamel** `#0A1230` is the ground; **Chrome** `#070D25` the header and hint bar; **Sidebar** `#0B1434`;
  **Panel** `#0F1A40` lifts unit panels one step; **Panel line** `#26325C` outlines and rules.
- Text steps: porcelain, secondary `#B6BED6`, muted `#8A95B8` (5:1 on panel).

### Named Rules

- **One ink per unit.** A unit's colour never appears on another unit, and no ink is used decoratively.
- **The route is porcelain.** The signal line is the only thick porcelain stroke; nothing else competes with it.

## Typography

One family carries everything: **Bahnschrift**, the DIN of road and rail signage, using its width axis
instead of a second face.

### Hierarchy

- Preset name: Condensed Bold 25, upper-cased like a station sign.
- Unit titles and station labels: Condensed Bold 17 / 14, upper case.
- Control labels: Condensed SemiBold 11.5, upper case; they shrink to fit (never clip) in narrow columns.
- Values: SemiCondensed 13, porcelain. Section titles: SemiCondensed SemiBold 11.5, upper case.
- Body, lists, inputs: Bahnschrift Regular 13–13.5.

## Layout

- Window: header, then sidebar (slot list or library) beside the route map and the unit stack, then the hint bar.
- At 1320x860 the route and all five units are visible without scrolling; at the 980x600 minimum the stack scrolls.
- Every unit has the same 228 px identity column (power, title, placement, model) so control columns start at
  the same x. Effect units use six equal columns, so knobs line up down the stack.
- Every control cell shares one skeleton: a 16 px label row, a 46 px control row whose centre is the line each
  control sits on, and a value row. Knobs, stops and switches therefore align across groups and units.
- The amp groups controls the way an amp is built: Preamp (Gain, Gain 2, Volume, Bright), EQ low to high
  (Bass, Middle, Treble, Presence), Power amp (Master, Bias, Sag), Noise gate (Gate, Threshold, Depth). Cabinet
  and USB out live in the identity column.

## Elevation & Depth

Flat. Depth is one tonal step (panel over enamel) plus a 1 px panel-line outline. No shadows.

## Shapes

Circles and lines are the vocabulary: rings, stations, stops, tracks. Containers are 10 px rounded panels;
inputs and buttons 6 px; chips fully rounded.

## Components

### Knob (`Controls/Knob.cs`)

Interchange ring (porcelain ring, enamel body, porcelain pointer) inside a 270° track. The value arc is the
unit's ink with station ticks cut in every tenth and a small ink station where the arc ends. Selectors of up to
24 positions draw one detent per position. Drag, wheel, arrows, double-click to reset.

### Stop selector (`Controls/StopSelector.cs`)

For two to six named positions (Sag, noise gate, Type, Shape, Mode). Stops along a track; the chosen stop is a
filled ink station with a porcelain ring and its name in porcelain. When names do not fit, only the chosen
name shows.

### Switches

- Line switch (Bright, High Q): pill outline, fills with the unit ink when on.
- Power (on/bypass): ring that fills with the unit ink when the effect runs.
- Placement: "PRE ●─◎─○ POST", a short line through the amp's interchange with the unit's dot on one side.

### Route map

Guitar terminus, one station per unit in chain order, Output terminus. Running: filled ink station.
Bypassed: dashed hollow ring, label dimmed. Empty: small grey tick. Amp: large porcelain ring with an amber
centre. Clicking a station brings its unit into view and flashes its outline in the unit ink.

### Lists

Preset slots and library presets are stops on a vertical line; the playing slot is the ringed interchange with
an amber centre.

### Buttons

Primary (one per view: "Save to amp…", dialog confirm) is porcelain-filled with enamel text. Secondary is a
porcelain outline. Destructive confirmations use the scarlet danger fill.

## Do's and Don'ts

### Do:

- Pick the control from what the parameter does (`ControlGuide.KindFor`), never one control for everything.
- Give every new control a one-line explanation in `ControlGuide`; the tests fail without one.
- Show only values the amp truly has: 0–10 for amounts, % for Mix, named or numbered positions for selectors.

### Don't:

- Invent units (ms, Hz, semitones) or names for positions the amp does not document.
- Add gear photography, screws, brushed metal, glow or gradients.
- Use a unit's ink for anything outside that unit.
