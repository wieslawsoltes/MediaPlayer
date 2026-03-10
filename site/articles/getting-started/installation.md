---
title: "Installation"
---

# Installation

## Add the primary package

```bash
dotnet add package MediaPlayer.Controls
```

## Add provider-contract packages when you need composition control

```bash
dotnet add package MediaPlayer.Native.Abstractions
dotnet add package MediaPlayer.Native.Interop
```

## Runtime prerequisites

- .NET 9 SDK or runtime for build and execution.
- Avalonia 11.x application host.
- Platform media dependencies for the backend modes you enable at runtime.
- FFmpeg and LibVLC only when you intentionally use those fallback paths.

## Recommended first validation

1. Add `GpuMediaPlayer` to a window.
2. Load a known local media file.
3. Confirm `ActiveDecodeApi`, `ActiveRenderPath`, and `ActiveNativePlaybackProvider` in a debug view or inspector.
