namespace MediaPlayer.Native.Abstractions;

public interface INativeWorkflowProvider
{
    MediaPlayerNativeProviderKind ProviderKind { get; }
}
