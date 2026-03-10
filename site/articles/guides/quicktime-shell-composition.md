---
title: "QuickTime-style Shell Composition"
---

# QuickTime-style Shell Composition

The demo application is the reference for a QuickTime-inspired playback shell.

## Patterns shown by the demo

- centered floating transport HUD
- auto-hide playback controls
- title-bar integration on macOS
- native macOS menu wiring
- backend and renderer preference toggles
- workflow entry points for export and recording

## Recommendation

Keep your reusable playback control surface in `MediaPlayer.Controls`, then place app-specific shell behaviors in your own view models and services. The demo shows the composition pattern, but the reusable primitives now live in the control library where possible.
