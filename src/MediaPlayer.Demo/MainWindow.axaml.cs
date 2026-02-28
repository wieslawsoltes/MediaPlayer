using System;
using System.Collections.Generic;
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
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentIcons.Common;
using MediaPlayer.Demo.ViewModels;

namespace MediaPlayer.Demo;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _overlayIdleTimer;
    private static readonly TimeSpan OverlayHideDelay = TimeSpan.FromSeconds(1.7);
    private bool _isTimelineDragging;
    private bool _isTimelineUpdating;
    private bool _overlayVisible = true;
    private bool _isPointerOverHud;
    private bool _wasPlaying;
    private bool _alwaysShowControls;
    private DateTime _lastOverlayInteractionUtc = DateTime.UtcNow;
    private int _lastFittedVideoWidth;
    private int _lastFittedVideoHeight;
    private DateTime _lastFitAttemptUtc = DateTime.MinValue;
    private NativeMenuItem? _menuPlayPauseItem;
    private NativeMenuItem? _menuMuteItem;
    private NativeMenuItem? _menuLoopItem;
    private NativeMenuItem? _menuFullscreenItem;
    private NativeMenuItem? _menuAlwaysShowControlsItem;

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => UpdateStatus());
        _overlayIdleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => HideOverlayOnIdle());

        _statusTimer.Start();
        _overlayIdleTimer.Start();
        Closed += OnClosed;
        AttachPointerWakeHandlers();

        LoadSource();
        ConfigureNativeMenuIfSupported();
        SetOverlayVisible(true);
        UpdateControlGlyphs();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DetachPointerWakeHandlers();
        _statusTimer.Stop();
        _overlayIdleTimer.Stop();
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
        UpdateControlGlyphs();
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
        ViewModel.Status = $"{state} | {position} / {duration}";

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
        }

        if (!_isTimelineUpdating)
        {
            var durationSeconds = Math.Max(1d, Player.Duration.TotalSeconds);
            var positionSeconds = Math.Clamp(Player.Position.TotalSeconds, 0d, durationSeconds);

            _isTimelineUpdating = true;
            TimelineSlider.Maximum = durationSeconds;
            if (!_isTimelineDragging)
            {
                TimelineSlider.Value = positionSeconds;
            }
            _isTimelineUpdating = false;
        }

        TryFitWindowToVideo(Player.VideoWidth, Player.VideoHeight);

        UpdateControlGlyphs();
    }

    private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        _isTimelineDragging = true;
        SetOverlayVisible(true);
        e.Pointer.Capture(slider);
        _lastOverlayInteractionUtc = DateTime.UtcNow;
    }

    private void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        if (_isTimelineDragging)
        {
            SeekFromTimeline(slider.Value);
        }

        _isTimelineDragging = false;
        if (e.Pointer.Captured == slider)
        {
            e.Pointer.Capture(null);
        }

        ShowOverlayAndRestartIdleTimer();
    }

    private void OnTimelinePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isTimelineDragging || sender is not Slider slider)
        {
            return;
        }

        _isTimelineDragging = false;
        SeekFromTimeline(slider.Value);
        ShowOverlayAndRestartIdleTimer();
    }

    private void OnTimelineValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isTimelineUpdating || !_isTimelineDragging || sender is not Slider slider)
        {
            return;
        }

        ViewModel.SeekSeconds = slider.Value;
    }

    private void SeekFromTimeline(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return;
        }

        var durationSeconds = Player.Duration.TotalSeconds;
        var upperBound = durationSeconds > 0d ? durationSeconds : Math.Max(0d, seconds);
        var clamped = Math.Clamp(seconds, 0d, upperBound);
        ViewModel.SeekSeconds = clamped;
        Player.Seek(TimeSpan.FromSeconds(clamped));
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
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Space)
        {
            OnPlayPauseClicked(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Left)
        {
            OnSeekBack10Clicked(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Right)
        {
            OnSeekForward10Clicked(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.M)
        {
            OnToggleMuteClicked(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.L)
        {
            OnToggleLoopClicked(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.A)
        {
            OnToggleAutoPlayClicked(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F)
        {
            OnToggleFullScreenClicked(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            UpdateControlGlyphs();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Up)
        {
            ViewModel.Volume = Math.Clamp(ViewModel.Volume + 5, 0, 100);
            if (ViewModel.Volume > 0)
            {
                ViewModel.IsMuted = false;
            }

            UpdateControlGlyphs();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Down)
        {
            ViewModel.Volume = Math.Clamp(ViewModel.Volume - 5, 0, 100);
            if (ViewModel.Volume <= 0)
            {
                ViewModel.IsMuted = true;
            }

            UpdateControlGlyphs();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.O)
        {
            await Dispatcher.UIThread.InvokeAsync(() => OnOpenFileClicked(this, new RoutedEventArgs()));
            e.Handled = true;
        }
    }

    private void HideOverlayOnIdle()
    {
        if (_alwaysShowControls || _isTimelineDragging || _isPointerOverHud || !CanAutoHideControls())
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
    }

    private void UpdateControlGlyphs()
    {
        PlayPauseIcon.Symbol = Player.IsPlaying ? Symbol.Pause : Symbol.Play;
        VolumeIcon.Symbol = ViewModel.IsMuted || ViewModel.Volume < 1 ? Symbol.SpeakerMute : Symbol.Speaker2;
        LoopIcon.Foreground = ViewModel.IsLooping ? Brushes.White : new SolidColorBrush(Color.FromArgb(168, 255, 255, 255));
        FullScreenIcon.Symbol = WindowState == WindowState.FullScreen ? Symbol.FullScreenMinimize : Symbol.FullScreenMaximize;
        UpdateNativeMenuState();
    }

    private bool CanAutoHideControls()
    {
        return Player.IsPlaying || Player.Position > TimeSpan.Zero || Player.Duration > TimeSpan.Zero;
    }

    private void AttachPointerWakeHandlers()
    {
        AddHandler(InputElement.PointerMovedEvent, OnGlobalPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerWheelChangedEvent, OnGlobalPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerEnteredEvent, OnGlobalPointerEntered, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
    }

    private void DetachPointerWakeHandlers()
    {
        RemoveHandler(InputElement.PointerMovedEvent, OnGlobalPointerMoved);
        RemoveHandler(InputElement.PointerPressedEvent, OnGlobalPointerPressed);
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

    private void OnGlobalPointerEntered(object? sender, PointerEventArgs e)
    {
        ShowOverlayAndRestartIdleTimer();
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

    private void ConfigureNativeMenuIfSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        var menu = BuildNativeMenu();
        menu.NeedsUpdate += (_, _) => UpdateNativeMenuState();
        NativeMenu.SetMenu(this, menu);
        UpdateNativeMenuState();
    }

    private NativeMenu BuildNativeMenu()
    {
        var appMenu = new NativeMenuItem("Media Player")
        {
            Menu = new NativeMenu()
        };
        appMenu.Menu!.Add(CreateNativeMenuItem("About Media Player", null, OnAboutClicked));
        appMenu.Menu.Add(new NativeMenuItemSeparator());
        appMenu.Menu.Add(CreateNativeMenuItem("Preferences…", null, OnPreferencesClicked));
        appMenu.Menu.Add(new NativeMenuItemSeparator());
        appMenu.Menu.Add(CreateNativeMenuItem("Quit Media Player", new KeyGesture(Key.Q, KeyModifiers.Meta), OnQuitClicked));

        var fileMenu = new NativeMenuItem("File")
        {
            Menu = new NativeMenu()
        };
        fileMenu.Menu!.Add(CreateNativeMenuItem("Open File…", new KeyGesture(Key.O, KeyModifiers.Meta), OnOpenFileFromMenuClicked));
        fileMenu.Menu.Add(CreateNativeMenuItem("Open Location…", new KeyGesture(Key.O, KeyModifiers.Meta | KeyModifiers.Shift), OnOpenLocationClicked));
        fileMenu.Menu.Add(new NativeMenuItemSeparator());
        _menuPlayPauseItem = CreateNativeMenuItem("Play", new KeyGesture(Key.Space), OnPlayPauseFromMenuClicked);
        fileMenu.Menu.Add(_menuPlayPauseItem);
        fileMenu.Menu.Add(CreateNativeMenuItem("Stop", null, OnStopFromMenuClicked));
        fileMenu.Menu.Add(new NativeMenuItemSeparator());
        fileMenu.Menu.Add(CreateNativeMenuItem("Close Window", new KeyGesture(Key.W, KeyModifiers.Meta), OnCloseWindowClicked));

        var editMenu = new NativeMenuItem("Edit")
        {
            Menu = new NativeMenu()
        };
        editMenu.Menu!.Add(CreateNativeMenuItem("Undo", new KeyGesture(Key.Z, KeyModifiers.Meta), OnNotImplementedMenuClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Redo", new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift), OnNotImplementedMenuClicked, enabled: false));
        editMenu.Menu.Add(new NativeMenuItemSeparator());
        editMenu.Menu.Add(CreateNativeMenuItem("Cut", new KeyGesture(Key.X, KeyModifiers.Meta), OnNotImplementedMenuClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Copy", new KeyGesture(Key.C, KeyModifiers.Meta), OnNotImplementedMenuClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Paste", new KeyGesture(Key.V, KeyModifiers.Meta), OnNotImplementedMenuClicked, enabled: false));
        editMenu.Menu.Add(CreateNativeMenuItem("Select All", new KeyGesture(Key.A, KeyModifiers.Meta), OnSelectAllMenuClicked));

        var viewMenu = new NativeMenuItem("View")
        {
            Menu = new NativeMenu()
        };
        _menuMuteItem = CreateNativeMenuItem(
            "Mute",
            new KeyGesture(Key.M, KeyModifiers.Meta),
            OnToggleMuteFromMenuClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu!.Add(_menuMuteItem);
        _menuLoopItem = CreateNativeMenuItem(
            "Loop",
            new KeyGesture(Key.L, KeyModifiers.Meta),
            OnToggleLoopFromMenuClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu.Add(_menuLoopItem);
        _menuAlwaysShowControlsItem = CreateNativeMenuItem(
            "Always Show Controls",
            null,
            OnToggleAlwaysShowControlsClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu.Add(_menuAlwaysShowControlsItem);
        viewMenu.Menu.Add(new NativeMenuItemSeparator());
        _menuFullscreenItem = CreateNativeMenuItem(
            "Enter Full Screen",
            new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control),
            OnToggleFullScreenFromMenuClicked,
            toggleType: NativeMenuItemToggleType.CheckBox);
        viewMenu.Menu.Add(_menuFullscreenItem);

        var windowMenu = new NativeMenuItem("Window")
        {
            Menu = new NativeMenu()
        };
        windowMenu.Menu!.Add(CreateNativeMenuItem("Minimize", new KeyGesture(Key.M, KeyModifiers.Meta), OnMinimizeClicked));
        windowMenu.Menu.Add(CreateNativeMenuItem("Zoom", null, OnZoomClicked));
        windowMenu.Menu.Add(new NativeMenuItemSeparator());
        windowMenu.Menu.Add(CreateNativeMenuItem("Bring All to Front", null, OnBringAllToFrontClicked));

        var helpMenu = new NativeMenuItem("Help")
        {
            Menu = new NativeMenu()
        };
        helpMenu.Menu!.Add(CreateNativeMenuItem("Media Player Help", null, OnHelpClicked));

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

    private void OnPreferencesClicked(object? sender, EventArgs e)
    {
        ViewModel.Status = "No preferences panel yet.";
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
        if (_menuPlayPauseItem is not null)
        {
            _menuPlayPauseItem.Header = Player.IsPlaying ? "Pause" : "Play";
        }

        if (_menuMuteItem is not null)
        {
            _menuMuteItem.IsChecked = ViewModel.IsMuted || ViewModel.Volume <= 0;
        }

        if (_menuLoopItem is not null)
        {
            _menuLoopItem.IsChecked = ViewModel.IsLooping;
        }

        if (_menuFullscreenItem is not null)
        {
            var isFullscreen = WindowState == WindowState.FullScreen;
            _menuFullscreenItem.IsChecked = isFullscreen;
            _menuFullscreenItem.Header = isFullscreen ? "Exit Full Screen" : "Enter Full Screen";
        }

        if (_menuAlwaysShowControlsItem is not null)
        {
            _menuAlwaysShowControlsItem.IsChecked = _alwaysShowControls;
        }
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
