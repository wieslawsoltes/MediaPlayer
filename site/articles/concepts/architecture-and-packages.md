---
title: "Architecture and Packages"
---

# Architecture and Packages

The repository separates playback UI, native-provider contracts, interop-provider catalogs, and the demo shell.

## Layers

| Layer | Responsibility |
| --- | --- |
| `MediaPlayer.Controls` | `GpuMediaPlayer`, renderer integration, backend orchestration, audio/device APIs, and media workflow services. |
| `MediaPlayer.Native.Abstractions` | Provider enums, diagnostics records, environment-based options, and native-provider contracts. |
| `MediaPlayer.Native.Interop` | Catalogs that describe which managed interop providers are available for playback and workflows. |
| `MediaPlayer.Demo` | QuickTime-style sample application showing how to compose the reusable pieces. |

## Design intent

- Keep playback inside Avalonia rendering instead of hosting a separate native child window.
- Expose provider diagnostics so users can see when native, interop, FFmpeg, or LibVLC paths are active.
- Keep workflow operations reusable so consumers do not need to copy demo-only plumbing.
