namespace MediaPlayer.Demo.ViewModels;

public sealed class MovieInspectorViewModel
{
    public required string MediaName { get; init; }

    public required string MediaLocation { get; init; }

    public required string MediaType { get; init; }

    public required string FileSize { get; init; }

    public required string Resolution { get; init; }

    public required string AspectRatio { get; init; }

    public required string Duration { get; init; }

    public required string CurrentPosition { get; init; }

    public required string FrameRate { get; init; }

    public required string PlaybackRate { get; init; }

    public required string BackendProfile { get; init; }

    public required string DecodePipeline { get; init; }

    public required string RenderPipeline { get; init; }

    public required string RendererPreference { get; init; }

    public required string NativeProviderMode { get; init; }

    public required string PlaybackProvider { get; init; }

    public required string WorkflowProvider { get; init; }

    public required string NativeFallbackReason { get; init; }

    public required string AudioCapabilities { get; init; }

    public required string AudioOutputDevices { get; init; }

    public required string AudioInputDevices { get; init; }

    public required string AudioOutputRoute { get; init; }

    public required string AudioInputRoute { get; init; }

    public required string BackendCapabilityTable { get; init; }

    public required string LastError { get; init; }
}
