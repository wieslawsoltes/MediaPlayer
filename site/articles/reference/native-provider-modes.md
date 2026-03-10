---
title: "Native Provider Modes"
---

# Native Provider Modes

`MediaPlayerNativeProviderMode` controls how playback and workflow registrations prefer native, interop, or fallback implementations.

| Mode | Meaning |
| --- | --- |
| `LegacyHelpers` | Prefer the existing platform helper implementations where they exist. |
| `InteropOnly` | Force managed interop providers only. |
| `NativeBindingsOnly` | Reserved for direct native-binding providers; current code paths still fall back because a binding-only provider is not implemented yet. |
| `AutoPreferInterop` | Prefer managed interop providers first, then fall back. |
| `AutoPreferBindings` | Prefer native-binding providers when they exist, otherwise use legacy helper or interop fallback. |

## Where it is used

- `MediaPlayerNativeRuntime` for playback configuration
- `AddMediaPlayerWorkflows(...)` for workflow provider selection
- `MEDIAPLAYER_NATIVE_PROVIDER_MODE` environment override for process-level configuration
