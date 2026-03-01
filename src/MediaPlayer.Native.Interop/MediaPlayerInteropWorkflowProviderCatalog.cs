using System.Collections.Generic;
using MediaPlayer.Native.Abstractions;

namespace MediaPlayer.Native.Interop;

public static class MediaPlayerInteropWorkflowProviderCatalog
{
    public static IReadOnlyList<MediaPlayerInteropWorkflowProviderDescriptor> GetWorkflowProviders()
    {
        return
        [
            new MediaPlayerInteropWorkflowProviderDescriptor(
                MediaPlayerInteropWorkflowProviderId.ManagedPcmWaveInterop,
                "Managed Interop Workflow (PCM WAV)",
                MediaPlayerNativeProviderKind.Interop,
                IsAvailable: true,
                UnavailableReason: string.Empty),
            new MediaPlayerInteropWorkflowProviderDescriptor(
                MediaPlayerInteropWorkflowProviderId.FfmpegManagedInterop,
                "Managed Interop Workflow (FFmpeg Fallback)",
                MediaPlayerNativeProviderKind.Interop,
                IsAvailable: true,
                UnavailableReason: string.Empty)
        ];
    }
}
