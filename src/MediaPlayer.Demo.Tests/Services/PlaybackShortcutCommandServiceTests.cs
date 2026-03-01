using Avalonia.Input;
using MediaPlayer.Demo;

namespace MediaPlayer.Demo.Tests.Services;

public sealed class PlaybackShortcutCommandServiceTests
{
    [Fact]
    public void TryResolve_GoToTime_UsesControlOnNonMac()
    {
        PlaybackShortcutCommandService service = new();
        PlaybackShortcutContext context = new(IsPlaying: true, IsFullscreen: false, IsMacOs: false);

        bool resolved = service.TryResolve(Key.G, KeyModifiers.Control, context, out PlaybackShortcutCommand command);

        Assert.True(resolved);
        Assert.Equal(PlaybackShortcutCommand.GoToTime, command);
    }

    [Fact]
    public void TryResolve_GoToTime_UsesMetaOnMac()
    {
        PlaybackShortcutCommandService service = new();
        PlaybackShortcutContext context = new(IsPlaying: true, IsFullscreen: false, IsMacOs: true);

        bool resolved = service.TryResolve(Key.G, KeyModifiers.Meta, context, out PlaybackShortcutCommand command);

        Assert.True(resolved);
        Assert.Equal(PlaybackShortcutCommand.GoToTime, command);
    }

    [Fact]
    public void TryResolve_GoToFrame_UsesShiftCommandModifierPerPlatform()
    {
        PlaybackShortcutCommandService service = new();

        PlaybackShortcutContext nonMacContext = new(IsPlaying: true, IsFullscreen: false, IsMacOs: false);
        bool nonMacResolved = service.TryResolve(
            Key.G,
            KeyModifiers.Control | KeyModifiers.Shift,
            nonMacContext,
            out PlaybackShortcutCommand nonMacCommand);
        Assert.True(nonMacResolved);
        Assert.Equal(PlaybackShortcutCommand.GoToFrame, nonMacCommand);

        PlaybackShortcutContext macContext = new(IsPlaying: true, IsFullscreen: false, IsMacOs: true);
        bool macResolved = service.TryResolve(
            Key.G,
            KeyModifiers.Meta | KeyModifiers.Shift,
            macContext,
            out PlaybackShortcutCommand macCommand);
        Assert.True(macResolved);
        Assert.Equal(PlaybackShortcutCommand.GoToFrame, macCommand);
    }
}
