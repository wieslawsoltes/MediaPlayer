using System;
using Avalonia;

namespace MediaPlayer.Demo;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RendererPreferenceState.Initialize(args);
        var runtimePreference = ResolveRuntimePreference(RendererPreferenceState.EffectivePreference);
        BuildAvaloniaApp(runtimePreference).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(RendererPreferenceState.EffectivePreference);

    public static AppBuilder BuildAvaloniaApp(RendererPreference preference) =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions
            {
                RenderingMode = GetWin32RenderingModes(preference),
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
                RenderingMode = GetX11RenderingModes(preference),
                OverlayPopups = true
            })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = GetAvaloniaNativeRenderingModes(preference),
                OverlayPopups = true
            })
            .LogToTrace();

    private static Win32RenderingMode[] GetWin32RenderingModes(RendererPreference preference)
    {
        return preference switch
        {
            RendererPreference.Vulkan =>
            [
                Win32RenderingMode.Vulkan,
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Wgl,
                Win32RenderingMode.Software
            ],
            RendererPreference.Software =>
            [
                Win32RenderingMode.Software,
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Wgl,
                Win32RenderingMode.Vulkan
            ],
            RendererPreference.OpenGl =>
            [
                Win32RenderingMode.Wgl,
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Vulkan,
                Win32RenderingMode.Software
            ],
            _ =>
            [
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Wgl,
                Win32RenderingMode.Vulkan,
                Win32RenderingMode.Software
            ]
        };
    }

    private static X11RenderingMode[] GetX11RenderingModes(RendererPreference preference)
    {
        return preference switch
        {
            RendererPreference.Vulkan =>
            [
                X11RenderingMode.Vulkan,
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            ],
            RendererPreference.Software =>
            [
                X11RenderingMode.Software,
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Vulkan
            ],
            RendererPreference.OpenGl =>
            [
                X11RenderingMode.Glx,
                X11RenderingMode.Egl,
                X11RenderingMode.Vulkan,
                X11RenderingMode.Software
            ],
            _ =>
            [
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Vulkan,
                X11RenderingMode.Software
            ]
        };
    }

    private static AvaloniaNativeRenderingMode[] GetAvaloniaNativeRenderingModes(RendererPreference preference)
    {
        return preference switch
        {
            RendererPreference.OpenGl =>
            [
                AvaloniaNativeRenderingMode.OpenGl,
                AvaloniaNativeRenderingMode.Metal,
                AvaloniaNativeRenderingMode.Software
            ],
            RendererPreference.Software =>
            [
                AvaloniaNativeRenderingMode.Software,
                AvaloniaNativeRenderingMode.Metal,
                AvaloniaNativeRenderingMode.OpenGl
            ],
            RendererPreference.Vulkan =>
            [
                // Avalonia.Native currently exposes Metal/OpenGL/Software modes (no Vulkan mode).
                AvaloniaNativeRenderingMode.Metal,
                AvaloniaNativeRenderingMode.OpenGl,
                AvaloniaNativeRenderingMode.Software
            ],
            // Auto and explicit Metal both prefer Metal on macOS while keeping OpenGL/Software fallback.
            _ =>
            [
                AvaloniaNativeRenderingMode.Metal,
                AvaloniaNativeRenderingMode.OpenGl,
                AvaloniaNativeRenderingMode.Software
            ]
        };
    }

    private static RendererPreference ResolveRuntimePreference(RendererPreference requested)
    {
        if (requested == RendererPreference.OpenGl)
        {
            RendererPreferenceState.SetRuntimePreference(RendererPreference.OpenGl, string.Empty);
            return RendererPreference.OpenGl;
        }

        // GpuMediaPlayer currently relies on OpenGlControlBase, so enforce an OpenGL-capable compositor.
        const string note = "Requested renderer is currently incompatible with OpenGlControlBase video surface. OpenGL is used at runtime.";
        RendererPreferenceState.SetRuntimePreference(RendererPreference.OpenGl, note);
        return RendererPreference.OpenGl;
    }
}
