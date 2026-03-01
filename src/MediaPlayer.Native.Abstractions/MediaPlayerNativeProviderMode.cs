namespace MediaPlayer.Native.Abstractions;

public enum MediaPlayerNativeProviderMode
{
    LegacyHelpers = 0,
    InteropOnly = 1,
    NativeBindingsOnly = 2,
    AutoPreferInterop = 3,
    AutoPreferBindings = 4
}
