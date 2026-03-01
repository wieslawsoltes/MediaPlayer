using MediaPlayer.Native.Abstractions;

namespace MediaPlayer.Native.Interop;

public readonly record struct MediaPlayerInteropPlaybackProviderDescriptor(
    MediaPlayerInteropPlaybackProviderId Id,
    string Name,
    MediaPlayerNativeProviderKind ProviderKind,
    bool IsAvailable,
    string UnavailableReason);
