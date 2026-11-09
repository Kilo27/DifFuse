# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

Native Windows 10/11 desktop app (WPF, .NET 10). Not a web surface.

## Users

Guitarists who own a Fender Mustang amp (Mustang I/II, III/IV/V, Bronco, Mini, Floor and the v2 models)
and want to edit and store its sounds from a Windows PC now that Fender FUSE is discontinued. They use it
both at a desk (monitor, mouse, guitar on lap, dialing in and saving presets) and on a laptop near the amp
(smaller screen, quick tweaks between playing). Both must work: the minimum window (980x600) and a large
monitor.

## Product Purpose

DifFuse is a preset editor and librarian for the amp. All sound comes from the amp's own DSP; DifFuse edits
the amp's settings live over USB, saves sounds to the amp's preset slots, and keeps a PC library of
FUSE-format `.fuse` presets in banks. Success: a player can see the whole sound at once, understand what every
control does, change it with confidence, and store it without ever overwriting a slot by accident.

## Positioning

A modern replacement for Fender FUSE on Windows, built from scratch. It speaks the amp's real USB protocol
(layout learned from PLUG) and reads and writes FUSE files, so existing FUSE presets keep working.

## Operating Context

- One amp unit plus four effect units (Stomp, Modulation, Delay, Reverb); each effect can sit before or after
  the amp, and a signal-chain strip shows the order.
- Knob moves go to the amp live, throttled for the USB link.
- Amp preset slots on the device (24 on the simulated Mustang I/II v2) and PC library banks in
  `Documents\DifFuse\Library`.
- A `--simulate` mode and a `--snapshot DIR` renderer exist for working without hardware.

## Capabilities and Constraints

- Amp controls: model, Gain, Gain 2, Volume, Master, Treble, Middle, Bass, Presence, Bias (0-255 each),
  Sag (Soft/Normal/Hard), noise gate (Off/Low/Mid/High/Super/Custom) with Threshold (0-9) and Depth only used
  by the Custom gate, cabinet (13 choices), Bright switch, USB Gain (sent in its own packet; the USB audio
  output level, not part of the amp tone).
- Effect knobs are defined per model in `EffectCatalog`; most are 0-255, some are small selectors
  (Shape 1/2, High Q Off/On, Type 1-4, Mode 1-4, Diatonic Pitch's Pitch/Key/Scale).
- The amp does not report physical units (ms, Hz, semitones) for knob values; DifFuse must not invent them.
- Saving to an amp slot always asks for confirmation first (covered by a test).

## Brand Commitments

- The app is named "DifFuse" everywhere (private repo Kilo27/DifFuse).
- Fender, Mustang and FUSE are Fender trademarks; DifFuse is not affiliated with Fender and must not look like
  an official Fender product.

## Evidence on Hand

- Real amp catalog and effect catalog in `src/DifFuse.Core/Models`.
- Demo presets from the simulated amp (`FakeMustangAmp.DemoPresets`) for screenshots.
- No user testimonials, logos or marketing assets exist.

## Product Principles

1. Every control's form matches what it does: continuous values turn, choices switch, on/off toggles.
2. The whole sound is visible at once; no hunting through tabs to see what the amp is doing.
3. Nothing touches the amp's stored presets without an explicit, confirmed action.
4. Show only what is true about the amp; no invented units or claims.
5. Feels like serious music gear, not a web dashboard and not decorative skeuomorphism.
