using System;

namespace MediaPlayer.Native.Abstractions;

public sealed class MediaPlayerNativeOptions
{
    public MediaPlayerNativeProviderMode ProviderMode { get; set; } = MediaPlayerNativeProviderMode.AutoPreferInterop;

    public MediaPlayerNativeOptions Clone()
    {
        return new MediaPlayerNativeOptions
        {
            ProviderMode = ProviderMode
        };
    }

    public static MediaPlayerNativeOptions FromEnvironment()
    {
        var options = new MediaPlayerNativeOptions();
        var rawMode = Environment.GetEnvironmentVariable(MediaPlayerNativeEnvironment.ProviderModeVariableName);
        if (MediaPlayerNativeProviderModeParser.TryParse(rawMode, out var mode))
        {
            options.ProviderMode = mode;
        }

        return options;
    }
}
