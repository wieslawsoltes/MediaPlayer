# Audio Input/Output and Playback Parity Plan (2026-03-01)

## Goal
Design and deliver full audio I/O and playback parity with Apple QuickTime Player behavior across all supported platforms and backend modes:
- FFmpeg backend
- LibVLC backend
- macOS native interop and native bindings backends
- Windows native interop and native bindings backends
- Linux parity via LibVLC and FFmpeg fallback paths

Non-negotiables:
- no-airspace Avalonia rendering remains unchanged
- equal user-facing feature set across backends (with capability diagnostics + fallback)
- hardware acceleration used when available, deterministic CPU fallback when not

## QuickTime Audio Capability Baseline
Reference scope to match:
- audio file playback (AAC/ALAC/MP3/WAV/AIFF and common container audio tracks)
- video audio playback with soundtrack track selection
- mute/volume controls and keyboard shortcuts
- smooth realtime scrub/seek with immediate audio timeline response
- playback speed changes while preserving UX continuity
- audio-only export and basic media editing workflows (trim/split/combine)
- microphone recording and movie recording with audio capture
- menu-level controls for playback, tracks, and recording/export operations

Apple QuickTime documentation references already used in repo plans:
- https://support.apple.com/en-il/guide/quicktime-player/welcome/mac
- https://support.apple.com/en-il/guide/quicktime-player/qtpaf278f3ce/10.5/mac/15.0
- https://support.apple.com/en-il/guide/quicktime-player/qtp0f47f6d22/mac
- https://support.apple.com/en-il/guide/quicktime-player/qtp356b55534/mac

## Current Baseline in This Repo

### Playback contract (`IMediaBackend`)
Current audio surface:
- `SetVolume`, `SetMuted`
- `GetAudioTracks`, `SetAudioTrack`
- playback rate and seek

Missing for parity:
- input/output device enumeration and selection
- output route changes during playback
- capture source selection for recording workflows
- audio level telemetry (peak/RMS) and latency diagnostics
- explicit capability model per backend for audio features

### Backend status (today)
- `LibVlcMediaBackend`: best existing parity for runtime audio control (volume/mute/tracks)
- `FfmpegMediaBackend`: audio playback uses ffplay process; volume/mute not implemented in backend API
- `NativeFramePumpMediaBackend` (macOS/Windows helper backends): volume/mute currently not implemented
- interop workflow provider (`WavInteropMediaWorkflowProvider`): currently limited to PCM WAV operations

### Workflow service status (`IMediaWorkflowService`)
- supports trim/split/combine/remove-audio/remove-video/export/record
- record uses default devices only (no explicit input/output device routing)
- no shared contract for capture device selection or audio format preferences

## Gap Matrix

| Capability | LibVLC | FFmpeg backend | macOS native helper | Windows native helper | Interop workflow providers |
|---|---|---|---|---|---|
| Audio file playback | Partial | Partial | Partial | Partial | N/A |
| Video audio playback | Yes | Yes | Yes | Yes | N/A |
| Volume/mute | Yes | No | No | No | N/A |
| Audio track selection | Yes | Partial | No | No | N/A |
| Output device selection | Partial (backend-specific) | No | No | No | No |
| Input device selection | N/A | No | No | No | No |
| Realtime seek audio continuity | Partial | Partial | Partial | Partial | N/A |
| Audio recording device routing | No | No | No | No | No |
| Audio levels/latency diagnostics | No | No | No | No | No |
| Equal feature contract | No | No | No | No | No |

## Target Architecture

### 1) Add audio-specific abstractions (interface segregation)
Create dedicated contracts instead of bloating `IMediaBackend`:
- `IMediaAudioPlaybackController`
- `IMediaAudioDeviceController`
- `IMediaAudioMetricsProvider`
- `IMediaAudioCapabilityProvider`

Backends implement only supported interfaces. `GpuMediaPlayer` composes optional services and exposes unified direct properties/commands.

### 2) Add shared audio domain models (`MediaPlayer.Controls/Audio`)
- `MediaAudioDeviceInfo` (id, name, direction, isDefault, isAvailable, backendTag)
- `MediaAudioFormat` (sampleRate, channels, sampleFormat, channelLayout)
- `MediaAudioRouteState` (selectedInputId, selectedOutputId, loopbackEnabled)
- `MediaAudioLevels` (timestamp, peakL, peakR, rmsL, rmsR)
- `MediaAudioCapabilities` (bit flags)

### 3) Extend workflow contract with explicit options objects
Keep existing methods for compatibility; add options-based overloads:
- `MediaRecordingOptions` (input device, system loopback, target format, AEC/noise suppression flags)
- `MediaExportOptions` (audio codec/bitrate/sample rate/channels/loudness normalization)
- `MediaAudioEditOptions` (preserve metadata, stream copy preference)

### 4) Capability-driven parity layer
Introduce `MediaAudioParityService` in controls library:
- normalizes backend feature availability
- applies fallback policy per operation (native -> interop -> ffmpeg -> vlc where relevant)
- surfaces deterministic diagnostics for UI and tests

### 5) Demo integration and reusable plumbing
Move audio-routing, device menus, and parity logic into reusable controls-library services so demo app only binds commands/viewmodels.

## Backend Design by Provider

### LibVLC backend
- add explicit output-device enumeration/selection through LibVLC APIs where available
- map LibVLC audio callbacks to `MediaAudioLevels`
- ensure track/volume/mute/device changes emit unified state notifications

### FFmpeg backend
- replace current "no volume/mute" limitation with controlled audio path:
  - option A (preferred): managed/native audio sink host with ffmpeg decode output PCM
  - option B: interop wrapper around libav* + platform sink (WASAPI/CoreAudio/PipeWire)
- support output device routing and low-latency seek restart without slider lag
- keep current ffmpeg process fallback path when advanced sink host is unavailable

### macOS native path (interop + native bindings)
- playback:
  - AVFoundation + AVAudioEngine/CoreAudio routing
  - device enumeration and output route switching
  - input capture device selection for recording workflows
- workflows:
  - AVAssetExportSession/AVAudioEngine-based recording and audio export options
  - preserve ffmpeg fallback for unsupported codecs/container cases

### Windows native path (interop + native bindings)
- playback:
  - Media Foundation decode + WASAPI render (AudioClient3 low-latency mode where possible)
  - endpoint enumeration via MMDevice API
- workflows:
  - Media Foundation capture (camera/mic) + WASAPI loopback for system audio
  - fallback to ffmpeg when device graph/build fails

### Linux parity path
- primary: LibVLC (PulseAudio/PipeWire output modules)
- fallback: FFmpeg path with Pulse/PipeWire sink integration
- keep feature contract identical; report capability degradation explicitly

## Hardware Acceleration Strategy
- video decode remains platform-native where available (VideoToolbox, D3D11VA, VAAPI)
- audio path uses native hardware render/capture APIs (CoreAudio/WASAPI/PipeWire/Pulse)
- for codecs with hardware decode paths exposed by native stacks, prefer native decode
- if hardware path unavailable/fails, auto-fallback to software decode with same UX/API

## Delivery Phases

### Phase A - Contracts and diagnostics [Done]
1. [x] Add audio capability and device interfaces/models.
2. [x] Add `MediaAudioCapabilities` + per-backend diagnostics in `MovieInspector`/status output.
3. [x] Add compatibility shims so existing API remains source-compatible.

### Phase B - Playback parity foundation
1. [x] Implement volume/mute parity on FFmpeg and native helper backends.
2. [x] Add unified audio track state updates across all playback backends.
3. [x] Add slider drag seek policy tuned for continuous audio feedback (debounced realtime seek + final commit).

### Phase C - Device routing parity
1. [x] Implement input/output device enumeration service per backend.
2. Add runtime output device switching during playback.
3. Add recording input device selection and persistence.

### Phase D - Workflow parity for audio operations
1. [x] Add options-based recording/export methods with device and format selection.
2. Upgrade interop workflow provider coverage beyond PCM WAV (M4A/MP3/WAV where native APIs allow).
3. Ensure ffmpeg fallback always preserves operation availability.

### Phase E - Native interop/bindings completion
1. Implement standalone macOS and Windows providers for audio path (no runtime helper compile).
2. Implement native bindings mode parity for playback + workflows.
3. Keep provider mode toggles and auto-fallback diagnostics.

### Phase F - UX parity and menu integration
1. Add reusable audio device and recording option menus (macOS native menu + cross-platform menu parity).
2. Add audio meters and route indicators in reusable control templates.
3. Ensure QuickTime-like command/shortcut parity for audio operations.

### Phase G - Validation and performance
1. Add backend parity tests for each audio capability.
2. Add integration tests for seek latency during slider drag and pause/play startup delay.
3. Add perf benchmarks: first-audio latency, seek p95, glitch/drop counters.

## Test Matrix (Required)
- Unit tests:
  - capability negotiation and fallback decisions
  - device selection state transitions
  - option-object validation and defaults
- Integration tests:
  - playback + output route switch for each backend mode
  - audio recording using selected input device
  - audio export/edit parity across providers
- Performance tests:
  - seek drag latency p95 <= 40 ms local media
  - pause->play audio start latency p95 <= 120 ms local media
  - no unbounded allocations in audio render loop

## Acceptance Criteria
1. Same audio feature set exposed in UI/API regardless of selected backend mode.
2. Audio files and video audio playback support parity across platforms.
3. Volume/mute/track switching/device routing all functional on macOS, Windows, Linux (with documented fallback path).
4. Recording/export workflows accept explicit audio device + format options.
5. Native interop and native bindings paths are preferred on macOS/Windows; FFmpeg/LibVLC fallback remains available.
6. `dotnet build MediaPlayer.sln -warnaserror` and full test suite pass.

## Risks and Mitigations
- Risk: backend API mismatch across LibVLC/FFmpeg/native stacks.
  - Mitigation: capability-first contracts + fallback orchestration.
- Risk: low-latency seek behavior differs by backend.
  - Mitigation: backend-specific seek scheduler with shared UX thresholds.
- Risk: device enumeration edge cases (hot-plug, permission denial).
  - Mitigation: refreshable device catalog + explicit error diagnostics and retry paths.

## Immediate Next Implementation Slice
1. Start interop workflow provider expansion beyond PCM WAV (Phase D.2 bootstrap).
2. Implement runtime output device switching in first backend slice (Phase C.2 bootstrap).
3. Add pause/play startup-latency integration coverage (Phase G.2 continuation).

## Implementation Log
- 2026-03-01: Implemented Phase A contract layer in `MediaPlayer.Controls/Audio` with capability/device/metrics abstractions.
- 2026-03-01: Added backend capability reporting for `LibVlcMediaBackend`, `FfmpegMediaBackend`, `NativeFramePumpMediaBackend`, and `NullMediaBackend`.
- 2026-03-01: Exposed audio diagnostics via `GpuMediaPlayer` and integrated capability/route/device summaries into demo status and Movie Inspector.
- 2026-03-01: Implemented Phase B.1 volume/mute parity for FFmpeg and native helper playback backends, passed volume/mute through macOS/Windows helper process contracts, and added runtime backend capability table diagnostics in `GpuMediaPlayer` + Movie Inspector.
- 2026-03-01: Implemented Phase B.2 unified audio track state updates by exposing stable default audio track state on native helper backends, updating capability table expectations, and adding backend tests for default track behavior and capability flags.
- 2026-03-01: Implemented Phase B.3 by introducing reusable `TimelineSeekController` debounced drag-seek policy in controls library, wiring demo timeline interaction to it, and adding deterministic unit tests for realtime seek flush + final commit behavior.
- 2026-03-01: Implemented Phase D.1 by adding options-based `ExportAsync`/`RecordAsync` overloads (`MediaExportOptions`, `MediaRecordingOptions`) with default compatibility shims, threading options through FFmpeg/native/interop workflow services, and adding tests for options forwarding and applied FFmpeg audio parameters.
- 2026-03-01: Implemented Phase C.1 bootstrap by adding default input/output device enumeration and route-state contracts to FFmpeg/LibVLC/native-helper backends (`IMediaAudioDeviceController`), updating capability diagnostics to include enumeration flags, and adding backend tests for route defaults and device-id validation.
- 2026-03-01: Implemented Phase G.2 bootstrap seek-latency harness by adding integration-style drag-seek latency tests (`TimelineSeekLatencyIntegrationTests`) with p95 budget assertions for fast/slow seek modes and pending-flush latency validation.
