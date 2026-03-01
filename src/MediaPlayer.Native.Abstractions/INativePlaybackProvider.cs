namespace MediaPlayer.Native.Abstractions;

public interface INativePlaybackProvider
{
    MediaPlayerNativeProviderKind ProviderKind { get; }
}
