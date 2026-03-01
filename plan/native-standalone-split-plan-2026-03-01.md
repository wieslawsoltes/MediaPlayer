# Standalone Native Split Plan (2026-03-01)

## Goal
Split current platform-native functionality into standalone, production-grade paths with both options available:
- Option A: pure modern .NET interop (no Swift helper process, no runtime helper build/publish).
- Option B: native platform libraries with managed high-performance bindings.

Both options must remain no-airspace compatible and keep identical user-facing features across backends.

## Why this plan
Current native paths are helper-process centric:
- macOS playback backend compiles/runs Swift helper at runtime.
- Windows playback backend builds/runs helper .NET process for frame pumping.
- Workflow native paths rely on shell tools (`avconvert`, `screencapture`) and external helper contract on Windows.

This is functional but not ideal for standalone deployment, latency, deterministic startup, or packaging clarity.

## Target architecture

### 1) Native provider model
Introduce provider abstractions and split implementations:
- `INativePlaybackProvider`
- `INativeWorkflowProvider`

Add provider selection config:
- `NativeProviderMode.InteropOnly`
- `NativeProviderMode.NativeBindingsOnly`
- `NativeProviderMode.AutoPreferInterop`
- `NativeProviderMode.AutoPreferBindings`

Selection must be per-platform and overridable via:
- DI options (`MediaPlayerNativeOptions`)
- environment variable (`MEDIAPLAYER_NATIVE_PROVIDER_MODE`)
- optional app-level setting in demo

### 2) Option A: .NET 10 interop path (no Swift/helper processes)
Build a direct interop layer using modern source-generated interop:
- `LibraryImport` (and .NET 10 interop improvements where applicable).
- `UnmanagedCallersOnly` callbacks for frame/data events.
- explicit blittable structs and safe-handle wrappers.

Platform scope:
- macOS:
  - CoreFoundation/CoreMedia/VideoToolbox/AVFoundation via direct interop.
  - Remove runtime Swift compilation dependency.
- Windows:
  - Media Foundation + D3D11/DirectX interop via direct bindings.
  - Remove helper publish/run dependency for playback/workflows.

### 3) Option B: native libraries + managed bindings
Build native C ABI bridge libraries:
- `libmediaplayer_native_mac` (Objective-C++/C bridge over AVFoundation/VideoToolbox/AVAssetExportSession)
- `mediaplayer_native_win` (C++/WinRT or C++ bridge over Media Foundation/D3D11)

Expose minimal C API:
- session lifecycle
- open/play/pause/seek/rate/loop
- track query/select
- frame callback with zero-copy or pooled-copy contract
- workflow operations: trim/split/combine/export/record

Managed side:
- source-generated P/Invoke bindings (`LibraryImport`)
- `SafeHandle` ownership model
- pooled buffers + lock-free frame handoff

## Repository split

### New projects
- `src/MediaPlayer.Native.Interop` (managed interop-only provider)
- `src/MediaPlayer.Native.Bindings` (managed binding layer for native libs)
- `src/MediaPlayer.Native.Abstractions` (provider contracts/options)
- `src/MediaPlayer.Native.Diagnostics` (capability/fallback telemetry)

### Native code roots
- `native/macos/` (Xcode/CMake output for C ABI dylib)
- `native/windows/` (MSBuild/CMake output for C ABI dll)

### Existing projects impact
- `MediaPlayer.Controls`
  - keep `IMediaBackend`/workflow contracts stable
  - replace helper-process internals with provider adapters
- `MediaPlayer.Demo`
  - expose provider mode selector + diagnostics

## Feature parity rules
All provider modes must support the same feature set:
- playback: open/play/pause/stop/seek/rate/loop
- tracks: audio/subtitle enumeration + selection
- timeline/frame metadata
- workflows: trim/split/combine/remove audio/remove video/transform/export/record

If a feature is unavailable in a concrete provider:
- fail fast with typed capability diagnostics
- auto-fallback to other configured provider mode
- preserve behavior parity at app level

## Performance requirements
- no per-frame allocations in hot render path
- zero-copy where API allows; otherwise pooled-copy with bounded latency
- seek responsiveness:
  - drag preview <= 40 ms target p95 for local media
- startup:
  - no runtime helper compilation/publish
- background decode thread must never block UI thread

## Security and deployment
- no runtime code generation/compilation for native playback
- signed native binaries for release packaging
- deterministic RID-specific artifact layout
- explicit dependency manifest and version pinning

## Test and validation matrix

### Unit tests
- provider selection and fallback policy
- capability resolution
- error mapping and disposal safety

### Integration tests
- backend parity tests per provider mode
- workflow parity tests per provider mode
- seek/play/pause/rate regression suite

### Performance tests
- frame upload throughput
- seek latency p50/p95
- startup and first-frame time

### Platform CI
- macOS: InteropOnly + NativeBindingsOnly + Auto modes
- Windows: InteropOnly + NativeBindingsOnly + Auto modes
- Linux: unchanged fallback path coverage (FFmpeg/LibVLC), no regression

## Phased delivery

### Phase 0 - Contract + scaffolding
1. Add `MediaPlayer.Native.Abstractions` provider contracts/options.
2. Introduce provider mode selection in DI/composition root.
3. Add diagnostics surface for active provider + fallback reason.
Status (2026-03-01): Implemented in `MediaPlayer.Native.Abstractions`, `MediaPlayer.Controls`, `MediaPlayer.Demo`, and provider-mode diagnostics tests.

### Phase 1 - InteropOnly playback path
1. Implement macOS direct interop playback provider.
2. Implement Windows direct interop playback provider.
3. Wire to existing `GpuMediaPlayer` adapters and parity tests.
Status (2026-03-01): In progress. Added `MediaPlayer.Native.Interop` project with interop provider catalog/capability diagnostics, extracted backend-selection policy for deterministic interop/legacy/fallback ordering with parity tests, and wired `GpuMediaPlayer` to prefer interop providers for `InteropOnly`/`AutoPreferInterop` with typed fallback reasons. Direct AVFoundation/MediaFoundation interop providers remain next.

### Phase 2 - InteropOnly workflow path
1. Implement native workflow interop services (export/record/edit).
2. Remove shell-tool hard dependency from primary path.
3. Keep ffmpeg fallback.
Status (2026-03-01): In progress. Added catalog-driven interop workflow provider routing, implemented `WavInteropMediaWorkflowProvider` (direct no-shell managed interop for PCM WAV trim/split/combine/audio-export), reworked `InteropMediaWorkflowService` into provider-orchestrated execution with explicit FFmpeg fallback, and added parity tests for capability downgrade and fallback behavior.

### Phase 3 - NativeBindings path
1. Implement C ABI native bridge libraries for macOS/Windows.
2. Implement managed bindings project with generated interop.
3. Add mode switch + fallback between InteropOnly and NativeBindingsOnly.

### Phase 4 - Hardening and packaging
1. Performance tuning and memory pressure validation.
2. Signing/notarization (macOS) and Windows binary packaging.
3. Final parity matrix and docs.

## Acceptance criteria
1. Both provider options compile and run independently on target platform.
2. No runtime Swift compilation/helper process is required in InteropOnly mode.
3. NativeBindings mode works with packaged native binaries and managed bindings.
4. Feature parity tests pass across provider modes.
5. `dotnet build MediaPlayer.sln -warnaserror` and full tests pass.
6. Existing no-airspace rendering behavior is unchanged.

## Risks and mitigations
- Risk: complex AVFoundation/MediaFoundation interop edge cases.
  - Mitigation: start from minimal stable playback path + progressive capability flags.
- Risk: binary packaging complexity for native libs.
  - Mitigation: separate native artifact pipeline with RID verification.
- Risk: divergence between provider behaviors.
  - Mitigation: shared parity test suite and capability contract.

## Immediate next implementation phase
Continue **Phase 2**:
1. add host-native direct workflow interop providers for macOS/Windows operations beyond PCM WAV (video export/trim/record),
2. move additional operations from shell helper paths to provider-backed no-shell implementations while preserving parity contracts,
3. extend capability diagnostics to report active interop workflow provider per operation in demo status/inspector surfaces.
