namespace MediaPlayer.Native.Abstractions;

public enum MediaPlayerNativeProviderKind
{
    Unknown = 0,
    LegacyHelper = 1,
    Interop = 2,
    NativeBindings = 3,
    FfmpegFallback = 4,
    LibVlcFallback = 5
}
