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
- `src/MediaPlayer.Controls/Backends/MacOsNativeMediaBackend.cs`
  - macOS native AVFoundation helper backend (non-FFmpeg/non-VLC).
- `src/MediaPlayer.Controls/Backends/WindowsNativeMediaBackend.cs`
  - Windows native MediaFoundation helper backend (non-FFmpeg/non-VLC).
- `src/MediaPlayer.Controls/Backends/NativeFramePumpMediaBackend.cs`
  - Shared native helper frame-pump backend base (probe/play/frame process orchestration).
- `src/MediaPlayer.Controls/Backends/MacOsFfmpegProfileMediaBackend.cs`
  - macOS FFmpeg profile fallback backend (VideoToolbox hint path).
- `src/MediaPlayer.Controls/Backends/WindowsFfmpegProfileMediaBackend.cs`
  - Windows FFmpeg profile fallback backend (D3D11VA hint path).
- `src/MediaPlayer.Controls/Backends/LibVlcMediaBackend.cs`
  - Platform-aware LibVLC backend implementation.
- `src/MediaPlayer.Controls/Backends/FfmpegMediaBackend.cs`
  - Generic FFmpeg fallback backend.
- `src/MediaPlayer.Controls/Rendering/OpenGlVideoRenderer.cs`
  - GPU draw path for video frames.
- `src/MediaPlayer.Demo/MainWindow.axaml`
  - Demo shell + transport UI.

## Platform Strategy

### Backend Selection Order
- macOS:
  1. `MacOsNativeMediaBackend`
  2. `MacOsFfmpegProfileMediaBackend`
  3. `LibVlcMediaBackend`
  4. `FfmpegMediaBackend`
- Windows:
  1. `WindowsNativeMediaBackend`
  2. `WindowsFfmpegProfileMediaBackend`
  3. `LibVlcMediaBackend`
  4. `FfmpegMediaBackend`
- Linux:
  1. `LibVlcMediaBackend`
  2. `FfmpegMediaBackend`

### Decode API Preference
- Windows: Media Foundation + D3D11VA
- macOS: AVFoundation / VideoToolbox
- Linux: VAAPI / VDPAU (depends on system drivers)

These are expressed via native helper backends first, then FFmpeg/LibVLC fallback profiles.

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
- macOS default native backend:
  - Builds a small AVFoundation frame-pump helper with `xcrun swiftc` on first run.
  - Caches helper under `~/Library/Application Support/MediaPlayer/native-helpers/macos`.
  - HTTP/HTTPS sources are cached locally before AVAssetReader playback.
- Windows default native backend:
  - Builds a MediaFoundation/WPF frame-pump helper with `dotnet publish` on first run.
  - Caches helper under `%LOCALAPPDATA%/MediaPlayer/native-helpers/windows`.
- LibVLC fallbacks:
  - Windows native libs come from `VideoLAN.LibVLC.Windows`.
  - macOS native libs come from `VideoLAN.LibVLC.Mac`.
  - Linux relies on system LibVLC installation.

### macOS Apple Silicon Note
`VideoLAN.LibVLC.Mac` may still fail on some arm64 hosts due runtime distribution differences.
The default native AVFoundation backend avoids that dependency and is selected first.

## Build and Run
```bash
dotnet restore MediaPlayer.sln
dotnet build MediaPlayer.sln
dotnet run --project src/MediaPlayer.Demo/MediaPlayer.Demo.csproj
```

### Renderer Selection (Demo)
- Renderer preference can be selected with:
  - CLI: `--renderer=auto|opengl|vulkan|metal|software`
  - Environment: `MEDIAPLAYER_RENDERER=auto|opengl|vulkan|metal|software`
  - macOS menu: `View -> Renderer` (saved for next launch)
- Platform mapping:
  - Windows: Vulkan mode is supported (`Win32RenderingMode.Vulkan`).
  - Linux/X11: Vulkan mode is supported (`X11RenderingMode.Vulkan`).
  - macOS: Avalonia.Native does not expose a Vulkan renderer mode; Vulkan preference maps to Metal/OpenGL/Software fallback order.
- macOS `Auto` prefers Metal first, then falls back to OpenGL and Software.
- Current playback-surface compatibility: `GpuMediaPlayer` uses `OpenGlControlBase`, so runtime renderer is coerced to an OpenGL-capable mode for reliable video rendering. Preferences are still stored and shown for future renderer-path work.

## Known Technical Tradeoff (Current)
Current callback-based integration copies decoded frames into control-owned buffers before GPU upload.
This preserves no-airspace behavior and broad compatibility, but is not a full zero-copy path.

Audio controls in FFmpeg-based backends are limited (volume/mute are not wired in-process).

## Future Extension Path
1. Add optional zero-copy GPU interop where backend and platform support it.
2. Replace helper-process transport with in-process native API bindings for lower copy overhead.
3. Add benchmark and diagnostics hooks for frame timing and drop analysis.

## Definition of Done for Feature Work
1. Builds cleanly.
2. No airspace policy preserved.
3. Playback control behavior validated in demo.
4. Docs updated (`PLAN.md` and this `AGENTS.md` when architecture changes).
