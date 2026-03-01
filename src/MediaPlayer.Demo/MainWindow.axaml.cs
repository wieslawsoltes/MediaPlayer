using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentIcons.Common;
using MediaPlayer.Controls;
using MediaPlayer.Controls.Audio;
using MediaPlayer.Controls.Workflows;
using MediaPlayer.Demo.Views;
using MediaPlayer.Demo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MediaPlayer.Demo;

public partial class MainWindow : Window
{
    private static readonly double[] PlaybackRateOptions = [0.5d, 0.75d, 1d, 1.25d, 1.5d, 2d];
    private static readonly double[] ExtendedPlaybackRateOptions = [0.5d, 0.75d, 1d, 1.25d, 1.5d, 2d, 3d, 4d];
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _overlayIdleTimer;
    private readonly DispatcherTimer _timelineSeekTimer;
    private readonly TimelineSeekController _timelineSeekController;
    private static readonly TimeSpan OverlayHideDelay = TimeSpan.FromSeconds(1.7);
    private static readonly TimeSpan TimelineSeekIntervalFast = TimeSpan.FromMilliseconds(24);
    private static readonly TimeSpan TimelineSeekIntervalSlow = TimeSpan.FromMilliseconds(60);
    private const double TimelineSeekMinDeltaFastSeconds = 0.03d;
    private const double TimelineSeekMinDeltaSlowSeconds = 0.08d;
    private bool _isTimelineUpdating;
    private bool _overlayVisible = true;
    private bool _isPointerOverHud;
    private bool _wasPlaying;
    private bool _alwaysShowControls;
    private readonly bool _isMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private readonly IMediaWorkflowService _mediaWorkflowService;
    private readonly IMediaWorkflowProviderDiagnostics _workflowProviderDiagnostics;
    private readonly PlaybackShortcutCommandService _playbackShortcutCommandService = new();
    private ExtendClientAreaChromeHints _defaultChromeHints;
    private DateTime _lastOverlayInteractionUtc = DateTime.UtcNow;
    private int _lastFittedVideoWidth;
    private int _lastFittedVideoHeight;
    private DateTime _lastFitAttemptUtc = DateTime.MinValue;
    private MediaAudioCapabilities _cachedAudioCapabilities = (MediaAudioCapabilities)(-1);
    private string _cachedAudioCapabilitiesText = "Unknown";
    private readonly MainWindowNativeMenuCoordinator _nativeMenuCoordinator;
    private readonly List<Uri> _recentSources = [];
    private const int MaxRecentSources = 10;
    private MenuTimeDisplayMode _timeDisplayMode = MenuTimeDisplayMode.Remaining;
    private MediaWorkflowQualityProfile _workflowQualityProfile = MediaWorkflowQualityProfile.Balanced;
    private MovieInspectorWindow? _movieInspectorWindow;

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    // Parameterless constructor is kept for XAML loader/designer compatibility.
    public MainWindow()
        : this(new MainWindowViewModel(), new FfmpegMediaWorkflowService(), null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public MainWindow(
        MainWindowViewModel viewModel,
        IMediaWorkflowService mediaWorkflowService,
        IMediaWorkflowProviderDiagnostics? workflowProviderDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(mediaWorkflowService);

        _mediaWorkflowService = mediaWorkflowService;
        _workflowProviderDiagnostics = workflowProviderDiagnostics ?? new NullWorkflowProviderDiagnostics();
        _nativeMenuCoordinator = new MainWindowNativeMenuCoordinator(
            this,
            CreateNativeMenuActions(),
            BuildDisplayTitle,
            () => _recentSources,
            () => Player.GetAudioTracks(),
            () => Player.GetSubtitleTracks(),
            ExtendedPlaybackRateOptions);

        InitializeComponent();
        TimelineSlider.AddHandler(
            InputElement.PointerPressedEvent,
            OnTimelinePointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        TimelineSlider.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTimelinePointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        TimelineSlider.AddHandler(
            InputElement.PointerCaptureLostEvent,
            OnTimelinePointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        DataContext = viewModel;
        _defaultChromeHints = ExtendClientAreaChromeHints;

        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => UpdateStatus());
        _overlayIdleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => HideOverlayOnIdle());
        _timelineSeekTimer = new DispatcherTimer(TimelineSeekIntervalFast, DispatcherPriority.Background, (_, _) => OnTimelineSeekTimerTick());
        _timelineSeekController = new TimelineSeekController(
            SeekFromTimeline,
            ClampTimelineSeconds,
            IsSlowSeekBackend,
            TimelineSeekIntervalFast,
            TimelineSeekIntervalSlow,
            TimelineSeekMinDeltaFastSeconds,
            TimelineSeekMinDeltaSlowSeconds);

        _statusTimer.Start();
        _overlayIdleTimer.Start();
        Closed += OnClosed;
        Deactivated += OnWindowDeactivated;
        AttachPointerWakeHandlers();

        LoadSource();
        _nativeMenuCoordinator.AttachIfSupported(CreateNativeMenuState());
        SetOverlayVisible(true);
        Player.PlaybackRate = 1d;
        Player.LayoutMode = VideoLayoutMode.Fit;
        UpdateControlGlyphs();
        UpdateTimeDisplay();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DetachPointerWakeHandlers();
        Deactivated -= OnWindowDeactivated;
        _statusTimer.Stop();
        _overlayIdleTimer.Stop();
        _timelineSeekTimer.Stop();
        if (_movieInspectorWindow is not null)
        {
            _movieInspectorWindow.Close();
            _movieInspectorWindow = null;
        }

        Player.Dispose();
    }

    private async void OnOpenFileClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select media file",
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            FileTypeFilter =
            [
                new FilePickerFileType("Media files")
                {
                    Patterns = new List<string> { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi", "*.mp3", "*.flac", "*.wav", "*.m3u8" }
                }
            ]
        });

        var selected = files.Count > 0 ? files[0] : null;
        if (selected is null)
        {
            return;
        }

        ViewModel.SourceText = selected.Path.LocalPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnPlayPauseClicked(object? sender, RoutedEventArgs e)
    {
        if (Player.IsPlaying)
        {
            Player.Pause();
            SetOverlayVisible(true);
            _lastOverlayInteractionUtc = DateTime.UtcNow;
        }
        else
        {
            Player.Play();
            ShowOverlayAndRestartIdleTimer();
        }

        UpdateControlGlyphs();
    }

    private void OnSeekBack10Clicked(object? sender, RoutedEventArgs e)
    {
        var target = Player.Position - TimeSpan.FromSeconds(10);
        Player.Seek(target > TimeSpan.Zero ? target : TimeSpan.Zero);
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnSeekForward10Clicked(object? sender, RoutedEventArgs e)
    {
        var max = Player.Duration > TimeSpan.Zero ? Player.Duration : TimeSpan.FromDays(365);
        var target = Player.Position + TimeSpan.FromSeconds(10);
        Player.Seek(target < max ? target : max);
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnToggleMuteClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsMuted = !ViewModel.IsMuted;
        if (!ViewModel.IsMuted && ViewModel.Volume < 1)
        {
            ViewModel.Volume = 60;
        }

        UpdateControlGlyphs();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnToggleLoopClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsLooping = !ViewModel.IsLooping;
        UpdateControlGlyphs();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnToggleAutoPlayClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.AutoPlay = !ViewModel.AutoPlay;
        UpdateControlGlyphs();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnToggleFullScreenClicked(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        UpdateTrafficLightsVisibility(_overlayVisible);
        UpdateControlGlyphs();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnCyclePlaybackRateClicked(object? sender, RoutedEventArgs e)
    {
        ApplyPlaybackRate(GetNextPlaybackRate(Player.PlaybackRate));
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnToggleAlwaysShowControlsClicked(object? sender, EventArgs e)
    {
        _alwaysShowControls = !_alwaysShowControls;
        if (_alwaysShowControls)
        {
            SetOverlayVisible(true);
            _lastOverlayInteractionUtc = DateTime.UtcNow;
        }

        UpdateControlGlyphs();
    }

    private void LoadSource()
    {
        if (!TryCreateMediaUri(ViewModel.SourceText, out var source, out var error))
        {
            ViewModel.Status = error;
            return;
        }

        ViewModel.SourceUri = source;
        ViewModel.DisplayTitle = BuildDisplayTitle(source);
        Title = ViewModel.DisplayTitle;
        AddRecentSource(source);
        _nativeMenuCoordinator.MarkTrackMenusDirty();
        _lastFittedVideoWidth = 0;
        _lastFittedVideoHeight = 0;
        _lastFitAttemptUtc = DateTime.MinValue;
        ViewModel.Status = $"Loaded {source}";
    }

    private void UpdateStatus()
    {
        var state = Player.IsPlaying ? "Playing" : "Paused/Stopped";
        var position = Player.Position.ToString(@"hh\:mm\:ss");
        var duration = Player.Duration.ToString(@"hh\:mm\:ss");
        var rendererPref = RendererPreferenceState.ToDisplayName(RendererPreferenceState.EffectivePreference);
        var runtimeRenderer = RendererPreferenceState.ToDisplayName(RendererPreferenceState.RuntimePreference);
        var rendererStatus = rendererPref == runtimeRenderer
            ? runtimeRenderer
            : $"{rendererPref}->{runtimeRenderer}";
        var workflowProvider = _workflowProviderDiagnostics.Current.ActiveProvider;
        var audioCapabilities = GetCachedAudioCapabilitiesText();
        ViewModel.Status = $"{state} | {position} / {duration} | {Player.PlaybackRate:0.##}x | Audio: {audioCapabilities} | Renderer: {rendererStatus} | Native: {Player.ActiveNativePlaybackProvider}/{workflowProvider}";

        if (Player.IsPlaying != _wasPlaying)
        {
            _wasPlaying = Player.IsPlaying;
            if (_wasPlaying)
            {
                ShowOverlayAndRestartIdleTimer();
            }
            else
            {
                SetOverlayVisible(true);
                _lastOverlayInteractionUtc = DateTime.UtcNow;
            }

            UpdateControlGlyphs();
        }

        if (!_isTimelineUpdating)
        {
            var durationSeconds = Math.Max(1d, Player.Duration.TotalSeconds);
            var positionSeconds = Math.Clamp(Player.Position.TotalSeconds, 0d, durationSeconds);

            _isTimelineUpdating = true;
            TimelineSlider.Maximum = durationSeconds;
            if (!_timelineSeekController.IsDragging)
            {
                TimelineSlider.Value = positionSeconds;
            }
            _isTimelineUpdating = false;
        }

        TryFitWindowToVideo(Player.VideoWidth, Player.VideoHeight);
        UpdateTimeDisplay();
    }

    private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        if (!e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _timelineSeekController.BeginDrag();
        SetOverlayVisible(true);
        _lastOverlayInteractionUtc = DateTime.UtcNow;
        _timelineSeekTimer.Interval = _timelineSeekController.CurrentInterval;
        try
        {
            e.Pointer.Capture(slider);
        }
        catch
        {
            // Best effort.
        }
    }

    private void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndTimelineDrag(commitSeek: true, e.Pointer);
    }

    private void OnTimelinePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndTimelineDrag(commitSeek: true, e.Pointer);
    }

    private void OnTimelineValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isTimelineUpdating || sender is not Slider slider)
        {
            return;
        }

        var clamped = ClampTimelineSeconds(slider.Value);
        ViewModel.SeekSeconds = clamped;
        if (_timelineSeekController.IsDragging)
        {
            _timelineSeekController.Submit(clamped);
            _timelineSeekTimer.Interval = _timelineSeekController.CurrentInterval;
            if (_timelineSeekController.HasPendingSeek)
            {
                if (!_timelineSeekTimer.IsEnabled)
                {
                    _timelineSeekTimer.Start();
                }
            }
            else if (_timelineSeekTimer.IsEnabled)
            {
                _timelineSeekTimer.Stop();
            }
        }
        else
        {
            _timelineSeekController.Submit(clamped);
        }
    }

    private void SeekFromTimeline(double seconds)
    {
        var clamped = ClampTimelineSeconds(seconds);
        ViewModel.SeekSeconds = clamped;
        Player.Seek(TimeSpan.FromSeconds(clamped));
    }

    private double ClampTimelineSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return 0d;
        }

        var durationSeconds = Player.Duration.TotalSeconds;
        var upperBound = durationSeconds > 0d ? durationSeconds : Math.Max(0d, seconds);
        return Math.Clamp(seconds, 0d, upperBound);
    }

    private void OnTimelineSeekTimerTick()
    {
        if (!_timelineSeekController.IsDragging)
        {
            _timelineSeekTimer.Stop();
            return;
        }

        _timelineSeekTimer.Interval = _timelineSeekController.CurrentInterval;
        if (!_timelineSeekController.HasPendingSeek)
        {
            _timelineSeekTimer.Stop();
            return;
        }

        if (_timelineSeekController.FlushPending())
        {
            ShowOverlayAndRestartIdleTimer();
        }
    }

    private bool IsSlowSeekBackend()
    {
        var renderPath = Player.ActiveRenderPath;
        var decodeApi = Player.ActiveDecodeApi;
        return renderPath.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)
            || decodeApi.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase);
    }

    private void TryFitWindowToVideo(int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || WindowState != WindowState.Normal)
        {
            return;
        }

        if (videoWidth == _lastFittedVideoWidth && videoHeight == _lastFittedVideoHeight)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastFitAttemptUtc < TimeSpan.FromMilliseconds(140))
        {
            return;
        }

        _lastFitAttemptUtc = now;

        var screens = Screens;
        if (screens is null)
        {
            return;
        }

        var screen = screens.ScreenFromWindow(this) ?? screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
        var maxWidth = Math.Max(MinWidth, (screen.WorkingArea.Width / scaling) - 72d);
        var maxHeight = Math.Max(MinHeight, (screen.WorkingArea.Height / scaling) - 96d);
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            return;
        }

        var fitScale = Math.Min(maxWidth / videoWidth, maxHeight / videoHeight);
        if (double.IsNaN(fitScale) || double.IsInfinity(fitScale) || fitScale <= 0d)
        {
            return;
        }

        var targetWidth = Math.Clamp(videoWidth * fitScale, MinWidth, maxWidth);
        var targetHeight = Math.Clamp(videoHeight * fitScale, MinHeight, maxHeight);

        var currentWidth = double.IsNaN(Width) || Width <= 0 ? Bounds.Width : Width;
        var currentHeight = double.IsNaN(Height) || Height <= 0 ? Bounds.Height : Height;
        if (Math.Abs(currentWidth - targetWidth) <= 1d && Math.Abs(currentHeight - targetHeight) <= 1d)
        {
            _lastFittedVideoWidth = videoWidth;
            _lastFittedVideoHeight = videoHeight;
            return;
        }

        Width = Math.Round(targetWidth);
        Height = Math.Round(targetHeight);
        _lastFittedVideoWidth = videoWidth;
        _lastFittedVideoHeight = videoHeight;
    }

    private void OnPlayerSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnPlayerSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnPlayerSurfacePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnHudPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOverHud = true;
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnHudPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOverHud = false;
        _lastOverlayInteractionUtc = DateTime.UtcNow;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var shortcutContext = new PlaybackShortcutContext(
            IsPlaying: Player.IsPlaying,
            IsFullscreen: WindowState == WindowState.FullScreen,
            IsMacOs: _isMacOs);
        if (!_playbackShortcutCommandService.TryResolve(e.Key, e.KeyModifiers, shortcutContext, out var command))
        {
            return;
        }

        // Mark handled before awaiting to prevent key bubbling/residual default handling.
        e.Handled = true;
        await ExecutePlaybackShortcutAsync(command);
    }

    private void HideOverlayOnIdle()
    {
        if (_alwaysShowControls || _timelineSeekController.IsDragging || _isPointerOverHud || !CanAutoHideControls())
        {
            return;
        }

        if (!_overlayVisible)
        {
            return;
        }

        if (DateTime.UtcNow - _lastOverlayInteractionUtc < OverlayHideDelay)
        {
            return;
        }

        SetOverlayVisible(false);
    }

    private void ShowOverlayAndRestartIdleTimer()
    {
        _lastOverlayInteractionUtc = DateTime.UtcNow;
        SetOverlayVisible(true);
    }

    private void SetOverlayVisible(bool visible)
    {
        if (_overlayVisible == visible)
        {
            return;
        }

        _overlayVisible = visible;
        OverlayChrome.Opacity = visible ? 1d : 0d;
        OverlayChrome.IsHitTestVisible = visible;
        UpdateTrafficLightsVisibility(visible);
    }

    private void UpdateTrafficLightsVisibility(bool hudVisible)
    {
        if (!_isMacOs || WindowState == WindowState.FullScreen)
        {
            return;
        }

        var targetHints = hudVisible
            ? _defaultChromeHints
            : ExtendClientAreaChromeHints.NoChrome;

        if (ExtendClientAreaChromeHints != targetHints)
        {
            ExtendClientAreaChromeHints = targetHints;
        }
    }

    private void UpdateControlGlyphs()
    {
        PlayPauseIcon.Symbol = Player.IsPlaying ? Symbol.Pause : Symbol.Play;
        VolumeIcon.Symbol = ViewModel.IsMuted || ViewModel.Volume < 1 ? Symbol.SpeakerMute : Symbol.Speaker2;
        LoopIcon.Foreground = ViewModel.IsLooping ? Brushes.White : new SolidColorBrush(Color.FromArgb(168, 255, 255, 255));
        FullScreenIcon.Symbol = WindowState == WindowState.FullScreen ? Symbol.FullScreenMinimize : Symbol.FullScreenMaximize;
        SpeedButton.Content = $"{Player.PlaybackRate:0.##}x";
        UpdateNativeMenuState();
    }

    private bool CanAutoHideControls()
    {
        return Player.IsPlaying || Player.Position > TimeSpan.Zero || Player.Duration > TimeSpan.Zero;
    }

    private async Task ExecutePlaybackShortcutAsync(PlaybackShortcutCommand command)
    {
        switch (command)
        {
            case PlaybackShortcutCommand.TogglePlayPause:
                OnPlayPauseClicked(this, new RoutedEventArgs());
                break;
            case PlaybackShortcutCommand.SeekToStart:
                Player.Seek(TimeSpan.Zero);
                ShowOverlayAndRestartIdleTimer();
                break;
            case PlaybackShortcutCommand.SeekToEnd:
                if (Player.Duration > TimeSpan.Zero)
                {
                    Player.Seek(Player.Duration);
                }

                ShowOverlayAndRestartIdleTimer();
                break;
            case PlaybackShortcutCommand.SeekBackward:
                OnSeekBack10Clicked(this, new RoutedEventArgs());
                break;
            case PlaybackShortcutCommand.SeekForward:
                OnSeekForward10Clicked(this, new RoutedEventArgs());
                break;
            case PlaybackShortcutCommand.StepFrameBackward:
                StepFrame(-1);
                break;
            case PlaybackShortcutCommand.StepFrameForward:
                StepFrame(1);
                break;
            case PlaybackShortcutCommand.ToggleMute:
                OnToggleMuteClicked(this, new RoutedEventArgs());
                break;
            case PlaybackShortcutCommand.ToggleLoop:
                OnToggleLoopClicked(this, new RoutedEventArgs());
                break;
            case PlaybackShortcutCommand.ToggleAutoPlay:
                OnToggleAutoPlayClicked(this, new RoutedEventArgs());
                break;
            case PlaybackShortcutCommand.ToggleFullScreen:
                OnToggleFullScreenClicked(this, new RoutedEventArgs());
                break;
            case PlaybackShortcutCommand.ExitFullScreen:
                WindowState = WindowState.Normal;
                UpdateTrafficLightsVisibility(_overlayVisible);
                UpdateControlGlyphs();
                break;
            case PlaybackShortcutCommand.IncreaseVolume:
                ViewModel.Volume = Math.Clamp(ViewModel.Volume + 5, 0, 100);
                if (ViewModel.Volume > 0)
                {
                    ViewModel.IsMuted = false;
                }

                UpdateControlGlyphs();
                break;
            case PlaybackShortcutCommand.DecreaseVolume:
                ViewModel.Volume = Math.Clamp(ViewModel.Volume - 5, 0, 100);
                if (ViewModel.Volume <= 0)
                {
                    ViewModel.IsMuted = true;
                }

                UpdateControlGlyphs();
                break;
            case PlaybackShortcutCommand.OpenFile:
                await Dispatcher.UIThread.InvokeAsync(() => OnOpenFileClicked(this, new RoutedEventArgs()));
                break;
            case PlaybackShortcutCommand.Trim:
                await TrimCurrentMediaAsync();
                break;
            case PlaybackShortcutCommand.Split:
                await SplitCurrentMediaAsync();
                break;
            case PlaybackShortcutCommand.Export1080p:
                await ExportCurrentMediaAsync(MediaExportPreset.Video1080p);
                break;
            case PlaybackShortcutCommand.ActualSize:
                OnActualSizeClicked(this, EventArgs.Empty);
                break;
            case PlaybackShortcutCommand.FitToScreen:
                OnFitToScreenClicked(this, EventArgs.Empty);
                break;
            case PlaybackShortcutCommand.FillMode:
                OnFillModeClicked(this, EventArgs.Empty);
                break;
            case PlaybackShortcutCommand.PanoramicMode:
                OnPanoramicModeClicked(this, EventArgs.Empty);
                break;
            case PlaybackShortcutCommand.GoToTime:
                await GoToTimeAsync();
                break;
            case PlaybackShortcutCommand.GoToFrame:
                await GoToFrameAsync();
                break;
            case PlaybackShortcutCommand.ShowMovieInspector:
                await ShowMovieInspectorAsync();
                break;
            case PlaybackShortcutCommand.DecreasePlaybackRate:
                ApplyPlaybackRate(GetAdjacentPlaybackRate(Player.PlaybackRate, -1));
                break;
            case PlaybackShortcutCommand.IncreasePlaybackRate:
                ApplyPlaybackRate(GetAdjacentPlaybackRate(Player.PlaybackRate, 1));
                break;
            case PlaybackShortcutCommand.ResetPlaybackRate:
                ApplyPlaybackRate(1d);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private void AttachPointerWakeHandlers()
    {
        AddHandler(InputElement.PointerMovedEvent, OnGlobalPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerReleasedEvent, OnGlobalPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerWheelChangedEvent, OnGlobalPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerEnteredEvent, OnGlobalPointerEntered, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
    }

    private void DetachPointerWakeHandlers()
    {
        RemoveHandler(InputElement.PointerMovedEvent, OnGlobalPointerMoved);
        RemoveHandler(InputElement.PointerPressedEvent, OnGlobalPointerPressed);
        RemoveHandler(InputElement.PointerReleasedEvent, OnGlobalPointerReleased);
        RemoveHandler(InputElement.PointerWheelChangedEvent, OnGlobalPointerWheelChanged);
        RemoveHandler(InputElement.PointerEnteredEvent, OnGlobalPointerEntered);
    }

    private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnGlobalPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnGlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
        if (_timelineSeekController.IsDragging && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndTimelineDrag(commitSeek: true, e.Pointer);
        }
    }

    private void OnGlobalPointerEntered(object? sender, PointerEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        EndTimelineDrag(commitSeek: true, null);
    }

    private void EndTimelineDrag(bool commitSeek, IPointer? pointer)
    {
        if (!_timelineSeekController.IsDragging)
        {
            return;
        }

        _timelineSeekController.EndDrag(TimelineSlider.Value, commitSeek);
        _timelineSeekTimer.Stop();

        try
        {
            pointer?.Capture(null);
        }
        catch
        {
            // Best effort.
        }

        ShowOverlayAndRestartIdleTimer();
    }

    private void OnTimeDisplayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _timeDisplayMode = _timeDisplayMode switch
        {
            MenuTimeDisplayMode.Remaining => MenuTimeDisplayMode.Elapsed,
            MenuTimeDisplayMode.Elapsed => MenuTimeDisplayMode.Timecode,
            MenuTimeDisplayMode.Timecode => MenuTimeDisplayMode.FrameCount,
            _ => MenuTimeDisplayMode.Remaining
        };

        UpdateTimeDisplay();
        UpdateNativeMenuState();
    }

    private void AddRecentSource(Uri source)
    {
        _recentSources.RemoveAll(candidate => candidate == source);
        _recentSources.Insert(0, source);
        if (_recentSources.Count > MaxRecentSources)
        {
            _recentSources.RemoveRange(MaxRecentSources, _recentSources.Count - MaxRecentSources);
        }

        _nativeMenuCoordinator.MarkRecentDirty();
    }

    private void UpdateTimeDisplay()
    {
        var position = Player.Position;
        var duration = Player.Duration;
        var frameRate = GetEffectiveFrameRate();
        PositionDisplayText.Text = FormatPrimaryTime(position, duration, frameRate);
        DurationDisplayText.Text = FormatSecondaryTime(duration, frameRate);
    }

    private string FormatPrimaryTime(TimeSpan position, TimeSpan duration, double frameRate)
    {
        return _timeDisplayMode switch
        {
            MenuTimeDisplayMode.Elapsed => position.ToString(@"hh\:mm\:ss"),
            MenuTimeDisplayMode.Timecode => ToTimecode(position, frameRate),
            MenuTimeDisplayMode.FrameCount => $"F{Math.Max(0, (long)Math.Round(position.TotalSeconds * frameRate))}",
            _ => $"-{(duration > TimeSpan.Zero ? duration - position : TimeSpan.Zero):hh\\:mm\\:ss}"
        };
    }

    private string FormatSecondaryTime(TimeSpan duration, double frameRate)
    {
        return _timeDisplayMode switch
        {
            MenuTimeDisplayMode.Timecode => ToTimecode(duration, frameRate),
            MenuTimeDisplayMode.FrameCount => $"F{Math.Max(0, (long)Math.Round(duration.TotalSeconds * frameRate))}",
            _ => duration.ToString(@"hh\:mm\:ss")
        };
    }

    private double GetEffectiveFrameRate()
    {
        var fps = Player.FrameRate;
        if (double.IsNaN(fps) || double.IsInfinity(fps) || fps <= 0.1d)
        {
            return 30d;
        }

        return Math.Clamp(fps, 1d, 240d);
    }

    private static string ToTimecode(TimeSpan value, double frameRate)
    {
        var fps = Math.Max(1d, frameRate);
        var totalSeconds = Math.Max(0d, value.TotalSeconds);
        var wholeSeconds = (int)Math.Floor(totalSeconds);
        var frames = (int)Math.Round((totalSeconds - wholeSeconds) * fps);
        var framesPerSecond = Math.Max(1, (int)Math.Round(fps));
        if (frames >= framesPerSecond)
        {
            frames = 0;
            wholeSeconds++;
        }

        var hh = wholeSeconds / 3600;
        var mm = (wholeSeconds % 3600) / 60;
        var ss = wholeSeconds % 60;
        return $"{hh:00}:{mm:00}:{ss:00}:{frames:00}";
    }

    private void StepFrame(int direction)
    {
        var fps = GetEffectiveFrameRate();
        var step = TimeSpan.FromSeconds(1d / fps);
        var target = Player.Position + TimeSpan.FromTicks(step.Ticks * direction);
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        if (Player.Duration > TimeSpan.Zero && target > Player.Duration)
        {
            target = Player.Duration;
        }

        Player.Seek(target);
        ShowOverlayAndRestartIdleTimer();
    }

    private void ApplyPlaybackRate(double rate)
    {
        var clamped = Math.Clamp(rate, 0.1d, 16d);
        Player.PlaybackRate = clamped;
        SpeedButton.Content = $"{clamped:0.##}x";
        ViewModel.Status = $"Playback speed: {clamped:0.##}x";
        UpdateNativeMenuState();
        ShowOverlayAndRestartIdleTimer();
    }

    private static double GetNextPlaybackRate(double currentRate)
    {
        foreach (var rate in PlaybackRateOptions)
        {
            if (currentRate < rate - 0.001d)
            {
                return rate;
            }
        }

        return PlaybackRateOptions[0];
    }

    private static double GetAdjacentPlaybackRate(double currentRate, int direction)
    {
        var options = ExtendedPlaybackRateOptions;
        var closestIndex = 0;
        var closestDistance = double.MaxValue;
        for (var i = 0; i < options.Length; i++)
        {
            var distance = Math.Abs(options[i] - currentRate);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        var target = Math.Clamp(closestIndex + direction, 0, options.Length - 1);
        return options[target];
    }

    private async Task GoToTimeAsync()
    {
        var input = await PromptForInputAsync("Go To Time", "hh:mm:ss(.ff) or seconds", Player.Position.ToString(@"hh\:mm\:ss"));
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (!TryParseGoToTime(input, out var target))
        {
            ViewModel.Status = "Invalid time format.";
            return;
        }

        if (Player.Duration > TimeSpan.Zero && target > Player.Duration)
        {
            target = Player.Duration;
        }

        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        Player.Seek(target);
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task GoToFrameAsync()
    {
        var fps = GetEffectiveFrameRate();
        var currentFrame = (long)Math.Round(Player.Position.TotalSeconds * fps);
        var input = await PromptForInputAsync("Go To Frame", "Frame number", currentFrame.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (!long.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame))
        {
            ViewModel.Status = "Invalid frame number.";
            return;
        }

        frame = Math.Max(0, frame);
        var target = TimeSpan.FromSeconds(frame / fps);
        if (Player.Duration > TimeSpan.Zero && target > Player.Duration)
        {
            target = Player.Duration;
        }

        Player.Seek(target);
        ShowOverlayAndRestartIdleTimer();
    }

    private static bool TryParseGoToTime(string input, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds))
        {
            value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
            return true;
        }

        if (TimeSpan.TryParseExact(trimmed, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out value)
            || TimeSpan.TryParseExact(trimmed, @"h\:mm\:ss", CultureInfo.InvariantCulture, out value)
            || TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out value))
        {
            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            return true;
        }

        return false;
    }

    private static string BuildDisplayTitle(Uri source)
    {
        if (source.IsFile)
        {
            var local = Path.GetFileName(source.LocalPath);
            if (!string.IsNullOrWhiteSpace(local))
            {
                return local;
            }
        }

        var absolutePath = source.AbsolutePath;
        var segment = Path.GetFileName(Uri.UnescapeDataString(absolutePath));
        if (!string.IsNullOrWhiteSpace(segment))
        {
            return segment;
        }

        return source.Host;
    }

    private MainWindowNativeMenuActions CreateNativeMenuActions()
    {
        return new MainWindowNativeMenuActions
        {
            OnAboutClicked = OnAboutClicked,
            OnPreferencesClicked = OnPreferencesClicked,
            OnQuitClicked = OnQuitClicked,
            OnOpenFileClicked = OnOpenFileFromMenuClicked,
            OnOpenLocationClicked = OnOpenLocationClicked,
            OnNewScreenRecordingClicked = OnNewScreenRecordingFromMenuClicked,
            OnNewMovieRecordingClicked = OnNewMovieRecordingFromMenuClicked,
            OnNewAudioRecordingClicked = OnNewAudioRecordingFromMenuClicked,
            OnExport4KClicked = OnExport4KFromMenuClicked,
            OnExport1080pClicked = OnExport1080pFromMenuClicked,
            OnExport720pClicked = OnExport720pFromMenuClicked,
            OnExport480pClicked = OnExport480pFromMenuClicked,
            OnExportAudioOnlyClicked = OnExportAudioOnlyFromMenuClicked,
            OnWorkflowQualitySpeedClicked = OnWorkflowQualitySpeedFromMenuClicked,
            OnWorkflowQualityBalancedClicked = OnWorkflowQualityBalancedFromMenuClicked,
            OnWorkflowQualityQualityClicked = OnWorkflowQualityQualityFromMenuClicked,
            OnShareCurrentMediaClicked = OnShareCurrentMediaFromMenuClicked,
            OnPlayPauseClicked = OnPlayPauseFromMenuClicked,
            OnStopClicked = OnStopFromMenuClicked,
            OnCloseWindowClicked = OnCloseWindowClicked,
            OnNotImplementedClicked = OnNotImplementedMenuClicked,
            OnTrimClicked = OnTrimFromMenuClicked,
            OnSplitClipClicked = OnSplitClipFromMenuClicked,
            OnCombineClipsClicked = OnCombineClipsFromMenuClicked,
            OnRemoveAudioClicked = OnRemoveAudioFromMenuClicked,
            OnRemoveVideoClicked = OnRemoveVideoFromMenuClicked,
            OnRotateClockwiseClicked = OnRotateClockwiseFromMenuClicked,
            OnRotateCounterClockwiseClicked = OnRotateCounterClockwiseFromMenuClicked,
            OnFlipHorizontalClicked = OnFlipHorizontalFromMenuClicked,
            OnFlipVerticalClicked = OnFlipVerticalFromMenuClicked,
            OnSelectAllClicked = OnSelectAllMenuClicked,
            OnToggleMuteClicked = OnToggleMuteFromMenuClicked,
            OnToggleLoopClicked = OnToggleLoopFromMenuClicked,
            OnToggleAlwaysShowControlsClicked = OnToggleAlwaysShowControlsClicked,
            OnToggleFloatOnTopClicked = OnToggleFloatOnTopClicked,
            OnTimeDisplayRemainingClicked = OnTimeDisplayRemainingClicked,
            OnTimeDisplayElapsedClicked = OnTimeDisplayElapsedClicked,
            OnTimeDisplayTimecodeClicked = OnTimeDisplayTimecodeClicked,
            OnTimeDisplayFramesClicked = OnTimeDisplayFramesClicked,
            OnGoToTimeClicked = OnGoToTimeClicked,
            OnGoToFrameClicked = OnGoToFrameClicked,
            OnActualSizeClicked = OnActualSizeClicked,
            OnFitToScreenClicked = OnFitToScreenClicked,
            OnFillModeClicked = OnFillModeClicked,
            OnPanoramicModeClicked = OnPanoramicModeClicked,
            OnRendererAutoClicked = OnRendererAutoClicked,
            OnRendererOpenGlClicked = OnRendererOpenGlClicked,
            OnRendererVulkanClicked = OnRendererVulkanClicked,
            OnRendererMetalClicked = OnRendererMetalClicked,
            OnRendererSoftwareClicked = OnRendererSoftwareClicked,
            OnToggleFullScreenClicked = OnToggleFullScreenFromMenuClicked,
            OnShowMovieInspectorClicked = OnShowMovieInspectorClicked,
            OnMinimizeClicked = OnMinimizeClicked,
            OnZoomClicked = OnZoomClicked,
            OnBringAllToFrontClicked = OnBringAllToFrontClicked,
            OnHelpClicked = OnHelpClicked,
            OnClearRecentClicked = OnClearRecentClicked,
            OnOpenRecentSource = OpenRecentSource,
            OnAudioTrackSelected = OnAudioTrackSelected,
            OnSubtitleTrackSelected = OnSubtitleTrackSelected,
            OnPlaybackRateSelected = ApplyPlaybackRate
        };
    }

    private MainWindowNativeMenuState CreateNativeMenuState()
    {
        return new MainWindowNativeMenuState(
            IsPlaying: Player.IsPlaying,
            IsMuted: ViewModel.IsMuted || ViewModel.Volume <= 0,
            IsLooping: ViewModel.IsLooping,
            IsFullscreen: WindowState == WindowState.FullScreen,
            AlwaysShowControls: _alwaysShowControls,
            Topmost: Topmost,
            TimeDisplayMode: _timeDisplayMode,
            LayoutMode: Player.LayoutMode,
            PlaybackRate: Player.PlaybackRate,
            RendererPreference: RendererPreferenceState.EffectivePreference,
            WorkflowQualityProfile: _workflowQualityProfile);
    }

    private void OpenRecentSource(Uri source)
    {
        ViewModel.SourceText = source.IsFile ? source.LocalPath : source.ToString();
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnClearRecentClicked(object? sender, EventArgs e)
    {
        _recentSources.Clear();
        _nativeMenuCoordinator.MarkRecentDirty();
        UpdateNativeMenuState();
    }

    private void OnAudioTrackSelected(int trackId)
    {
        if (Player.SetAudioTrack(trackId))
        {
            ViewModel.Status = $"Audio track selected: {trackId}";
            _nativeMenuCoordinator.MarkTrackMenusDirty();
            UpdateNativeMenuState();
            ShowOverlayAndRestartIdleTimer();
            return;
        }

        ViewModel.Status = "Audio track change is unavailable on this backend or media source.";
    }

    private void OnSubtitleTrackSelected(int trackId)
    {
        if (Player.SetSubtitleTrack(trackId))
        {
            ViewModel.Status = trackId < 0
                ? "Subtitles disabled."
                : $"Subtitle track selected: {trackId}";
            _nativeMenuCoordinator.MarkTrackMenusDirty();
            UpdateNativeMenuState();
            ShowOverlayAndRestartIdleTimer();
            return;
        }

        ViewModel.Status = "Subtitle track change is unavailable on this backend or media source.";
    }

    private async void OnOpenLocationClicked(object? sender, EventArgs e)
    {
        var text = await PromptForInputAsync(
            "Open Location",
            "HTTP/HTTPS URL or file path",
            ViewModel.SourceText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ViewModel.SourceText = text;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async void OnTrimFromMenuClicked(object? sender, EventArgs e)
    {
        await TrimCurrentMediaAsync();
    }

    private async void OnSplitClipFromMenuClicked(object? sender, EventArgs e)
    {
        await SplitCurrentMediaAsync();
    }

    private async void OnCombineClipsFromMenuClicked(object? sender, EventArgs e)
    {
        await CombineClipsAsync();
    }

    private async void OnRemoveAudioFromMenuClicked(object? sender, EventArgs e)
    {
        await RemoveAudioFromCurrentMediaAsync();
    }

    private async void OnRemoveVideoFromMenuClicked(object? sender, EventArgs e)
    {
        await RemoveVideoFromCurrentMediaAsync();
    }

    private async void OnRotateClockwiseFromMenuClicked(object? sender, EventArgs e)
    {
        await TransformCurrentMediaAsync(MediaVideoTransform.Rotate90Clockwise, "Save Rotated Clip", "-rotated-cw", "Rotate clockwise");
    }

    private async void OnRotateCounterClockwiseFromMenuClicked(object? sender, EventArgs e)
    {
        await TransformCurrentMediaAsync(MediaVideoTransform.Rotate90CounterClockwise, "Save Rotated Clip", "-rotated-ccw", "Rotate counterclockwise");
    }

    private async void OnFlipHorizontalFromMenuClicked(object? sender, EventArgs e)
    {
        await TransformCurrentMediaAsync(MediaVideoTransform.FlipHorizontal, "Save Flipped Clip", "-flipped-h", "Flip horizontal");
    }

    private async void OnFlipVerticalFromMenuClicked(object? sender, EventArgs e)
    {
        await TransformCurrentMediaAsync(MediaVideoTransform.FlipVertical, "Save Flipped Clip", "-flipped-v", "Flip vertical");
    }

    private async void OnNewScreenRecordingFromMenuClicked(object? sender, EventArgs e)
    {
        await StartRecordingWorkflowAsync(MediaRecordingPreset.Screen);
    }

    private async void OnNewMovieRecordingFromMenuClicked(object? sender, EventArgs e)
    {
        await StartRecordingWorkflowAsync(MediaRecordingPreset.Movie);
    }

    private async void OnNewAudioRecordingFromMenuClicked(object? sender, EventArgs e)
    {
        await StartRecordingWorkflowAsync(MediaRecordingPreset.Audio);
    }

    private async void OnExport4KFromMenuClicked(object? sender, EventArgs e)
    {
        await ExportCurrentMediaAsync(MediaExportPreset.Video2160p);
    }

    private async void OnExport1080pFromMenuClicked(object? sender, EventArgs e)
    {
        await ExportCurrentMediaAsync(MediaExportPreset.Video1080p);
    }

    private async void OnExport720pFromMenuClicked(object? sender, EventArgs e)
    {
        await ExportCurrentMediaAsync(MediaExportPreset.Video720p);
    }

    private async void OnExport480pFromMenuClicked(object? sender, EventArgs e)
    {
        await ExportCurrentMediaAsync(MediaExportPreset.Video480p);
    }

    private async void OnExportAudioOnlyFromMenuClicked(object? sender, EventArgs e)
    {
        await ExportCurrentMediaAsync(MediaExportPreset.AudioOnly);
    }

    private async void OnShareCurrentMediaFromMenuClicked(object? sender, EventArgs e)
    {
        await ShareCurrentMediaAsync();
    }

    private void OnWorkflowQualitySpeedFromMenuClicked(object? sender, EventArgs e)
    {
        SetWorkflowQualityProfile(MediaWorkflowQualityProfile.Speed);
    }

    private void OnWorkflowQualityBalancedFromMenuClicked(object? sender, EventArgs e)
    {
        SetWorkflowQualityProfile(MediaWorkflowQualityProfile.Balanced);
    }

    private void OnWorkflowQualityQualityFromMenuClicked(object? sender, EventArgs e)
    {
        SetWorkflowQualityProfile(MediaWorkflowQualityProfile.Quality);
    }

    private async Task TrimCurrentMediaAsync()
    {
        var source = ViewModel.SourceUri;
        if (source is null)
        {
            ViewModel.Status = "No media loaded to trim.";
            return;
        }

        var current = Player.Position;
        var duration = Player.Duration > TimeSpan.Zero ? Player.Duration : TimeSpan.FromHours(1);
        var startInput = await PromptForInputAsync(
            "Trim Clip",
            "Start time (hh:mm:ss or seconds)",
            current.ToString(@"hh\:mm\:ss"));
        if (string.IsNullOrWhiteSpace(startInput))
        {
            return;
        }

        if (!TryParseGoToTime(startInput, out var startTime))
        {
            ViewModel.Status = "Invalid trim start time.";
            return;
        }

        var defaultEnd = startTime < duration
            ? duration
            : startTime + TimeSpan.FromSeconds(30);
        var endInput = await PromptForInputAsync(
            "Trim Clip",
            "End time (hh:mm:ss or seconds)",
            defaultEnd.ToString(@"hh\:mm\:ss"));
        if (string.IsNullOrWhiteSpace(endInput))
        {
            return;
        }

        if (!TryParseGoToTime(endInput, out var endTime))
        {
            ViewModel.Status = "Invalid trim end time.";
            return;
        }

        if (Player.Duration > TimeSpan.Zero && endTime > Player.Duration)
        {
            endTime = Player.Duration;
        }

        if (startTime < TimeSpan.Zero)
        {
            startTime = TimeSpan.Zero;
        }

        if (endTime <= startTime)
        {
            ViewModel.Status = "Trim end time must be greater than start time.";
            return;
        }

        var outputPath = await PromptForTrimOutputPathAsync(source);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        ViewModel.Status = $"Trimming clip: {startTime:hh\\:mm\\:ss} - {endTime:hh\\:mm\\:ss}";
        var trimResult = await _mediaWorkflowService.TrimAsync(source, startTime, endTime, outputPath);
        if (!trimResult.Success)
        {
            ViewModel.Status = $"Trim failed: {trimResult.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"Trim saved: {outputPath}";
        ViewModel.SourceText = outputPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task SplitCurrentMediaAsync()
    {
        var source = ViewModel.SourceUri;
        if (source is null)
        {
            ViewModel.Status = "No media loaded to split.";
            return;
        }

        if (Player.Duration <= TimeSpan.Zero)
        {
            ViewModel.Status = "Split requires known media duration.";
            return;
        }

        var splitInput = await PromptForInputAsync(
            "Split Clip",
            "Split time (hh:mm:ss or seconds)",
            Player.Position.ToString(@"hh\:mm\:ss"));
        if (string.IsNullOrWhiteSpace(splitInput))
        {
            return;
        }

        if (!TryParseGoToTime(splitInput, out var splitTime))
        {
            ViewModel.Status = "Invalid split time.";
            return;
        }

        splitTime = TimeSpan.FromMilliseconds(Math.Clamp(splitTime.TotalMilliseconds, 0d, Player.Duration.TotalMilliseconds));
        if (splitTime <= TimeSpan.Zero || splitTime >= Player.Duration)
        {
            ViewModel.Status = "Split time must be inside media duration.";
            return;
        }

        var partOnePath = await PromptForDerivedOutputPathAsync(source, "Save First Split Clip", "-part1");
        if (string.IsNullOrWhiteSpace(partOnePath))
        {
            return;
        }

        var partTwoPath = _mediaWorkflowService.BuildSiblingOutputPath(partOnePath, "-part2");
        ViewModel.Status = $"Splitting clip at {splitTime:hh\\:mm\\:ss}";

        var splitResult = await _mediaWorkflowService.SplitAsync(source, splitTime, Player.Duration, partOnePath, partTwoPath);
        if (!splitResult.Success)
        {
            ViewModel.Status = $"Split failed: {splitResult.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"Split saved: {partOnePath} and {partTwoPath}";
        ViewModel.SourceText = partOnePath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task CombineClipsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select clips to combine",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Media files")
                {
                    Patterns = new List<string> { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi", "*.m4v" }
                }
            ]
        });

        if (files.Count < 1)
        {
            ViewModel.Status = "Select at least one clip to combine.";
            return;
        }

        var inputs = new List<string>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var candidate = files[i].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                inputs.Add(candidate);
            }
        }

        if (inputs.Count < 1)
        {
            ViewModel.Status = "Only local clips can be combined in this workflow.";
            return;
        }

        var orderedInputs = await PromptForClipArrangementAsync(inputs);
        if (orderedInputs is null)
        {
            ViewModel.Status = "Combine canceled.";
            return;
        }

        if (orderedInputs.Count < 2)
        {
            ViewModel.Status = "Select at least two clips to combine.";
            return;
        }

        var sourceHint = new Uri(Path.GetFullPath(orderedInputs[0]));
        var outputPath = await PromptForDerivedOutputPathAsync(sourceHint, "Save Combined Clip", "-combined");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        ViewModel.Status = $"Combining {orderedInputs.Count} clips…";
        var combineResult = await _mediaWorkflowService.CombineAsync(orderedInputs, outputPath);
        if (!combineResult.Success)
        {
            ViewModel.Status = $"Combine failed: {combineResult.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"Combined clip saved: {outputPath}";
        ViewModel.SourceText = outputPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task<IReadOnlyList<string>?> PromptForClipArrangementAsync(IReadOnlyList<string> inputPaths)
    {
        var viewModel = new ClipArrangeWindowViewModel(inputPaths);
        if (viewModel.Clips.Count == 0)
        {
            return null;
        }

        viewModel.SelectedClip = viewModel.Clips[0];
        while (true)
        {
            var dialog = new ClipArrangeWindow
            {
                DataContext = viewModel
            };

            void OnCloseRequested(object? sender, EventArgs e) => dialog.Close(viewModel.DialogResult);
            viewModel.CloseRequested += OnCloseRequested;
            try
            {
                var dialogResult = await dialog.ShowDialog<bool?>(this);
                if (dialogResult == true)
                {
                    return viewModel.BuildOrderedPaths();
                }

                if (dialogResult == false)
                {
                    return null;
                }

                if (!viewModel.TryConsumeInsertionRequest(out var insertionMode))
                {
                    return null;
                }

                var additionalClips = await PromptForAdditionalArrangeClipsAsync(insertionMode);
                if (additionalClips.Count == 0)
                {
                    ViewModel.Status = "No additional clips were selected.";
                    continue;
                }

                viewModel.InsertClips(additionalClips, insertionMode);
                ViewModel.Status = $"{additionalClips.Count} clip(s) added to arrangement.";
            }
            finally
            {
                viewModel.CloseRequested -= OnCloseRequested;
            }
        }
    }

    private async Task<IReadOnlyList<string>> PromptForAdditionalArrangeClipsAsync(ClipArrangeInsertionMode insertionMode)
    {
        var title = insertionMode == ClipArrangeInsertionMode.InsertBeforeSelection
            ? "Select clips to insert before selected clip"
            : "Select clips to append";
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Media files")
                {
                    Patterns = new List<string> { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi", "*.m4v" }
                }
            ]
        });

        var clipPaths = new List<string>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            string candidate = files[i].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                clipPaths.Add(candidate);
            }
        }

        return clipPaths;
    }

    private async Task RemoveAudioFromCurrentMediaAsync()
    {
        var source = ViewModel.SourceUri;
        if (source is null)
        {
            ViewModel.Status = "No media loaded.";
            return;
        }

        var outputPath = await PromptForDerivedOutputPathAsync(source, "Save Video Without Audio", "-no-audio");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        ViewModel.Status = "Removing audio track…";
        var result = await _mediaWorkflowService.RemoveAudioAsync(source, outputPath);
        if (!result.Success)
        {
            ViewModel.Status = $"Remove audio failed: {result.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"Saved video without audio: {outputPath}";
        ViewModel.SourceText = outputPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task RemoveVideoFromCurrentMediaAsync()
    {
        var source = ViewModel.SourceUri;
        if (source is null)
        {
            ViewModel.Status = "No media loaded.";
            return;
        }

        var outputPath = await PromptForDerivedOutputPathAsync(source, "Save Audio Only", "-audio-only");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        ViewModel.Status = "Extracting audio track…";
        var result = await _mediaWorkflowService.RemoveVideoAsync(source, outputPath);
        if (!result.Success)
        {
            ViewModel.Status = $"Extract audio failed: {result.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"Saved audio-only media: {outputPath}";
        ViewModel.SourceText = outputPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task TransformCurrentMediaAsync(
        MediaVideoTransform transform,
        string saveTitle,
        string outputSuffix,
        string operationDisplayName)
    {
        var source = ViewModel.SourceUri;
        if (source is null)
        {
            ViewModel.Status = "No media loaded.";
            return;
        }

        var outputPath = await PromptForDerivedOutputPathAsync(source, saveTitle, outputSuffix);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        ViewModel.Status = $"{operationDisplayName}…";
        var result = await _mediaWorkflowService.TransformAsync(source, outputPath, transform);
        if (!result.Success)
        {
            ViewModel.Status = $"{operationDisplayName} failed: {result.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"{operationDisplayName} complete: {outputPath}";
        ViewModel.SourceText = outputPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task StartRecordingWorkflowAsync(MediaRecordingPreset preset)
    {
        var presetName = _mediaWorkflowService.GetRecordingPresetDisplayName(preset);
        var qualityProfile = await ResolveWorkflowQualityProfileForOperationAsync("Recording");
        if (qualityProfile is null)
        {
            return;
        }

        var qualityLabel = _mediaWorkflowService.GetQualityProfileDisplayName(qualityProfile.Value);
        var defaultDurationSeconds = preset == MediaRecordingPreset.Audio ? 30d : 10d;
        var durationInput = await PromptForInputAsync(
            presetName,
            "Duration in seconds (1-3600)",
            defaultDurationSeconds.ToString("0", CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(durationInput))
        {
            return;
        }

        if (!double.TryParse(durationInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds)
            || durationSeconds <= 0d)
        {
            ViewModel.Status = "Invalid recording duration.";
            return;
        }

        durationSeconds = Math.Clamp(durationSeconds, 1d, 3600d);
        var outputPath = await PromptForRecordingOutputPathAsync(preset);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var duration = TimeSpan.FromSeconds(durationSeconds);
        var routeState = Player.GetAudioRouteState();
        var recordingOptions = new MediaRecordingOptions(
            qualityProfile,
            routeState.SelectedInputDeviceId,
            routeState.SelectedOutputDeviceId,
            EnableSystemLoopback: false,
            EnableAcousticEchoCancellation: false,
            EnableNoiseSuppression: false,
            TargetAudioFormat: default);
        ViewModel.Status = $"Recording {presetName} ({qualityLabel}) for {duration.TotalSeconds:0.#}s…";
        var result = await _mediaWorkflowService.RecordAsync(preset, outputPath, duration, recordingOptions);
        if (!result.Success)
        {
            ViewModel.Status = $"Recording failed: {result.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"Recording saved: {outputPath}";
        ViewModel.SourceText = outputPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task<string?> PromptForRecordingOutputPathAsync(MediaRecordingPreset preset)
    {
        var suggestedName = _mediaWorkflowService.GetSuggestedRecordingFileName(preset, DateTime.Now);
        var fileTypeChoices = preset == MediaRecordingPreset.Audio
            ? new List<FilePickerFileType>
            {
                new("AAC Audio")
                {
                    Patterns = ["*.m4a"]
                }
            }
            : new List<FilePickerFileType>
            {
                new("MPEG-4")
                {
                    Patterns = ["*.mp4"]
                },
                new("QuickTime Movie")
                {
                    Patterns = ["*.mov"]
                }
            };

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save {_mediaWorkflowService.GetRecordingPresetDisplayName(preset)}",
            SuggestedFileName = suggestedName,
            FileTypeChoices = fileTypeChoices
        });

        return saveFile?.Path.LocalPath;
    }

    private async Task ExportCurrentMediaAsync(MediaExportPreset preset)
    {
        var source = ViewModel.SourceUri;
        if (source is null)
        {
            ViewModel.Status = "No media loaded to export.";
            return;
        }

        var qualityProfile = await ResolveWorkflowQualityProfileForOperationAsync("Export");
        if (qualityProfile is null)
        {
            return;
        }

        var outputPath = await PromptForExportOutputPathAsync(source, preset);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var presetName = _mediaWorkflowService.GetExportPresetDisplayName(preset);
        var qualityLabel = _mediaWorkflowService.GetQualityProfileDisplayName(qualityProfile.Value);
        var exportOptions = MediaExportOptions.FromQualityProfile(qualityProfile.Value);
        ViewModel.Status = $"Exporting ({presetName}, {qualityLabel})…";
        var result = await _mediaWorkflowService.ExportAsync(source, outputPath, preset, exportOptions);
        if (!result.Success)
        {
            ViewModel.Status = $"Export failed: {result.ErrorMessage}";
            return;
        }

        ViewModel.Status = $"Export saved ({presetName}, {qualityLabel}): {outputPath}";
        ViewModel.SourceText = outputPath;
        LoadSource();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task ShareCurrentMediaAsync()
    {
        var source = ViewModel.SourceUri;
        if (source is null)
        {
            ViewModel.Status = "No media loaded to share.";
            return;
        }

        if (source.IsFile && !File.Exists(source.LocalPath))
        {
            ViewModel.Status = "Cannot share media: source file does not exist.";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            ViewModel.Status = "Cannot share media: top-level launcher is unavailable.";
            return;
        }

        try
        {
            await topLevel.Launcher.LaunchUriAsync(source);
            ViewModel.Status = source.IsFile
                ? $"Shared media in default app: {source.LocalPath}"
                : $"Opened media link for sharing: {source}";
            ShowOverlayAndRestartIdleTimer();
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"Share failed: {ex.Message}";
        }
    }

    private async Task<string?> PromptForExportOutputPathAsync(Uri source, MediaExportPreset preset)
    {
        var suggestedName = _mediaWorkflowService.GetSuggestedExportFileName(source, preset);
        var fileTypeChoices = preset == MediaExportPreset.AudioOnly
            ? new List<FilePickerFileType>
            {
                new("AAC Audio")
                {
                    Patterns = ["*.m4a"]
                }
            }
            : new List<FilePickerFileType>
            {
                new("MPEG-4")
                {
                    Patterns = ["*.mp4"]
                },
                new("QuickTime Movie")
                {
                    Patterns = ["*.mov"]
                },
                new("Matroska")
                {
                    Patterns = ["*.mkv"]
                }
            };

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export As {_mediaWorkflowService.GetExportPresetDisplayName(preset)}",
            SuggestedFileName = suggestedName,
            FileTypeChoices = fileTypeChoices
        });

        return saveFile?.Path.LocalPath;
    }

    private async Task<string?> PromptForTrimOutputPathAsync(Uri source)
    {
        return await PromptForDerivedOutputPathAsync(source, "Save Trimmed Clip", "-trimmed");
    }

    private async Task<string?> PromptForDerivedOutputPathAsync(Uri source, string title, string suffix)
    {
        var sourcePath = source.IsFile ? source.LocalPath : source.AbsolutePath;
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp4";
        }

        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "media";
        }

        var suggestedName = $"{sourceName}{suffix}{extension}";
        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType("MPEG-4")
                {
                    Patterns = ["*.mp4"]
                },
                new FilePickerFileType("QuickTime Movie")
                {
                    Patterns = ["*.mov"]
                },
                new FilePickerFileType("Matroska")
                {
                    Patterns = ["*.mkv"]
                },
                new FilePickerFileType("WebM")
                {
                    Patterns = ["*.webm"]
                }
            ]
        });

        return saveFile?.Path.LocalPath;
    }

    private void OnStopFromMenuClicked(object? sender, EventArgs e)
    {
        Player.Stop();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnOpenFileFromMenuClicked(object? sender, EventArgs e)
    {
        OnOpenFileClicked(this, new RoutedEventArgs());
    }

    private void OnPlayPauseFromMenuClicked(object? sender, EventArgs e)
    {
        OnPlayPauseClicked(this, new RoutedEventArgs());
    }

    private void OnToggleMuteFromMenuClicked(object? sender, EventArgs e)
    {
        OnToggleMuteClicked(this, new RoutedEventArgs());
    }

    private void OnToggleLoopFromMenuClicked(object? sender, EventArgs e)
    {
        OnToggleLoopClicked(this, new RoutedEventArgs());
    }

    private void OnToggleFullScreenFromMenuClicked(object? sender, EventArgs e)
    {
        OnToggleFullScreenClicked(this, new RoutedEventArgs());
    }

    private void OnToggleFloatOnTopClicked(object? sender, EventArgs e)
    {
        Topmost = !Topmost;
        UpdateNativeMenuState();
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnActualSizeClicked(object? sender, EventArgs e)
    {
        SetVideoLayoutMode(VideoLayoutMode.Fit, "Actual Size");
        ResizeWindowToVideoActualSize();
    }

    private void OnFitToScreenClicked(object? sender, EventArgs e)
    {
        SetVideoLayoutMode(VideoLayoutMode.Fit, "Fit to Screen");
        FitWindowToScreen();
    }

    private void OnFillModeClicked(object? sender, EventArgs e)
    {
        SetVideoLayoutMode(VideoLayoutMode.Fill, "Fill");
        FitWindowToScreen();
    }

    private void OnPanoramicModeClicked(object? sender, EventArgs e)
    {
        SetVideoLayoutMode(VideoLayoutMode.Panoramic, "Panoramic");
        FitWindowToScreen();
    }

    private void OnTimeDisplayRemainingClicked(object? sender, EventArgs e) => SetTimeDisplayMode(MenuTimeDisplayMode.Remaining);

    private void OnTimeDisplayElapsedClicked(object? sender, EventArgs e) => SetTimeDisplayMode(MenuTimeDisplayMode.Elapsed);

    private void OnTimeDisplayTimecodeClicked(object? sender, EventArgs e) => SetTimeDisplayMode(MenuTimeDisplayMode.Timecode);

    private void OnTimeDisplayFramesClicked(object? sender, EventArgs e) => SetTimeDisplayMode(MenuTimeDisplayMode.FrameCount);

    private async void OnGoToTimeClicked(object? sender, EventArgs e) => await GoToTimeAsync();

    private async void OnGoToFrameClicked(object? sender, EventArgs e) => await GoToFrameAsync();

    private void SetTimeDisplayMode(MenuTimeDisplayMode mode)
    {
        _timeDisplayMode = mode;
        UpdateTimeDisplay();
        UpdateNativeMenuState();
        ShowOverlayAndRestartIdleTimer();
    }

    private void SetVideoLayoutMode(VideoLayoutMode mode, string modeName)
    {
        Player.LayoutMode = mode;
        ViewModel.Status = $"Video mode: {modeName}";
        UpdateNativeMenuState();
        ShowOverlayAndRestartIdleTimer();
    }

    private void FitWindowToScreen()
    {
        if (Player.VideoWidth <= 0 || Player.VideoHeight <= 0)
        {
            return;
        }

        _lastFittedVideoWidth = 0;
        _lastFittedVideoHeight = 0;
        _lastFitAttemptUtc = DateTime.MinValue;
        TryFitWindowToVideo(Player.VideoWidth, Player.VideoHeight);
    }

    private void ResizeWindowToVideoActualSize()
    {
        var videoWidth = Player.VideoWidth;
        var videoHeight = Player.VideoHeight;
        if (videoWidth <= 0 || videoHeight <= 0 || WindowState != WindowState.Normal)
        {
            return;
        }

        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
        var maxWidth = Math.Max(MinWidth, (screen.WorkingArea.Width / scaling) - 72d);
        var maxHeight = Math.Max(MinHeight, (screen.WorkingArea.Height / scaling) - 96d);
        var scale = Math.Min(1d, Math.Min(maxWidth / videoWidth, maxHeight / videoHeight));
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0d)
        {
            return;
        }

        Width = Math.Round(Math.Clamp(videoWidth * scale, MinWidth, maxWidth));
        Height = Math.Round(Math.Clamp(videoHeight * scale, MinHeight, maxHeight));
        _lastFittedVideoWidth = videoWidth;
        _lastFittedVideoHeight = videoHeight;
    }

    private void OnCloseWindowClicked(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnMinimizeClicked(object? sender, EventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnZoomClicked(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            return;
        }

        WindowState = WindowState.Maximized;
    }

    private void OnBringAllToFrontClicked(object? sender, EventArgs e)
    {
        Activate();
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        var about = new Window
        {
            Width = 430,
            Height = 180,
            CanResize = false,
            Title = "About Media Player",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Media Player",
                            FontSize = 22,
                            FontWeight = FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = "GPU-accelerated, no-airspace Avalonia media player."
                        },
                        new TextBlock
                        {
                            Text = "QuickTime-style controls and native macOS app menu."
                        }
                    }
                }
            }
        };
        await about.ShowDialog(this);
    }

    private async void OnHelpClicked(object? sender, EventArgs e)
    {
        await TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(new Uri("https://github.com/wieslawsoltes/MediaPlayer"));
    }

    private async void OnShowMovieInspectorClicked(object? sender, EventArgs e)
    {
        await ShowMovieInspectorAsync();
    }

    private void OnPreferencesClicked(object? sender, EventArgs e)
    {
        ViewModel.Status = "Use File for Workflow Quality, View for playback/time/renderer options, and Window for Movie Inspector.";
    }

    private void SetWorkflowQualityProfile(MediaWorkflowQualityProfile qualityProfile)
    {
        if (_workflowQualityProfile == qualityProfile)
        {
            return;
        }

        _workflowQualityProfile = qualityProfile;
        var label = _mediaWorkflowService.GetQualityProfileDisplayName(qualityProfile);
        ViewModel.Status = $"Workflow quality set: {label}";
        UpdateNativeMenuState();
        ShowOverlayAndRestartIdleTimer();
    }

    private async Task<MediaWorkflowQualityProfile?> ResolveWorkflowQualityProfileForOperationAsync(string operationName)
    {
        if (_isMacOs)
        {
            return _workflowQualityProfile;
        }

        var currentLabel = _mediaWorkflowService.GetQualityProfileDisplayName(_workflowQualityProfile);
        var input = await PromptForInputAsync(
            $"{operationName} Quality",
            "Speed / Balanced / Quality",
            currentLabel);
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (!TryParseWorkflowQualityProfile(input, out var selectedProfile))
        {
            ViewModel.Status = "Invalid quality profile. Use Speed, Balanced, or Quality.";
            return null;
        }

        SetWorkflowQualityProfile(selectedProfile);
        return selectedProfile;
    }

    private static bool TryParseWorkflowQualityProfile(string input, out MediaWorkflowQualityProfile profile)
    {
        var normalized = input.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "1":
            case "speed":
            case "fast":
                profile = MediaWorkflowQualityProfile.Speed;
                return true;
            case "2":
            case "balanced":
            case "default":
                profile = MediaWorkflowQualityProfile.Balanced;
                return true;
            case "3":
            case "quality":
            case "best":
                profile = MediaWorkflowQualityProfile.Quality;
                return true;
            default:
                profile = MediaWorkflowQualityProfile.Balanced;
                return false;
        }
    }

    private Task ShowMovieInspectorAsync()
    {
        if (_movieInspectorWindow is not null)
        {
            _movieInspectorWindow.DataContext = BuildMovieInspectorViewModel();
            _movieInspectorWindow.Activate();
            return Task.CompletedTask;
        }

        var inspector = new MovieInspectorWindow
        {
            DataContext = BuildMovieInspectorViewModel()
        };
        _movieInspectorWindow = inspector;
        inspector.Closed += (_, _) => _movieInspectorWindow = null;
        inspector.Show(this);
        return Task.CompletedTask;
    }

    private MovieInspectorViewModel BuildMovieInspectorViewModel()
    {
        var source = ViewModel.SourceUri;
        var mediaName = source is null ? ViewModel.DisplayTitle : BuildDisplayTitle(source);
        var location = source is null
            ? "Not loaded"
            : source.IsFile
                ? source.LocalPath
                : source.ToString();

        var mediaType = source is null ? "Unknown" : GetMediaType(source);
        var fileSize = source is not null && source.IsFile ? FormatFileSize(source.LocalPath) : "N/A (stream)";

        var videoWidth = Player.VideoWidth;
        var videoHeight = Player.VideoHeight;
        var resolution = videoWidth > 0 && videoHeight > 0
            ? $"{videoWidth} x {videoHeight}"
            : "Unknown";
        var aspectRatio = FormatAspectRatio(videoWidth, videoHeight);
        var frameRate = Player.FrameRate > 0.1d
            ? $"{Player.FrameRate:0.###} fps"
            : "Unknown";

        var effectiveRenderer = RendererPreferenceState.ToDisplayName(RendererPreferenceState.EffectivePreference);
        var runtimeRenderer = RendererPreferenceState.ToDisplayName(RendererPreferenceState.RuntimePreference);
        var rendererPreference = effectiveRenderer == runtimeRenderer
            ? effectiveRenderer
            : $"{effectiveRenderer} (runtime: {runtimeRenderer})";
        var workflowDiagnostics = _workflowProviderDiagnostics.Current;
        var nativeFallbackReason = !string.IsNullOrWhiteSpace(Player.NativePlaybackFallbackReason)
            ? Player.NativePlaybackFallbackReason
            : workflowDiagnostics.FallbackReason;
        var audioCapabilities = GetCachedAudioCapabilitiesText();
        var audioInputDevices = Player.GetAudioInputDevices();
        var audioOutputDevices = Player.GetAudioOutputDevices();
        var routeState = Player.GetAudioRouteState();

        return new MovieInspectorViewModel
        {
            MediaName = mediaName,
            MediaLocation = location,
            MediaType = mediaType,
            FileSize = fileSize,
            Resolution = resolution,
            AspectRatio = aspectRatio,
            Duration = FormatClock(Player.Duration),
            CurrentPosition = FormatClock(Player.Position),
            FrameRate = frameRate,
            PlaybackRate = $"{Player.PlaybackRate:0.##}x",
            BackendProfile = string.IsNullOrWhiteSpace(Player.ActiveProfileName) ? "Unknown" : Player.ActiveProfileName,
            DecodePipeline = string.IsNullOrWhiteSpace(Player.ActiveDecodeApi) ? "Unknown" : Player.ActiveDecodeApi,
            RenderPipeline = string.IsNullOrWhiteSpace(Player.ActiveRenderPath) ? "Unknown" : Player.ActiveRenderPath,
            RendererPreference = rendererPreference,
            NativeProviderMode = string.IsNullOrWhiteSpace(Player.ConfiguredNativeProviderMode)
                ? workflowDiagnostics.ConfiguredMode.ToString()
                : Player.ConfiguredNativeProviderMode,
            PlaybackProvider = string.IsNullOrWhiteSpace(Player.ActiveNativePlaybackProvider)
                ? "Unknown"
                : Player.ActiveNativePlaybackProvider,
            WorkflowProvider = workflowDiagnostics.ActiveProvider.ToString(),
            NativeFallbackReason = string.IsNullOrWhiteSpace(nativeFallbackReason) ? "None" : nativeFallbackReason,
            AudioCapabilities = audioCapabilities,
            AudioOutputDevices = FormatAudioDeviceSummary(audioOutputDevices),
            AudioInputDevices = FormatAudioDeviceSummary(audioInputDevices),
            AudioOutputRoute = FormatAudioRoute(routeState.SelectedOutputDeviceId),
            AudioInputRoute = FormatAudioRoute(routeState.SelectedInputDeviceId),
            BackendCapabilityTable = string.IsNullOrWhiteSpace(Player.BackendCapabilityTable)
                ? "Unavailable"
                : Player.BackendCapabilityTable,
            LastError = string.IsNullOrWhiteSpace(Player.LastError) ? "None" : Player.LastError
        };
    }

    private sealed class NullWorkflowProviderDiagnostics : IMediaWorkflowProviderDiagnostics
    {
        public MediaPlayer.Native.Abstractions.MediaPlayerNativeProviderDiagnostics Current =>
            new(
                MediaPlayer.Native.Abstractions.MediaPlayerNativeProviderMode.AutoPreferInterop,
                MediaPlayer.Native.Abstractions.MediaPlayerNativeProviderKind.Unknown,
                string.Empty);
    }

    private static string GetMediaType(Uri source)
    {
        var extension = source.IsFile
            ? Path.GetExtension(source.LocalPath)
            : Path.GetExtension(source.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "Unknown";
        }

        return extension.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatFileSize(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return "Unknown";
            }

            var bytes = new FileInfo(filePath).Length;
            var value = (double)bytes;
            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            var unitIndex = 0;
            while (value >= 1024d && unitIndex < units.Length - 1)
            {
                value /= 1024d;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string FormatClock(TimeSpan value)
    {
        var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return clamped.ToString(@"hh\:mm\:ss");
    }

    private static string FormatAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return "Unknown";
        }

        var divisor = GreatestCommonDivisor(width, height);
        return $"{width / divisor}:{height / divisor}";
    }

    private static string FormatAudioDeviceSummary(IReadOnlyList<MediaAudioDeviceInfo> devices)
    {
        if (devices.Count == 0)
        {
            return "Unavailable";
        }

        var defaultName = string.Empty;
        for (var index = 0; index < devices.Count; index++)
        {
            if (devices[index].IsDefault)
            {
                defaultName = devices[index].Name;
                break;
            }
        }

        return string.IsNullOrWhiteSpace(defaultName)
            ? devices.Count.ToString(CultureInfo.InvariantCulture)
            : $"{devices.Count} (default: {defaultName})";
    }

    private string GetCachedAudioCapabilitiesText()
    {
        var capabilities = Player.AudioCapabilities;
        if (capabilities == _cachedAudioCapabilities)
        {
            return _cachedAudioCapabilitiesText;
        }

        _cachedAudioCapabilities = capabilities;
        _cachedAudioCapabilitiesText = MediaAudioCapabilityFormatter.ToDisplayString(capabilities);
        return _cachedAudioCapabilitiesText;
    }

    private static string FormatAudioRoute(string? routeDeviceId)
    {
        return string.IsNullOrWhiteSpace(routeDeviceId)
            ? "Default"
            : routeDeviceId;
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        if (left == 0 || right == 0)
        {
            return 1;
        }

        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }

        return Math.Max(1, left);
    }

    private void OnRendererAutoClicked(object? sender, EventArgs e) => ApplyRendererPreference(RendererPreference.Auto);

    private void OnRendererOpenGlClicked(object? sender, EventArgs e) => ApplyRendererPreference(RendererPreference.OpenGl);

    private void OnRendererVulkanClicked(object? sender, EventArgs e) => ApplyRendererPreference(RendererPreference.Vulkan);

    private void OnRendererMetalClicked(object? sender, EventArgs e) => ApplyRendererPreference(RendererPreference.Metal);

    private void OnRendererSoftwareClicked(object? sender, EventArgs e) => ApplyRendererPreference(RendererPreference.Software);

    private void ApplyRendererPreference(RendererPreference preference)
    {
        if (!RendererPreferenceState.SavePreference(preference, out var error))
        {
            ViewModel.Status = $"Failed to save renderer preference: {error}";
            return;
        }

        UpdateNativeMenuState();
        ViewModel.Status = $"Renderer preference set to {RendererPreferenceState.ToDisplayName(preference)}. Restart app to apply (runtime playback surface uses OpenGL).";
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnNotImplementedMenuClicked(object? sender, EventArgs e)
    {
        ViewModel.Status = "This menu command is not implemented in demo.";
    }

    private void OnSelectAllMenuClicked(object? sender, EventArgs e)
    {
        // There is no editable main document surface in this demo.
    }

    private void OnQuitClicked(object? sender, EventArgs e)
    {
        Close();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    private async Task<string?> PromptForInputAsync(string title, string watermark, string initialText)
    {
        var value = initialText;
        var tcs = new TaskCompletionSource<string?>();
        var input = new TextBox
        {
            Text = initialText,
            Watermark = watermark,
            MinWidth = 420
        };

        var dialog = new Window
        {
            Width = 500,
            Height = 160,
            CanResize = false,
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var open = new Button { Content = "Open", MinWidth = 78 };
        var cancel = new Button { Content = "Cancel", MinWidth = 78 };

        open.Click += (_, _) =>
        {
            value = input.Text ?? string.Empty;
            tcs.TrySetResult(value);
            dialog.Close();
        };
        cancel.Click += (_, _) =>
        {
            tcs.TrySetResult(null);
            dialog.Close();
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(null);

        dialog.Content = new Border
        {
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { cancel, open }
                    }
                }
            }
        };

        dialog.Show(this);
        return await tcs.Task;
    }

    private void UpdateNativeMenuState()
    {
        _nativeMenuCoordinator.Update(CreateNativeMenuState());
    }

    private static bool TryCreateMediaUri(string input, out Uri source, out string error)
    {
        source = default!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Provide a media URL or local file path.";
            return false;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile))
        {
            source = uri;
            return true;
        }

        if (File.Exists(input))
        {
            source = new Uri(Path.GetFullPath(input));
            return true;
        }

        error = "Cannot parse media source. Use an absolute URL or an existing file path.";
        return false;
    }

}
