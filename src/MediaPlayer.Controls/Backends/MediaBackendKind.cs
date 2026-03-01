namespace MediaPlayer.Controls.Backends;

internal enum MediaBackendKind
{
    Unknown = 0,
    MacOsNativeHelper = 1,
    WindowsNativeHelper = 2,
    MacOsFfmpegProfile = 3,
    WindowsFfmpegProfile = 4,
    LibVlcInterop = 5,
    FfmpegFallback = 6
}
