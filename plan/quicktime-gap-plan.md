# QuickTime Gap Plan (2026-03-01)

## Sources Used (Apple)
- QuickTime Player User Guide landing: https://support.apple.com/en-il/guide/quicktime-player/welcome/mac
- Open and play files (includes play speed controls and live stream): https://support.apple.com/en-il/guide/quicktime-player/qtpaf278f3ce/10.5/mac/15.0
- QuickTime keyboard shortcuts: https://support.apple.com/en-il/guide/quicktime-player/qtp356b55534/mac
- QuickTime User Guide table of contents (editing/export/subtitles/soundtracks/recording): https://support.apple.com/en-il/guide/quicktime-player/apdf8f0c5f84/mac
- Change movie screen size and viewer mode (Actual Size/Fit/Fill/Panoramic): https://support.apple.com/en-il/guide/quicktime-player/qtp8d0b98c80/mac
- Show subtitles or captions in movies: https://support.apple.com/en-il/guide/quicktime-player/qtp9e7ee068d/mac
- View and change soundtrack options: https://support.apple.com/en-il/guide/quicktime-player/qtp0f47f6d22/mac

## Scope
Bridge the highest-impact QuickTime-like playback and control gaps first, while preserving:
- no-airspace rendering
- backend feature parity (same exposed feature set on every backend)
- hardware acceleration where the platform/backend supports it

## Current Feature Matrix
- Implemented in this app (before this plan):
  - Open file/location, play/pause/stop, seek, loop, fullscreen
  - QuickTime-like HUD + auto-hide controls
  - macOS native menu integration
  - native-first backend selection on macOS/Windows with fallbacks
- Missing or partial vs QuickTime:
  - Subtitle/caption and soundtrack track selection
  - Native track switching implementation parity on non-LibVLC backends
  - Editing workflows (trim, split, rearrange, remove audio/video)
  - Recording workflows (screen/movie/audio)
  - Export/share presets and options

## Delivery Phases

### Phase 1 (implement now)
1. Backend parity contract [Done]
   - Add backend-level playback-rate support to all backends.
   - Add backend frame-rate metadata where available.
2. Playback UX parity [Done]
   - HUD speed control, keyboard speed controls, and native menu speed options.
3. Time and frame workflows [Done]
   - Time display modes: remaining, elapsed, timecode, frame count.
   - Go To Time and Go To Frame actions.
   - Frame-step left/right when paused.
4. Window behavior [Done]
   - Float-on-top toggle.
5. File workflow [Done]
   - Open Recent list in native menu.

### Phase 2 (next)
1. Track controls parity [In progress]
   - Common backend contract added for audio/subtitle track listing and switching.
   - Demo native menu now exposes Audio Track and Subtitles submenus.
   - Full runtime switching implemented on LibVLC backend.
   - Full runtime soundtrack/subtitle switching now implemented on FFmpeg backend (ffprobe enumeration, audio stream mapping, subtitle burn-in for local files).
   - Native frame-pump and ffmpeg backends expose equal API surface and gracefully report unavailable switching.
   - Subtitle/caption track listing and switching.
   - Audio/soundtrack track listing and switching.
2. Inspector parity [Done in this turn]
   - Movie inspector dialog with media metadata and backend diagnostics.
3. Video presentation parity [Done in this turn]
   - Added QuickTime-style `Actual Size`, `Fit to Screen`, `Fill`, and `Panoramic` menu commands.
   - Added QuickTime keyboard shortcuts (`Cmd+1`, `Cmd+3`, `Cmd+4`, `Cmd+5`) on macOS and control-key equivalents on non-macOS.
   - Implemented backend-independent renderer layout modes (`Fit`, `Fill`, `Panoramic`) in `GpuMediaPlayer`.

### Phase 3 (advanced)
1. Editing parity [In progress]
   - Implemented first slice: `Trim…` workflow in demo (`Edit > Trim…`, `Cmd+T`), with time range input, save picker, and ffmpeg copy/transcode fallback.
   - Implemented second slice in demo native menu:
     - `Split Clip…` (`Edit > Split Clip…`, `Cmd+Y`) creates `-part1` and `-part2` clips around a split point.
     - `Combine Clips…` concatenates multiple local clips in selected order using ffmpeg concat (copy-first, transcode fallback).
     - `Remove Audio…` exports a video-only version.
     - `Remove Video…` exports an audio-only version.
   - Implemented third slice in demo:
     - Added interactive `Arrange Clips` dialog before combine, with visual ordered clip list, move up/down controls, remove, and explicit confirm/cancel flow.
     - `Combine Clips…` now uses user-confirmed interactive order instead of direct picker order.
   - Implemented fourth slice in workflow + menu:
     - Added `Rotate Clockwise`, `Rotate Counterclockwise`, `Flip Horizontal`, and `Flip Vertical` editing actions.
     - Added shared workflow contract operation for video transforms, with FFmpeg implementation and native-service fallback integration.
   - Implemented fifth slice in arranger workflow:
     - Added `Insert Before…` and `Append Clips…` actions in `Arrange Clips` for composition insertion without restarting combine flow.
   - Implemented sixth slice in arranger workflow:
     - Added drag-and-drop clip reordering gestures, while keeping Move Up/Down controls as keyboard/fallback behavior.
   - Remaining editing gaps:
     - None in current phase scope.
2. Capture/export parity [In progress]
   - Implemented quality profile model:
     - Added backend-independent workflow quality profiles (`Speed`, `Balanced`, `Quality`) for export/record operations.
     - Added File-menu workflow quality selection with live status updates and profile-aware export/record execution.
     - Mapped profile selection through native workflow paths:
       - macOS native exports now apply profile-specific `avconvert` flags (`--multiPass` for quality, `--disableFastStart` for speed) while preserving preset target.
       - macOS native screen recording conversion now applies profile-specific conversion presets/flags.
       - Windows native helper workflow now receives explicit `--qualityProfile` so helper implementations can apply native profile tuning; ffmpeg fallback remains automatic on helper failure.
   - Implemented first slice in demo native menu:
     - `File > Export As > 4K/1080p/720p/480p/Audio Only` with ffmpeg-backed export workflows.
     - `File > Share Current Media` opening the current source in the system-default handler for quick sharing.
   - Implemented second slice in demo native menu:
     - `File > New Recording > New Screen Recording…`
     - `File > New Recording > New Movie Recording…`
     - `File > New Recording > New Audio Recording…`
     - Platform-specific ffmpeg capture attempts with duration prompt and save picker flow.
   - Remaining capture/export gaps:
     - Add richer share targets.

## Backend Parity Requirements
- Every backend must implement the same public control contract for:
  - playback rate
  - seeking
  - frame stepping support (using frame-rate metadata or fallback)
  - time display source values
- If a backend cannot accelerate a path, it must:
  - expose the same feature behavior
  - degrade to CPU fallback while preserving no-airspace rendering

## Acceptance Criteria for Phase 1
1. `dotnet build MediaPlayer.sln` passes.
2. Playback speed works on all backends through one common API.
3. Time display modes update correctly and are user-switchable.
4. Go To Time/Frame works and clamps to valid duration.
5. Paused frame stepping works (left/right keys).
6. Float-on-top toggle works.
7. Open Recent menu is populated and functional on macOS native menu.
