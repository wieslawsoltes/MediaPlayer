using MediaPlayer.Native.Abstractions;

namespace MediaPlayer.Controls.Backends;

internal readonly record struct MediaBackendCandidate(
    string Name,
    MediaPlayerNativeProviderKind ProviderKind,
    MediaBackendKind BackendKind);
