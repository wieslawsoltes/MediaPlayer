using System;
using Avalonia;

namespace MediaPlayer.Demo;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                [
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Wgl,
                    Win32RenderingMode.Vulkan,
                    Win32RenderingMode.Software
                ],
                CompositionMode =
                [
                    Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition,
                    Win32CompositionMode.RedirectionSurface
                ],
                OverlayPopups = true
            })
            .With(new X11PlatformOptions
            {
                RenderingMode =
                [
                    X11RenderingMode.Egl,
                    X11RenderingMode.Glx,
                    X11RenderingMode.Vulkan,
                    X11RenderingMode.Software
                ],
                OverlayPopups = true
            })
            .With(new AvaloniaNativePlatformOptions
            {
                // OpenGL first to guarantee OpenGlControlBase interop on macOS.
                RenderingMode =
                [
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Metal,
                    AvaloniaNativeRenderingMode.Software
                ],
                OverlayPopups = true
            })
            .LogToTrace();
}
