using Avalonia.Input;

namespace MediaPlayer.Demo;

internal enum PlaybackShortcutCommand
{
    TogglePlayPause,
    SeekToStart,
    SeekToEnd,
    SeekBackward,
    SeekForward,
    StepFrameBackward,
    StepFrameForward,
    ToggleMute,
    ToggleLoop,
    ToggleAutoPlay,
    ToggleFullScreen,
    ExitFullScreen,
    IncreaseVolume,
    DecreaseVolume,
    OpenFile,
    Trim,
    Split,
    Export1080p,
    ActualSize,
    FitToScreen,
    FillMode,
    PanoramicMode,
    GoToTime,
    GoToFrame,
    ShowMovieInspector,
    DecreasePlaybackRate,
    IncreasePlaybackRate,
    ResetPlaybackRate
}

internal readonly record struct PlaybackShortcutContext(
    bool IsPlaying,
    bool IsFullscreen,
    bool IsMacOs);

internal sealed class PlaybackShortcutCommandService
{
    public bool TryResolve(
        Key key,
        KeyModifiers modifiers,
        PlaybackShortcutContext context,
        out PlaybackShortcutCommand command)
    {
        command = default;

        if (modifiers == KeyModifiers.None && key == Key.Space)
        {
            command = PlaybackShortcutCommand.TogglePlayPause;
            return true;
        }

        if (modifiers == KeyModifiers.Alt && key == Key.Left)
        {
            command = PlaybackShortcutCommand.SeekToStart;
            return true;
        }

        if (modifiers == KeyModifiers.Alt && key == Key.Right)
        {
            command = PlaybackShortcutCommand.SeekToEnd;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.Left)
        {
            command = context.IsPlaying
                ? PlaybackShortcutCommand.SeekBackward
                : PlaybackShortcutCommand.StepFrameBackward;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.Right)
        {
            command = context.IsPlaying
                ? PlaybackShortcutCommand.SeekForward
                : PlaybackShortcutCommand.StepFrameForward;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.M)
        {
            command = PlaybackShortcutCommand.ToggleMute;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.L)
        {
            command = PlaybackShortcutCommand.ToggleLoop;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.A)
        {
            command = PlaybackShortcutCommand.ToggleAutoPlay;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.F)
        {
            command = PlaybackShortcutCommand.ToggleFullScreen;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.Escape && context.IsFullscreen)
        {
            command = PlaybackShortcutCommand.ExitFullScreen;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.Up)
        {
            command = PlaybackShortcutCommand.IncreaseVolume;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.Down)
        {
            command = PlaybackShortcutCommand.DecreaseVolume;
            return true;
        }

        if (modifiers == KeyModifiers.None && key == Key.O)
        {
            command = PlaybackShortcutCommand.OpenFile;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && key == Key.T)
        {
            command = PlaybackShortcutCommand.Trim;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && key == Key.Y)
        {
            command = PlaybackShortcutCommand.Split;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && key == Key.E)
        {
            command = PlaybackShortcutCommand.Export1080p;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && (key == Key.D1 || key == Key.NumPad1))
        {
            command = PlaybackShortcutCommand.ActualSize;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && (key == Key.D3 || key == Key.NumPad3))
        {
            command = PlaybackShortcutCommand.FitToScreen;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && (key == Key.D4 || key == Key.NumPad4))
        {
            command = PlaybackShortcutCommand.FillMode;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && (key == Key.D5 || key == Key.NumPad5))
        {
            command = PlaybackShortcutCommand.PanoramicMode;
            return true;
        }

        if (IsCommandModifier(modifiers, context.IsMacOs) && key == Key.G)
        {
            command = PlaybackShortcutCommand.GoToTime;
            return true;
        }

        var shiftCommandModifiers = (context.IsMacOs ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift;
        if (modifiers == shiftCommandModifiers && key == Key.G)
        {
            command = PlaybackShortcutCommand.GoToFrame;
            return true;
        }

        if ((modifiers == KeyModifiers.Meta || modifiers == KeyModifiers.Control) && key == Key.I)
        {
            command = PlaybackShortcutCommand.ShowMovieInspector;
            return true;
        }

        if (modifiers == KeyModifiers.None && (key == Key.OemOpenBrackets || key == Key.Subtract))
        {
            command = PlaybackShortcutCommand.DecreasePlaybackRate;
            return true;
        }

        if (modifiers == KeyModifiers.None && (key == Key.OemCloseBrackets || key == Key.Add))
        {
            command = PlaybackShortcutCommand.IncreasePlaybackRate;
            return true;
        }

        if (modifiers == KeyModifiers.None && (key == Key.D0 || key == Key.NumPad0))
        {
            command = PlaybackShortcutCommand.ResetPlaybackRate;
            return true;
        }

        return false;
    }

    private static bool IsCommandModifier(KeyModifiers modifiers, bool isMacOs)
    {
        return modifiers == (isMacOs ? KeyModifiers.Meta : KeyModifiers.Control);
    }
}
