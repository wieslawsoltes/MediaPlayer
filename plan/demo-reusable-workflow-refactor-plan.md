# Demo-to-Shipable Refactor Plan (2026-03-01)

## Objective
Move reusable media workflow plumbing out of `MediaPlayer.Demo` and into a shippable surface in `MediaPlayer.Controls`, so integrators can consume advanced edit/export/record capabilities without copying demo code.

## Scope
- In scope:
  - shared workflow contracts + presets
  - ffmpeg command orchestration and fallback logic
  - reusable output naming/path helper logic
  - demo migration to shared workflow API
- Out of scope:
  - demo-only UI composition (native menu structure, dialogs, status wording)
  - backend-specific decode/render internals

## Current State (Before Refactor)
`MainWindow.axaml.cs` previously contained both UI and reusable media workflow logic:
- UI orchestration: picker dialogs, menu events, HUD status.
- Reusable workflows: trim/split/combine/remove audio/remove video/export/record.
- Process execution internals: ffmpeg process setup, arguments, error handling, cleanup.

This tightly coupled demo and reusable behavior.

## Target Design
### Reusable package (`MediaPlayer.Controls`)
Namespace: `MediaPlayer.Controls.Workflows`

- `MediaExportPreset`
- `MediaRecordingPreset`
- `MediaWorkflowResult`
- `IMediaWorkflowService`
- `FfmpegMediaWorkflowService`

### Demo app (`MediaPlayer.Demo`)
- Keeps UI and interaction concerns.
- Delegates workflow execution to `IMediaWorkflowService`.
- Uses service for:
  - preset labels
  - suggested output names
  - operation execution (trim/split/combine/remove/export/record)
  - sibling path generation for split operations

## Migration Plan
1. Extract workflow API contracts into controls.
2. Move ffmpeg command and fallback implementation into controls service.
3. Replace demo workflow calls with service calls.
4. Delete duplicated process helper methods from demo.
5. Validate build and solution tests.
6. Document resulting ownership split and next phases.

## Implementation Status
- [x] Added reusable workflow contracts and presets in `MediaPlayer.Controls.Workflows`.
- [x] Added `FfmpegMediaWorkflowService` with reusable ffmpeg orchestration and cleanup.
- [x] Updated `MainWindow.axaml.cs` to use `IMediaWorkflowService`.
- [x] Removed duplicated ffmpeg workflow methods from demo code-behind.
- [x] Added DI registration extension (`AddMediaPlayerWorkflows`) and wired demo `App` composition root.
- [x] Extracted macOS native menu composition/state sync into `MainWindowNativeMenuCoordinator`.
- [x] Extracted keyboard shortcut routing into `PlaybackShortcutCommandService`.
- [x] `dotnet build MediaPlayer.sln` passes.
- [x] `dotnet test MediaPlayer.sln --no-build` passes.

## API Mapping (Demo -> Controls)
- `TrimMediaSegmentAsync` -> `IMediaWorkflowService.TrimAsync`
- split two-pass trim logic -> `IMediaWorkflowService.SplitAsync`
- `CombineMediaSegmentsAsync` -> `IMediaWorkflowService.CombineAsync`
- `RemoveAudioFromMediaAsync` -> `IMediaWorkflowService.RemoveAudioAsync`
- `RemoveVideoFromMediaAsync` -> `IMediaWorkflowService.RemoveVideoAsync`
- `ExportMediaAsync` -> `IMediaWorkflowService.ExportAsync`
- `RecordMediaAsync` -> `IMediaWorkflowService.RecordAsync`
- `BuildSiblingOutputPath` -> `IMediaWorkflowService.BuildSiblingOutputPath`
- local preset title/filename switches -> service display + suggestion APIs

## Remaining Gaps / Next Refactor Phases
1. [x] Add DI extension methods in controls (`AddMediaPlayerWorkflows`) and wire app composition root.
2. [x] Extract demo-native menu behavior into a dedicated presenter/coordinator class.
3. [x] Extract playback shortcut handling into reusable command service.
4. [x] Add unit tests for `FfmpegMediaWorkflowService` command setup + path generation + error cleanup.
5. [x] Add optional native backend workflow service implementations (macOS/Windows capture/export paths) behind `IMediaWorkflowService`.
