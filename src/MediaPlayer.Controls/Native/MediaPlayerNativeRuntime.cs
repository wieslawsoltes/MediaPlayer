using System;
using MediaPlayer.Native.Abstractions;

namespace MediaPlayer.Controls;

public static class MediaPlayerNativeRuntime
{
    private static readonly object s_gate = new();
    private static MediaPlayerNativeOptions s_options = MediaPlayerNativeOptions.FromEnvironment();
    private static MediaPlayerNativeProviderDiagnostics s_playbackDiagnostics =
        new(
            s_options.ProviderMode,
            MediaPlayerNativeProviderKind.Unknown,
            string.Empty);

    public static MediaPlayerNativeOptions GetOptions()
    {
        lock (s_gate)
        {
            return s_options.Clone();
        }
    }

    public static void Configure(MediaPlayerNativeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (s_gate)
        {
            s_options = options.Clone();
            s_playbackDiagnostics = new MediaPlayerNativeProviderDiagnostics(
                s_options.ProviderMode,
                MediaPlayerNativeProviderKind.Unknown,
                string.Empty);
        }
    }

    public static MediaPlayerNativeProviderDiagnostics GetPlaybackDiagnostics()
    {
        lock (s_gate)
        {
            return s_playbackDiagnostics;
        }
    }

    internal static void ReportPlaybackProvider(
        MediaPlayerNativeProviderMode mode,
        MediaPlayerNativeProviderKind activeProvider,
        string fallbackReason)
    {
        lock (s_gate)
        {
            s_playbackDiagnostics = new MediaPlayerNativeProviderDiagnostics(
                mode,
                activeProvider,
                fallbackReason ?? string.Empty);
        }
    }
}
