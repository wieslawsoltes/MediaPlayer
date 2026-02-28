namespace MediaPlayer.Controls.Backends;

internal sealed record LibVlcPlatformProfile(
    string Name,
    string NativeDecodeApi,
    string NativeRenderPipeline,
    string[] LibVlcOptions);
