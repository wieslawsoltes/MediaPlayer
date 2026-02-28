using System.Runtime.InteropServices;

namespace MediaPlayer.Controls.Backends;

internal static class LibVlcPlatformProfileResolver
{
    public static LibVlcPlatformProfile Resolve()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new LibVlcPlatformProfile(
                Name: "Windows",
                NativeDecodeApi: "Media Foundation + D3D11VA",
                NativeRenderPipeline: "D3D11 decoder -> libVLC decode callbacks -> Avalonia OpenGL surface",
                LibVlcOptions: []);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new LibVlcPlatformProfile(
                Name: "macOS",
                NativeDecodeApi: "AVFoundation / VideoToolbox",
                NativeRenderPipeline: "VideoToolbox decoder -> libVLC decode callbacks -> Avalonia OpenGL surface",
                LibVlcOptions: []);
        }

        return new LibVlcPlatformProfile(
            Name: "Linux",
            NativeDecodeApi: "VAAPI / VDPAU (driver dependent)",
            NativeRenderPipeline: "VAAPI decoder -> libVLC decode callbacks -> Avalonia OpenGL surface",
            LibVlcOptions: []);
    }
}
