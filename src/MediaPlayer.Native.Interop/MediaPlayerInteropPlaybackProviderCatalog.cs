using System.Collections.Generic;
using System.Runtime.InteropServices;
using MediaPlayer.Native.Abstractions;

namespace MediaPlayer.Native.Interop;

public static class MediaPlayerInteropPlaybackProviderCatalog
{
    public static IReadOnlyList<MediaPlayerInteropPlaybackProviderDescriptor> GetPlaybackProviders()
    {
        var providers = new List<MediaPlayerInteropPlaybackProviderDescriptor>(1);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            providers.Add(new MediaPlayerInteropPlaybackProviderDescriptor(
                MediaPlayerInteropPlaybackProviderId.LibVlcManagedInterop,
                "Managed Interop (LibVLC)",
                MediaPlayerNativeProviderKind.Interop,
                IsAvailable: true,
                UnavailableReason: string.Empty));
            return providers;
        }

        providers.Add(new MediaPlayerInteropPlaybackProviderDescriptor(
            MediaPlayerInteropPlaybackProviderId.LibVlcManagedInterop,
            "Managed Interop (LibVLC)",
            MediaPlayerNativeProviderKind.Interop,
            IsAvailable: false,
            UnavailableReason: "No interop playback provider is registered for this runtime platform."));
        return providers;
    }
}
