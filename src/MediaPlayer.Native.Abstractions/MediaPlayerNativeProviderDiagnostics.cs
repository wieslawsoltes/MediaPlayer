namespace MediaPlayer.Native.Abstractions;

public readonly record struct MediaPlayerNativeProviderDiagnostics(
    MediaPlayerNativeProviderMode ConfiguredMode,
    MediaPlayerNativeProviderKind ActiveProvider,
    string FallbackReason);
