using MediaPlayer.Native.Abstractions;

namespace MediaPlayer.Native.Interop;

public readonly record struct MediaPlayerInteropWorkflowProviderDescriptor(
    MediaPlayerInteropWorkflowProviderId Id,
    string Name,
    MediaPlayerNativeProviderKind ProviderKind,
    bool IsAvailable,
    string UnavailableReason);
