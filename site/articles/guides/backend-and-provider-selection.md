---
title: "Backend and Provider Selection"
---

# Backend and Provider Selection

Use native-provider modes when you need to force or diagnose a specific selection path.

## Runtime configuration

```csharp
using MediaPlayer.Controls.Native;
using MediaPlayer.Native.Abstractions;

MediaPlayerNativeRuntime.Configure(new MediaPlayerNativeOptions
{
    ProviderMode = MediaPlayerNativeProviderMode.AutoPreferInterop
});
```

## Environment configuration

```bash
export MEDIAPLAYER_NATIVE_PROVIDER_MODE=AutoPreferInterop
```

## Inspecting the active path

At runtime, inspect:

- `GpuMediaPlayer.ConfiguredNativeProviderMode`
- `GpuMediaPlayer.ActiveNativePlaybackProvider`
- `GpuMediaPlayer.NativePlaybackFallbackReason`
- `GpuMediaPlayer.ActiveDecodeApi`
- `GpuMediaPlayer.ActiveRenderPath`
