# AGENTS.md

## Mission
Implement and evolve a production-grade, no-airspace, GPU-rendered media player control for Avalonia on:
- Windows
- macOS
- Linux

The control must be embeddable in normal Avalonia layouts and support overlays (controls/subtitles/effects) without native child window clipping issues.

## Non-Negotiable Requirements
1. No airspace gap.
2. Rendering remains inside Avalonia visual/compositor tree.
3. Use hardware decode where available and prefer native platform decode APIs.
4. Keep backend architecture swappable (do not hardwire UI to one media engine).
5. Keep API surface simple for app consumers (`Source`, `Play`, `Pause`, `Stop`, `Seek`, `Volume`, `Mute`, `Loop`).

## Current Architecture

### Projects
- `src/MediaPlayer.Controls`
  - Reusable control library.
  - Contains backend abstraction, LibVLC backend, OpenGL renderer, and `GpuMediaPlayer`.
- `src/MediaPlayer.Demo`
  - Desktop demo app to validate playback and integration.

### Key Files
- `src/MediaPlayer.Controls/GpuMediaPlayer.cs`
  - Public Avalonia control.
  - Inherits `OpenGlControlBase`.
  - Exposes control properties and transport methods.
- `src/MediaPlayer.Controls/Backends/IMediaBackend.cs`
  - Backend contract.
- `src/MediaPlayer.Controls/Backends/LibVlcMediaBackend.cs`
  - Platform-aware LibVLC backend implementation.
- `src/MediaPlayer.Controls/Backends/FfmpegMediaBackend.cs`
  - Runtime fallback backend (used when LibVLC cannot initialize on host).
- `src/MediaPlayer.Controls/Rendering/OpenGlVideoRenderer.cs`
  - GPU draw path for video frames.
- `src/MediaPlayer.Demo/MainWindow.axaml`
  - Demo shell + transport UI.

## Platform Strategy

### Decode API Preference
- Windows: Media Foundation + D3D11VA
- macOS: AVFoundation / VideoToolbox
- Linux: VAAPI / VDPAU (depends on system drivers)

These are expressed via LibVLC options in `LibVlcPlatformProfileResolver`.

### Render Strategy
- Decode callbacks deliver frames to control-owned buffers.
- Control uploads frames to an OpenGL texture.
- Texture is rendered by `OpenGlControlBase` into Avalonia composition surface.
- No platform child window embedding is used.

## No-Airspace Policy
Do not introduce:
- `libvlc_media_player_set_hwnd`, `set_nsobject`, `set_xwindow`.
- Native child-host controls for video output.
- Any approach that creates a separate platform surface layered above/below Avalonia content.

Always keep video as texture content rendered by Avalonia-owned composition.

## Quality Bar
1. `dotnet build MediaPlayer.sln` must pass.
2. Control must render within normal Avalonia layout containers.
3. Basic playback operations must function in demo:
   - open source
   - play/pause/stop
   - seek
   - volume/mute/loop
4. Errors must surface via `LastError`.

## Coding Rules for Contributors
1. Preserve public API compatibility unless intentional and documented.
2. Keep backend-specific logic out of `GpuMediaPlayer` when possible.
3. Add platform-specific behavior in backend/profile layer, not UI layer.
4. Maintain thread-safety around frame buffer ownership.
5. Never block UI thread in decode callback paths.
6. Prefer deterministic disposal and explicit event unsubscription.

## Native Runtime Notes
- Windows native libs come from `VideoLAN.LibVLC.Windows`.
- macOS native libs come from `VideoLAN.LibVLC.Mac`.
- Linux relies on system LibVLC installation (package manager).
  - Typical prerequisite: `libvlc` and related codec plugins installed.

### macOS Apple Silicon Note
`VideoLAN.LibVLC.Mac` package used here provides x64 binaries. On arm64 hosts, LibVLC initialization may fail.
When this happens, the control automatically falls back to `ffmpeg`/`ffplay` backend so playback still works.

## Build and Run
```bash
dotnet restore MediaPlayer.sln
dotnet build MediaPlayer.sln
dotnet run --project src/MediaPlayer.Demo/MediaPlayer.Demo.csproj
```

## Known Technical Tradeoff (Current)
Current callback-based integration copies decoded frames into control-owned buffers before GPU upload.
This preserves no-airspace behavior and broad compatibility, but is not a full zero-copy path.

Audio controls in FFmpeg fallback mode are limited (volume/mute are not wired in-process).

## Future Extension Path
1. Add optional zero-copy GPU interop where backend and platform support it.
2. Introduce dedicated native backends (Media Foundation/AVFoundation/GStreamer) under `IMediaBackend`.
3. Add benchmark and diagnostics hooks for frame timing and drop analysis.

## Definition of Done for Feature Work
1. Builds cleanly.
2. No airspace policy preserved.
3. Playback control behavior validated in demo.
4. Docs updated (`PLAN.md` and this `AGENTS.md` when architecture changes).
