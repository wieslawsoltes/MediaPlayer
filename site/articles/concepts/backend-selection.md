---
title: "Backend Selection"
---

# Backend Selection

`GpuMediaPlayer` and the workflow registration layer both select the strongest available provider based on runtime mode, platform, and provider availability.

## Playback side

`GpuMediaPlayer` reads `MediaPlayerNativeRuntime.GetOptions()` and uses the configured `MediaPlayerNativeProviderMode` when constructing its backend.

Available modes:

- `LegacyHelpers`
- `InteropOnly`
- `NativeBindingsOnly`
- `AutoPreferInterop`
- `AutoPreferBindings`

`NativeBindingsOnly` is present as a public mode, but the current implementation still falls back because a dedicated binding-only provider has not been completed yet.

You can configure the mode through `MediaPlayerNativeRuntime.Configure(...)` or the `MEDIAPLAYER_NATIVE_PROVIDER_MODE` environment variable.

## Workflow side

`AddMediaPlayerWorkflows(...)` applies similar mode selection for export, trim, transform, and record operations. The selected path is surfaced through `IMediaWorkflowProviderDiagnostics`.

## Fallback behavior

The project intentionally preserves compatibility paths:

- native helper or interop provider where supported
- FFmpeg fallback when no stronger provider is available
- LibVLC playback compatibility mode for playback scenarios that need it
