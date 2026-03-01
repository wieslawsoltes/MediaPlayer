using MediaPlayer.Native.Abstractions;

namespace MediaPlayer.Native.Interop;

public readonly record struct MediaPlayerInteropPlaybackCapabilityDiagnostics(
    MediaPlayerNativeProviderMode ConfiguredMode,
    MediaPlayerInteropPlaybackProviderId ProviderId,
    bool IsAvailable,
    string Message);
