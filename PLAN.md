# MediaPlayer Implementation Plan

## Goal
Build a cross-platform Avalonia media player control with:
- no airspace gap,
- hardware-accelerated decode pipelines,
- GPU-rendered presentation inside the Avalonia visual tree,
- native platform decode APIs selected per OS.

## Constraints
- Must run on macOS, Windows, Linux.
- Must not host platform child windows (`HWND`/`NSView`/X11 child) inside Avalonia content.
- Must expose an embeddable control API for app integration.

## Delivery Phases

### Phase 1 (implemented)
1. Recreate solution and projects from scratch.
2. Implement `GpuMediaPlayer` control using `OpenGlControlBase`.
3. Implement platform-aware backend selection and LibVLC integration.
4. Configure native decode hints by OS:
   - Windows: Media Foundation + D3D11VA.
   - macOS: AVFoundation / VideoToolbox.
   - Linux: VAAPI / VDPAU (driver dependent).
5. Wire decode callbacks to an OpenGL texture rendered by Avalonia.
6. Build demo app with transport controls and diagnostics.
7. Validate with `dotnet build`.
8. Add runtime fallback backend (`ffmpeg`/`ffplay`) when LibVLC cannot initialize (notably on macOS arm64 with x64-only runtime package).

### Phase 2 (next)
1. Add automated headless render tests and image snapshots.
2. Add frame pacing metrics and dropped-frame telemetry.
3. Improve seek behavior and buffering UI feedback.
4. Add subtitle and multi-audio track switching.

### Phase 3 (advanced optimization)
1. Introduce optional zero-copy GPU interop path per backend where available.
2. Add backend plugins for dedicated native engines (Media Foundation/AVFoundation/GStreamer) behind common interfaces.
3. Add benchmark suite for decode/render latency and CPU/GPU utilization.

## Current Status
- Phase 1 complete.
- Solution builds successfully on .NET 9.
