# QuickTime Feature Bridge Plan (2026-03-01)

## Goal
Close remaining feature gaps versus Apple QuickTime Player while preserving:
- no-airspace rendering in Avalonia
- equal feature set exposed across all playback backends
- full hardware acceleration where available, with deterministic fallback paths

## Apple QuickTime Feature Sources (official)
- QuickTime Player User Guide landing:
  - https://support.apple.com/en-il/guide/quicktime-player/welcome/mac
- Open/play controls and playback speed:
  - https://support.apple.com/en-il/guide/quicktime-player/qtpaf278f3ce/10.5/mac/15.0
- Keyboard shortcuts:
  - https://support.apple.com/en-il/guide/quicktime-player/qtp356b55534/mac
- Movie size/viewer modes (Actual Size / Fit / Fill / Panoramic):
  - https://support.apple.com/en-il/guide/quicktime-player/qtp8d0b98c80/mac
- Subtitles/captions:
  - https://support.apple.com/en-il/guide/quicktime-player/qtp9e7ee068d/mac
- Soundtrack options:
  - https://support.apple.com/en-il/guide/quicktime-player/qtp0f47f6d22/mac

## Current App Status (as of 2026-03-01)
Implemented:
- native menu integration (macOS), HUD auto-hide, fullscreen/window controls
- playback speed controls, frame stepping, timecode/frame views, go-to time/frame
- audio/subtitle track menu integration with backend abstraction
- trim/split/combine/remove audio/remove video workflows
- export presets and recording workflows
- native workflow service layer (macOS/Windows) with ffmpeg fallback
- interactive clip arrangement before combine
- rotate/flip clip editing workflows via shared workflow service API

Still missing or partial versus QuickTime:
- richer clip insertion workflows (append/insert clip into current timeline composition)
- per-platform quality profile selection UX for export/record workflows
- richer share targets (copy path/link, reveal in finder/explorer, app handoff matrix)
- drag-and-drop clip reordering gesture support in arranger UI
- parity verification automation matrix across backends for each workflow operation

## Parity Strategy (All Backends)
1. Keep one public workflow contract (`IMediaWorkflowService`) for editing/export/capture features.
2. Keep one public playback contract (`IMediaBackend`) for runtime playback controls.
3. Prefer native implementation on platform when stable and GPU-friendly.
4. Fallback to ffmpeg path when native path is unavailable/unsupported.
5. Never remove a feature from one backend unless removed from all; fallback required.
6. Add regression tests for service registration and command construction paths.

## Delivery Phases

### Phase A - Editing Parity Completion [Done]
1. [x] Interactive clip arrangement prior to combine.
2. [x] Rotate/flip transformations exposed via workflow contract and demo menu.
3. [x] Add clip insertion workflow (append/insert selected clip(s) into composition order).
4. [x] Add drag-and-drop reorder gestures in arranger UI (keep move-up/down as keyboard fallback).

### Phase B - Capture/Export Parity [In progress]
1. [x] Add explicit quality profile model (Speed/Balanced/Quality) independent of backend.
2. [x] Map profile model to backend-native paths (macOS native, Windows native, ffmpeg fallback).
3. [ ] Add richer share actions (copy file path/URI, reveal in folder, open-with picker where supported).

### Phase C - Backend Equality & Validation [Pending]
1. [ ] Add backend parity smoke tests for workflow API availability and fallback behavior.
2. [ ] Add backend diagnostics panel entries for active workflow path (native vs fallback).
3. [ ] Add documentation matrix per feature: native accelerated / fallback / unavailable.

## Implementation Log
- 2026-03-01: Implemented Phase A.2 (`Rotate/Flip`) in workflow API and demo integration.
- 2026-03-01: Implemented Phase A.3 (`Append/Insert Clips`) in clip arranger workflow, with command-level tests.
- 2026-03-01: Implemented Phase A.4 (`Drag-and-drop Reorder`) using MVVM command-driven clip reorder gestures with keyboard fallback retained.
- 2026-03-01: Implemented Phase B.1 explicit workflow quality profile model (`Speed/Balanced/Quality`) across workflow contract, demo menu selection, and ffmpeg encoding paths.
- 2026-03-01: Implemented Phase B.2 native workflow quality mapping (`Speed/Balanced/Quality`) on macOS (`avconvert` flags/presets) and Windows native helper argument surface, with ffmpeg fallback retained.

## Acceptance Criteria
1. `dotnet build MediaPlayer.sln -warnaserror` passes.
2. `dotnet test MediaPlayer.sln` passes.
3. Every new editing/export feature is callable via `IMediaWorkflowService`.
4. On unsupported native path, operation transparently falls back to ffmpeg path.
5. No-airspace rendering path remains unchanged for playback.
