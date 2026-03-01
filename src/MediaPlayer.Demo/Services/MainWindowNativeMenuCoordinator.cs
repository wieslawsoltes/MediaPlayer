using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using MediaPlayer.Controls;
using MediaPlayer.Controls.Backends;
using MediaPlayer.Controls.Workflows;

namespace MediaPlayer.Demo;

internal enum MenuTimeDisplayMode
{
    Remaining,
    Elapsed,
    Timecode,
    FrameCount
}

internal readonly record struct MainWindowNativeMenuState(
    bool IsPlaying,
    bool IsMuted,
    bool IsLooping,
    bool IsFullscreen,
    bool AlwaysShowControls,
    bool Topmost,
    MenuTimeDisplayMode TimeDisplayMode,
    VideoLayoutMode LayoutMode,
    double PlaybackRate,
    RendererPreference RendererPreference,
    MediaWorkflowQualityProfile WorkflowQualityProfile);

internal sealed class MainWindowNativeMenuActions
{
    public required EventHandler OnAboutClicked { get; init; }
    public required EventHandler OnPreferencesClicked { get; init; }
    public required EventHandler OnQuitClicked { get; init; }
    public required EventHandler OnOpenFileClicked { get; init; }
    public required EventHandler OnOpenLocationClicked { get; init; }
    public required EventHandler OnNewScreenRecordingClicked { get; init; }
    public required EventHandler OnNewMovieRecordingClicked { get; init; }
    public required EventHandler OnNewAudioRecordingClicked { get; init; }
    public required EventHandler OnExport4KClicked { get; init; }
    public required EventHandler OnExport1080pClicked { get; init; }
    public required EventHandler OnExport720pClicked { get; init; }
    public required EventHandler OnExport480pClicked { get; init; }
    public required EventHandler OnExportAudioOnlyClicked { get; init; }
    public required EventHandler OnWorkflowQualitySpeedClicked { get; init; }
    public required EventHandler OnWorkflowQualityBalancedClicked { get; init; }
    public required EventHandler OnWorkflowQualityQualityClicked { get; init; }
    public required EventHandler OnShareCurrentMediaClicked { get; init; }
    public required EventHandler OnPlayPauseClicked { get; init; }
    public required EventHandler OnStopClicked { get; init; }
    public required EventHandler OnCloseWindowClicked { get; init; }
    public required EventHandler OnNotImplementedClicked { get; init; }
    public required EventHandler OnTrimClicked { get; init; }
    public required EventHandler OnSplitClipClicked { get; init; }
    public required EventHandler OnCombineClipsClicked { get; init; }
    public required EventHandler OnRemoveAudioClicked { get; init; }
    public required EventHandler OnRemoveVideoClicked { get; init; }
    public required EventHandler OnRotateClockwiseClicked { get; init; }
    public required EventHandler OnRotateCounterClockwiseClicked { get; init; }
    public required EventHandler OnFlipHorizontalClicked { get; init; }
    public required EventHandler OnFlipVerticalClicked { get; init; }
    public required EventHandler OnSelectAllClicked { get; init; }
    public required EventHandler OnToggleMuteClicked { get; init; }
    public required EventHandler OnToggleLoopClicked { get; init; }
    public required EventHandler OnToggleAlwaysShowControlsClicked { get; init; }
    public required EventHandler OnToggleFloatOnTopClicked { get; init; }
    public required EventHandler OnTimeDisplayRemainingClicked { get; init; }
    public required EventHandler OnTimeDisplayElapsedClicked { get; init; }
    public required EventHandler OnTimeDisplayTimecodeClicked { get; init; }
    public required EventHandler OnTimeDisplayFramesClicked { get; init; }
    public required EventHandler OnGoToTimeClicked { get; init; }
    public required EventHandler OnGoToFrameClicked { get; init; }
    public required EventHandler OnActualSizeClicked { get; init; }
    public required EventHandler OnFitToScreenClicked { get; init; }
    public required EventHandler OnFillModeClicked { get; init; }
    public required EventHandler OnPanoramicModeClicked { get; init; }
    public required EventHandler OnRendererAutoClicked { get; init; }
    public required EventHandler OnRendererOpenGlClicked { get; init; }
    public required EventHandler OnRendererVulkanClicked { get; init; }
    public required EventHandler OnRendererMetalClicked { get; init; }
    public required EventHandler OnRendererSoftwareClicked { get; init; }
    public required EventHandler OnToggleFullScreenClicked { get; init; }
    public required EventHandler OnShowMovieInspectorClicked { get; init; }
    public required EventHandler OnMinimizeClicked { get; init; }
    public required EventHandler OnZoomClicked { get; init; }
    public required EventHandler OnBringAllToFrontClicked { get; init; }
    public required EventHandler OnHelpClicked { get; init; }
    public required EventHandler OnClearRecentClicked { get; init; }
    public required Action<Uri> OnOpenRecentSource { get; init; }
    public required Action<int> OnAudioTrackSelected { get; init; }
    public required Action<int> OnSubtitleTrackSelected { get; init; }
    public required Action<double> OnPlaybackRateSelected { get; init; }
}

internal sealed class MainWindowNativeMenuCoordinator
{
    private readonly Window _owner;
    private readonly MainWindowNativeMenuActions _actions;
    private readonly Func<Uri, string> _buildDisplayTitle;
    private readonly Func<IReadOnlyList<Uri>> _getRecentSources;
    private readonly Func<IReadOnlyList<MediaTrackInfo>> _getAudioTracks;
    private readonly Func<IReadOnlyList<MediaTrackInfo>> _getSubtitleTracks;
    private readonly IReadOnlyList<double> _playbackRates;
    private readonly bool _isMacOs;

    private NativeMenuItem? _menuPlayPauseItem;
    private NativeMenuItem? _menuOpenRecentRoot;
    private NativeMenuItem? _menuMuteItem;
    private NativeMenuItem? _menuLoopItem;
    private NativeMenuItem? _menuFullscreenItem;
    private NativeMenuItem? _menuAlwaysShowControlsItem;
    private NativeMenuItem? _menuRendererAutoItem;
    private NativeMenuItem? _menuRendererOpenGlItem;
    private NativeMenuItem? _menuRendererVulkanItem;
    private NativeMenuItem? _menuRendererMetalItem;
    private NativeMenuItem? _menuRendererSoftwareItem;
    private NativeMenuItem? _menuFloatOnTopItem;
    private NativeMenuItem? _menuTimeRemainingItem;
    private NativeMenuItem? _menuTimeElapsedItem;
    private NativeMenuItem? _menuTimeTimecodeItem;
    private NativeMenuItem? _menuTimeFramesItem;
    private NativeMenuItem? _menuVideoFitItem;
    private NativeMenuItem? _menuVideoFillItem;
    private NativeMenuItem? _menuVideoPanoramicItem;
    private NativeMenuItem? _menuAudioTracksRoot;
    private NativeMenuItem? _menuSubtitleTracksRoot;
    private NativeMenuItem? _menuWorkflowQualitySpeedItem;
    private NativeMenuItem? _menuWorkflowQualityBalancedItem;
    private NativeMenuItem? _menuWorkflowQualityQualityItem;
    private readonly Dictionary<double, NativeMenuItem> _menuPlaybackRateItems = [];

    private bool _recentMenuDirty = true;
    private bool _trackMenusDirty = true;
    private bool _isAttached;
    private MainWindowNativeMenuState _lastState;

    public MainWindowNativeMenuCoordinator(
        Window owner,
        MainWindowNativeMenuActions actions,
        Func<Uri, string> buildDisplayTitle,
        Func<IReadOnlyList<Uri>> getRecentSources,
        Func<IReadOnlyList<MediaTrackInfo>> getAudioTracks,
        Func<IReadOnlyList<MediaTrackInfo>> getSubtitleTracks,
        IReadOnlyList<double> playbackRates)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(buildDisplayTitle);
        ArgumentNullException.ThrowIfNull(getRecentSources);
        ArgumentNullException.ThrowIfNull(getAudioTracks);
        ArgumentNullException.ThrowIfNull(getSubtitleTracks);
        ArgumentNullException.ThrowIfNull(playbackRates);

        _owner = owner;
        _actions = actions;
        _buildDisplayTitle = buildDisplayTitle;
        _getRecentSources = getRecentSources;
        _getAudioTracks = getAudioTracks;
        _getSubtitleTracks = getSubtitleTracks;
        _playbackRates = playbackRates;
        _isMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    public void AttachIfSupported(MainWindowNativeMenuState initialState)
    {
        if (!_isMacOs || _isAttached)
        {
            return;
        }

        _isAttached = true;
        var menu = BuildNativeMenu();
        menu.NeedsUpdate += (_, _) =>
        {
            _recentMenuDirty = true;
            _trackMenusDirty = true;
            Update(_lastState);
        };
        NativeMenu.SetMenu(_owner, menu);
        Update(initialState);
    }

    public void MarkRecentDirty() => _recentMenuDirty = true;

    public void MarkTrackMenusDirty() => _trackMenusDirty = true;

    public void Update(MainWindowNativeMenuState state)
    {
        _lastState = state;
        if (!_isMacOs)
        {
            return;
        }

        if (_recentMenuDirty)
        {
            RefreshOpenRecentMenu();
        }

        if (_trackMenusDirty)
        {
            RefreshTrackMenus();
        }

        if (_menuPlayPauseItem is not null)
        {
            _menuPlayPauseItem.Header = state.IsPlaying ? "Pause" : "Play";
        }

        if (_menuMuteItem is not null)
        {
            _menuMuteItem.IsChecked = state.IsMuted;
        }

        if (_menuLoopItem is not null)
        {
            _menuLoopItem.IsChecked = state.IsLooping;
        }

        if (_menuFullscreenItem is not null)
        {
            _menuFullscreenItem.IsChecked = state.IsFullscreen;
            _menuFullscreenItem.Header = state.IsFullscreen ? "Exit Full Screen" : "Enter Full Screen";
        }

        if (_menuAlwaysShowControlsItem is not null)
        {
            _menuAlwaysShowControlsItem.IsChecked = state.AlwaysShowControls;
        }

        if (_menuFloatOnTopItem is not null)
        {
            _menuFloatOnTopItem.IsChecked = state.Topmost;
        }

        if (_menuTimeRemainingItem is not null)
        {
            _menuTimeRemainingItem.IsChecked = state.TimeDisplayMode == MenuTimeDisplayMode.Remaining;
        }

        if (_menuTimeElapsedItem is not null)
        {
            _menuTimeElapsedItem.IsChecked = state.TimeDisplayMode == MenuTimeDisplayMode.Elapsed;
        }

        if (_menuTimeTimecodeItem is not null)
        {
            _menuTimeTimecodeItem.IsChecked = state.TimeDisplayMode == MenuTimeDisplayMode.Timecode;
        }

        if (_menuTimeFramesItem is not null)
        {
            _menuTimeFramesItem.IsChecked = state.TimeDisplayMode == MenuTimeDisplayMode.FrameCount;
        }

        if (_menuVideoFitItem is not null)
        {
            _menuVideoFitItem.IsChecked = state.LayoutMode == VideoLayoutMode.Fit;
        }

        if (_menuVideoFillItem is not null)
        {
            _menuVideoFillItem.IsChecked = state.LayoutMode == VideoLayoutMode.Fill;
        }

        if (_menuVideoPanoramicItem is not null)
        {
            _menuVideoPanoramicItem.IsChecked = state.LayoutMode == VideoLayoutMode.Panoramic;
        }

        foreach (var pair in _menuPlaybackRateItems)
        {
            pair.Value.IsChecked = Math.Abs(state.PlaybackRate - pair.Key) < 0.01d;
        }

        if (_menuRendererAutoItem is not null)
        {
            _menuRendererAutoItem.IsChecked = state.RendererPreference == RendererPreference.Auto;
        }

        if (_menuRendererOpenGlItem is not null)
        {
            _menuRendererOpenGlItem.IsChecked = state.RendererPreference == RendererPreference.OpenGl;
        }

        if (_menuRendererVulkanItem is not null)
        {
            _menuRendererVulkanItem.IsChecked = state.RendererPreference == RendererPreference.Vulkan;
        }

        if (_menuRendererMetalItem is not null)
        {
            _menuRendererMetalItem.IsChecked = state.RendererPreference == RendererPreference.Metal;
        }

        if (_menuRendererSoftwareItem is not null)
        {
            _menuRendererSoftwareItem.IsChecked = state.RendererPreference == RendererPreference.Software;
        }

        if (_menuWorkflowQualitySpeedItem is not null)
        {
            _menuWorkflowQualitySpeedItem.IsChecked = state.WorkflowQualityProfile == MediaWorkflowQualityProfile.Speed;
        }

        if (_menuWorkflowQualityBalancedItem is not null)
        {
            _menuWorkflowQualityBalancedItem.IsChecked = state.WorkflowQualityProfile == MediaWorkflowQualityProfile.Balanced;
        }

        if (_menuWorkflowQualityQualityItem is not null)
        {
            _menuWorkflowQualityQualityItem.IsChecked = state.WorkflowQualityProfile == MediaWorkflowQualityProfile.Quality;
        }
    }

    private void RefreshOpenRecentMenu()
    {
        if (_menuOpenRecentRoot is null)
        {
            _recentMenuDirty = false;
            return;
        }

        var recentSources = _getRecentSources();
        var menu = new NativeMenu();
        if (recentSources.Count == 0)
        {
            menu.Add(new NativeMenuItem("None")
            {
                IsEnabled = false
            });
            _menuOpenRecentRoot.Menu = menu;
            _recentMenuDirty = false;
            return;
        }

        foreach (var source in recentSources)
        {
            var item = new NativeMenuItem(_buildDisplayTitle(source));
            item.Click += (_, _) => _actions.OnOpenRecentSource(source);
            menu.Add(item);
        }

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(CreateNativeMenuItem("Clear Menu", null, _actions.OnClearRecentClicked));
        _menuOpenRecentRoot.Menu = menu;
        _recentMenuDirty = false;
    }

    private void RefreshTrackMenus()
    {
        RefreshTrackMenu(_menuAudioTracksRoot, _getAudioTracks(), _actions.OnAudioTrackSelected, "No audio tracks");
        RefreshTrackMenu(_menuSubtitleTracksRoot, _getSubtitleTracks(), _actions.OnSubtitleTrackSelected, "No subtitles");
        _trackMenusDirty = false;
    }

    private static void RefreshTrackMenu(
        NativeMenuItem? root,
        IReadOnlyList<MediaTrackInfo> tracks,
        Action<int> onSelect,
        string emptyCaption)
    {
        if (root is null)
        {
            return;
        }

        var menu = new NativeMenu();
        if (tracks.Count == 0)
        {
            menu.Add(new NativeMenuItem(emptyCaption)
            {
                IsEnabled = false
            });
            root.Menu = menu;
            return;
        }

        foreach (var track in tracks)
        {
            var selectedTrackId = track.Id;
            var item = new NativeMenuItem(track.Name)
            {
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = track.IsSelected
            };
            item.Click += (_, _) => onSelect(selectedTrackId);
            menu.Add(item);
        }

        root.Menu = menu;
    }

    private NativeMenu BuildNativeMenu()
    {
        var appMenu = new NativeMenuItem("Media Player")
        {
            Menu = new NativeMenu()
        };
        appMenu.Menu!.Add(CreateNativeMenuItem("About Media Player", null, _actions.OnAboutClicked));
        appMenu.Menu.Add(new NativeMenuItemSeparator());
        appMenu.Menu.Add(CreateNativeMenuItem("Preferences…", null, _actions.OnPreferencesClicked));
        appMenu.Menu.Add(new NativeMenuItemSeparator());
        appMenu.Menu.Add(CreateNativeMenuItem("Quit Media Player", new KeyGesture(Key.Q, KeyModifiers.Meta), _actions.OnQuitClicked));

        var fileMenu = new NativeMenuItem("File")
        {
            Menu = new NativeMenu()
        };
        fileMenu.Menu!.Add(CreateNativeMenuItem("Open File…", new KeyGesture(Key.O, KeyModifiers.Meta), _actions.OnOpenFileClicked));
        fileMenu.Menu.Add(CreateNativeMenuItem("Open Location…", new KeyGesture(Key.O, KeyModifiers.Meta | KeyModifiers.Shift), _actions.OnOpenLocationClicked));
        _menuOpenRecentRoot = new NativeMenuItem("Open Recent")
        {
            Menu = new NativeMenu()
        };
        fileMenu.Menu.Add(_menuOpenRecentRoot);
        fileMenu.Menu.Add(new NativeMenuItemSeparator());
        var newRecordingMenu = new NativeMenuItem("New Recording")
        {
            Menu = new NativeMenu()
        };
        newRecordingMenu.Menu!.Add(CreateNativeMenuItem("New Screen Recording…", new KeyGesture(Key.N, KeyModifiers.Meta | KeyModifiers.Control), _actions.OnNewScreenRecordingClicked));
        newRecordingMenu.Menu.Add(CreateNativeMenuItem("New Movie Recording…", new KeyGesture(Key.N, KeyModifiers.Meta | KeyModifiers.Alt), _actions.OnNewMovieRecordingClicked));
        newRecordingMenu.Menu.Add(CreateNativeMenuItem("New Audio Recording…", new KeyGesture(Key.N, KeyModifiers.Meta | KeyModifiers.Shift), _actions.OnNewAudioRecordingClicked));
        fileMenu.Menu.Add(newRecordingMenu);
        fileMenu.Menu.Add(new NativeMenuItemSeparator());
        var exportAsMenu = new NativeMenuItem("Export As")
        {
            Menu = new NativeMenu()
        };
        exportAsMenu.Menu!.Add(CreateNativeMenuItem("4K…", null, _actions.OnExport4KClicked));
        exportAsMenu.Menu.Add(CreateNativeMenuItem("1080p…", new KeyGesture(Key.E, KeyModifiers.Meta), _actions.OnExport1080pClicked));
        exportAsMenu.Menu.Add(CreateNativeMenuItem("720p…", null, _actions.OnExport720pClicked));
        exportAsMenu.Menu.Add(CreateNativeMenuItem("480p…", null, _actions.OnExport480pClicked));
        exportAsMenu.Menu.Add(new NativeMenuItemSeparator());
        exportAsMenu.Menu.Add(CreateNativeMenuItem("Audio Only…", null, _actions.OnExportAudioOnlyClicked));
        fileMenu.Menu.Add(exportAsMenu);
        var workflowQualityMenu = new NativeMenuItem("Workflow Quality")
        {
            Menu = new NativeMenu()
        };
        _menuWorkflowQualitySpeedItem = CreateNativeMenuItem(
            "Speed",
            null,
            _actions.OnWorkflowQualitySpeedClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuWorkflowQualityBalancedItem = CreateNativeMenuItem(
            "Balanced",
            null,
            _actions.OnWorkflowQualityBalancedClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuWorkflowQualityQualityItem = CreateNativeMenuItem(
            "Quality",
            null,
            _actions.OnWorkflowQualityQualityClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        workflowQualityMenu.Menu!.Add(_menuWorkflowQualitySpeedItem);
        workflowQualityMenu.Menu.Add(_menuWorkflowQualityBalancedItem);
        workflowQualityMenu.Menu.Add(_menuWorkflowQualityQualityItem);
        fileMenu.Menu.Add(workflowQualityMenu);
        fileMenu.Menu.Add(CreateNativeMenuItem("Share Current Media", null, _actions.OnShareCurrentMediaClicked));
        fileMenu.Menu.Add(new NativeMenuItemSeparator());
        _menuPlayPauseItem = CreateNativeMenuItem("Play", new KeyGesture(Key.Space), _actions.OnPlayPauseClicked);
        fileMenu.Menu.Add(_menuPlayPauseItem);
        fileMenu.Menu.Add(CreateNativeMenuItem("Stop", null, _actions.OnStopClicked));
        fileMenu.Menu.Add(new NativeMenuItemSeparator());
        fileMenu.Menu.Add(CreateNativeMenuItem("Close Window", new KeyGesture(Key.W, KeyModifiers.Meta), _actions.OnCloseWindowClicked));

        var editMenu = new NativeMenuItem("Edit")
        {
            Menu = new NativeMenu()
        };
        editMenu.Menu!.Add(CreateNativeMenuItem("Undo", new KeyGesture(Key.Z, KeyModifiers.Meta), _actions.OnNotImplementedClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Redo", new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift), _actions.OnNotImplementedClicked, enabled: false));
        editMenu.Menu.Add(new NativeMenuItemSeparator());
        editMenu.Menu.Add(CreateNativeMenuItem("Trim…", new KeyGesture(Key.T, KeyModifiers.Meta), _actions.OnTrimClicked));
        editMenu.Menu.Add(CreateNativeMenuItem("Split Clip…", new KeyGesture(Key.Y, KeyModifiers.Meta), _actions.OnSplitClipClicked));
        editMenu.Menu.Add(CreateNativeMenuItem("Combine Clips…", null, _actions.OnCombineClipsClicked));
        editMenu.Menu.Add(new NativeMenuItemSeparator());
        var rotateFlipMenu = new NativeMenuItem("Rotate & Flip")
        {
            Menu = new NativeMenu()
        };
        rotateFlipMenu.Menu!.Add(CreateNativeMenuItem("Rotate Clockwise", null, _actions.OnRotateClockwiseClicked));
        rotateFlipMenu.Menu.Add(CreateNativeMenuItem("Rotate Counterclockwise", null, _actions.OnRotateCounterClockwiseClicked));
        rotateFlipMenu.Menu.Add(new NativeMenuItemSeparator());
        rotateFlipMenu.Menu.Add(CreateNativeMenuItem("Flip Horizontal", null, _actions.OnFlipHorizontalClicked));
        rotateFlipMenu.Menu.Add(CreateNativeMenuItem("Flip Vertical", null, _actions.OnFlipVerticalClicked));
        editMenu.Menu.Add(rotateFlipMenu);
        editMenu.Menu.Add(new NativeMenuItemSeparator());
        editMenu.Menu.Add(CreateNativeMenuItem("Remove Audio…", null, _actions.OnRemoveAudioClicked));
        editMenu.Menu.Add(CreateNativeMenuItem("Remove Video…", null, _actions.OnRemoveVideoClicked));
        editMenu.Menu.Add(new NativeMenuItemSeparator());
        editMenu.Menu.Add(CreateNativeMenuItem("Cut", new KeyGesture(Key.X, KeyModifiers.Meta), _actions.OnNotImplementedClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Copy", new KeyGesture(Key.C, KeyModifiers.Meta), _actions.OnNotImplementedClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Paste", new KeyGesture(Key.V, KeyModifiers.Meta), _actions.OnNotImplementedClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Select All", new KeyGesture(Key.A, KeyModifiers.Meta), _actions.OnSelectAllClicked));

        var viewMenu = new NativeMenuItem("View")
        {
            Menu = new NativeMenu()
        };
        _menuMuteItem = CreateNativeMenuItem(
            "Mute",
            new KeyGesture(Key.M, KeyModifiers.Meta),
            _actions.OnToggleMuteClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu!.Add(_menuMuteItem);
        _menuLoopItem = CreateNativeMenuItem(
            "Loop",
            new KeyGesture(Key.L, KeyModifiers.Meta),
            _actions.OnToggleLoopClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu.Add(_menuLoopItem);
        _menuAlwaysShowControlsItem = CreateNativeMenuItem(
            "Always Show Controls",
            null,
            _actions.OnToggleAlwaysShowControlsClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu.Add(_menuAlwaysShowControlsItem);
        viewMenu.Menu.Add(new NativeMenuItemSeparator());
        _menuFloatOnTopItem = CreateNativeMenuItem(
            "Float On Top",
            null,
            _actions.OnToggleFloatOnTopClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu.Add(_menuFloatOnTopItem);
        viewMenu.Menu.Add(new NativeMenuItemSeparator());
        var timeDisplayMenu = new NativeMenuItem("Time Display")
        {
            Menu = new NativeMenu()
        };
        _menuTimeRemainingItem = CreateNativeMenuItem(
            "Remaining Time",
            null,
            _actions.OnTimeDisplayRemainingClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuTimeElapsedItem = CreateNativeMenuItem(
            "Elapsed Time",
            null,
            _actions.OnTimeDisplayElapsedClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuTimeTimecodeItem = CreateNativeMenuItem(
            "Timecode",
            null,
            _actions.OnTimeDisplayTimecodeClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuTimeFramesItem = CreateNativeMenuItem(
            "Frame Count",
            null,
            _actions.OnTimeDisplayFramesClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        timeDisplayMenu.Menu!.Add(_menuTimeRemainingItem);
        timeDisplayMenu.Menu.Add(_menuTimeElapsedItem);
        timeDisplayMenu.Menu.Add(_menuTimeTimecodeItem);
        timeDisplayMenu.Menu.Add(_menuTimeFramesItem);
        timeDisplayMenu.Menu.Add(new NativeMenuItemSeparator());
        timeDisplayMenu.Menu.Add(CreateNativeMenuItem("Go To Time…", new KeyGesture(Key.G, KeyModifiers.Meta), _actions.OnGoToTimeClicked));
        timeDisplayMenu.Menu.Add(CreateNativeMenuItem("Go To Frame…", null, _actions.OnGoToFrameClicked));
        viewMenu.Menu.Add(timeDisplayMenu);
        var videoSizeMenu = new NativeMenuItem("Video Size")
        {
            Menu = new NativeMenu()
        };
        videoSizeMenu.Menu!.Add(CreateNativeMenuItem("Actual Size", new KeyGesture(Key.D1, KeyModifiers.Meta), _actions.OnActualSizeClicked));
        _menuVideoFitItem = CreateNativeMenuItem(
            "Fit to Screen",
            new KeyGesture(Key.D3, KeyModifiers.Meta),
            _actions.OnFitToScreenClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuVideoFillItem = CreateNativeMenuItem(
            "Fill",
            new KeyGesture(Key.D4, KeyModifiers.Meta),
            _actions.OnFillModeClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuVideoPanoramicItem = CreateNativeMenuItem(
            "Panoramic",
            new KeyGesture(Key.D5, KeyModifiers.Meta),
            _actions.OnPanoramicModeClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        videoSizeMenu.Menu.Add(_menuVideoFitItem);
        videoSizeMenu.Menu.Add(_menuVideoFillItem);
        videoSizeMenu.Menu.Add(_menuVideoPanoramicItem);
        viewMenu.Menu.Add(videoSizeMenu);
        var speedMenu = new NativeMenuItem("Playback Speed")
        {
            Menu = new NativeMenu()
        };
        foreach (var rate in _playbackRates)
        {
            var selectedRate = rate;
            var item = CreateNativeMenuItem(
                $"{rate:0.##}x",
                null,
                (_, _) => _actions.OnPlaybackRateSelected(selectedRate),
                toggleType: NativeMenuItemToggleType.CheckBox);
            _menuPlaybackRateItems[rate] = item;
            speedMenu.Menu!.Add(item);
        }

        viewMenu.Menu.Add(speedMenu);
        viewMenu.Menu.Add(new NativeMenuItemSeparator());
        _menuAudioTracksRoot = new NativeMenuItem("Audio Track")
        {
            Menu = new NativeMenu()
        };
        _menuSubtitleTracksRoot = new NativeMenuItem("Subtitles")
        {
            Menu = new NativeMenu()
        };
        viewMenu.Menu.Add(_menuAudioTracksRoot);
        viewMenu.Menu.Add(_menuSubtitleTracksRoot);
        viewMenu.Menu.Add(new NativeMenuItemSeparator());
        var rendererMenu = new NativeMenuItem("Renderer")
        {
            Menu = new NativeMenu()
        };
        _menuRendererAutoItem = CreateNativeMenuItem(
            "Auto (Metal preferred on macOS)",
            null,
            _actions.OnRendererAutoClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuRendererOpenGlItem = CreateNativeMenuItem(
            "OpenGL",
            null,
            _actions.OnRendererOpenGlClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuRendererVulkanItem = CreateNativeMenuItem(
            "Vulkan",
            null,
            _actions.OnRendererVulkanClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuRendererMetalItem = CreateNativeMenuItem(
            "Metal",
            null,
            _actions.OnRendererMetalClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        _menuRendererSoftwareItem = CreateNativeMenuItem(
            "Software",
            null,
            _actions.OnRendererSoftwareClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        rendererMenu.Menu!.Add(_menuRendererAutoItem);
        rendererMenu.Menu.Add(_menuRendererOpenGlItem);
        rendererMenu.Menu.Add(_menuRendererVulkanItem);
        rendererMenu.Menu.Add(_menuRendererMetalItem);
        rendererMenu.Menu.Add(_menuRendererSoftwareItem);
        viewMenu.Menu.Add(rendererMenu);
        viewMenu.Menu.Add(new NativeMenuItemSeparator());
        _menuFullscreenItem = CreateNativeMenuItem(
            "Enter Full Screen",
            new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control),
            _actions.OnToggleFullScreenClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu.Add(_menuFullscreenItem);

        var windowMenu = new NativeMenuItem("Window")
        {
            Menu = new NativeMenu()
        };
        windowMenu.Menu!.Add(CreateNativeMenuItem("Show Movie Inspector", new KeyGesture(Key.I, KeyModifiers.Meta), _actions.OnShowMovieInspectorClicked));
        windowMenu.Menu.Add(new NativeMenuItemSeparator());
        windowMenu.Menu.Add(CreateNativeMenuItem("Minimize", new KeyGesture(Key.M, KeyModifiers.Meta), _actions.OnMinimizeClicked));
        windowMenu.Menu.Add(CreateNativeMenuItem("Zoom", null, _actions.OnZoomClicked));
        windowMenu.Menu.Add(new NativeMenuItemSeparator());
        windowMenu.Menu.Add(CreateNativeMenuItem("Bring All to Front", null, _actions.OnBringAllToFrontClicked));

        var helpMenu = new NativeMenuItem("Help")
        {
            Menu = new NativeMenu()
        };
        helpMenu.Menu!.Add(CreateNativeMenuItem("Media Player Help", null, _actions.OnHelpClicked));

        return new NativeMenu
        {
            appMenu,
            fileMenu,
            editMenu,
            viewMenu,
            windowMenu,
            helpMenu
        };
    }

    private static NativeMenuItem CreateNativeMenuItem(
        string header,
        KeyGesture? gesture,
        EventHandler onClick,
        bool enabled = true,
        NativeMenuItemToggleType toggleType = NativeMenuItemToggleType.None)
    {
        var item = new NativeMenuItem(header)
        {
            IsEnabled = enabled,
            ToggleType = toggleType
        };
        if (gesture is not null)
        {
            item.Gesture = gesture;
        }

        item.Click += onClick;
        return item;
    }
}
