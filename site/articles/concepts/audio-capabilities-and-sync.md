---
title: "Audio Capabilities and Sync"
---

# Audio Capabilities and Sync

The control surface exposes audio-device, route, and level information without forcing callers to know which backend is active.

## Audio-related APIs

`GpuMediaPlayer` exposes:

- `AudioCapabilities`
- `GetAudioInputDevices()`
- `GetAudioOutputDevices()`
- `GetAudioRouteState()`
- `TryGetAudioLevels(out MediaAudioLevels levels)`

## Sync responsibilities

Backends are responsible for maintaining playback timing and seek behavior. The reusable `TimelineSeekController` coordinates drag-based seeking so slider interactions can stay responsive across both fast and slow backends.

## Capability model

`MediaAudioCapabilities` gives the application a backend-neutral way to decide whether route changes, level meters, loopback, or device selection should be shown in the UI.
