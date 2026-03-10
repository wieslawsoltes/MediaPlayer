---
title: "Rendering Without Airspace"
---

# Rendering Without Airspace

The playback control uses an Avalonia-hosted GPU composition path so video can live inside the normal visual tree.

## Why this matters

Traditional embedded native media views often rely on a separate child window. That causes airspace issues:

- overlays render behind video
- popups and tooltips clip incorrectly
- transforms and composition effects break
- title-bar and chrome integration become limited

`GpuMediaPlayer` avoids that by deriving from `OpenGlControlBase` and delegating video upload and draw operations to the OpenGL renderer path.

## Texture upload modes

Two upload modes are available:

- direct GPU texture upload, enabled by default
- compatibility copy-upload fallback for drivers that need a safer path

The demo exposes this toggle so consumers can diagnose GPU-driver behavior without changing code.
