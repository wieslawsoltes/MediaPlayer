---
title: "Packages and Assemblies"
---

# Packages and Assemblies

## Shipable packages

| Package | Assembly | Summary |
| --- | --- | --- |
| `MediaPlayer.Controls` | `MediaPlayer.Controls.dll` | Public Avalonia control library with playback, rendering, audio, and workflow APIs. |
| `MediaPlayer.Native.Abstractions` | `MediaPlayer.Native.Abstractions.dll` | Contracts and diagnostics models for native provider selection. |
| `MediaPlayer.Native.Interop` | `MediaPlayer.Native.Interop.dll` | Interop provider catalogs and runtime selection helpers. |

## Non-packable projects

| Project | Purpose |
| --- | --- |
| `MediaPlayer.Demo` | Reference shell application for UX, menus, diagnostics, and workflow composition. |
| `MediaPlayer.Controls.Tests` | Unit tests for controls, timeline seeking, workflow services, and backend behavior. |
| `MediaPlayer.Demo.Tests` | Demo-specific coverage for reusable application-layer services and state. |
