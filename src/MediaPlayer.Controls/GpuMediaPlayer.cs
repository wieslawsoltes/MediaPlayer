using System;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using MediaPlayer.Controls.Backends;
using MediaPlayer.Controls.Rendering;

namespace MediaPlayer.Controls;

public sealed class GpuMediaPlayer : OpenGlControlBase, IDisposable
{
    public static readonly StyledProperty<Uri?> SourceProperty =
        AvaloniaProperty.Register<GpuMediaPlayer, Uri?>(nameof(Source));

    public static readonly StyledProperty<bool> AutoPlayProperty =
        AvaloniaProperty.Register<GpuMediaPlayer, bool>(nameof(AutoPlay), true);

    public static readonly StyledProperty<double> VolumeProperty =
        AvaloniaProperty.Register<GpuMediaPlayer, double>(nameof(Volume), 85d);

    public static readonly StyledProperty<bool> IsMutedProperty =
        AvaloniaProperty.Register<GpuMediaPlayer, bool>(nameof(IsMuted));

    public static readonly StyledProperty<bool> IsLoopingProperty =
        AvaloniaProperty.Register<GpuMediaPlayer, bool>(nameof(IsLooping));

    public static readonly DirectProperty<GpuMediaPlayer, bool> IsPlayingProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, bool>(
            nameof(IsPlaying),
            o => o.IsPlaying);

    public static readonly DirectProperty<GpuMediaPlayer, TimeSpan> PositionProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, TimeSpan>(
            nameof(Position),
            o => o.Position);

    public static readonly DirectProperty<GpuMediaPlayer, TimeSpan> DurationProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, TimeSpan>(
            nameof(Duration),
            o => o.Duration);

    public static readonly DirectProperty<GpuMediaPlayer, int> VideoWidthProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, int>(
            nameof(VideoWidth),
            o => o.VideoWidth);

    public static readonly DirectProperty<GpuMediaPlayer, int> VideoHeightProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, int>(
            nameof(VideoHeight),
            o => o.VideoHeight);

    public static readonly DirectProperty<GpuMediaPlayer, string> ActiveDecodeApiProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, string>(
            nameof(ActiveDecodeApi),
            o => o.ActiveDecodeApi);

    public static readonly DirectProperty<GpuMediaPlayer, string> ActiveRenderPathProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, string>(
            nameof(ActiveRenderPath),
            o => o.ActiveRenderPath);

    public static readonly DirectProperty<GpuMediaPlayer, string> LastErrorProperty =
        AvaloniaProperty.RegisterDirect<GpuMediaPlayer, string>(
            nameof(LastError),
            o => o.LastError);

    private readonly IMediaBackend _backend;
    private readonly OpenGlVideoRenderer _renderer = new();
    private bool _isPlaying;
    private TimeSpan _position;
    private TimeSpan _duration;
    private int _videoWidth;
    private int _videoHeight;
    private string _activeDecodeApi;
    private string _activeRenderPath;
    private string _lastError = string.Empty;
    private long _lastRenderedFrameSequence = -1;
    private bool _disposed;

    public GpuMediaPlayer()
    {
        try
        {
            _backend = new LibVlcMediaBackend();
            _activeDecodeApi = _backend.ActiveDecodeApi;
            _activeRenderPath = _backend.ActiveRenderPath;
        }
        catch (Exception ex)
        {
            try
            {
                _backend = new FfmpegMediaBackend();
                _activeDecodeApi = _backend.ActiveDecodeApi;
                _activeRenderPath = _backend.ActiveRenderPath;
                _lastError = $"Primary backend failed, fallback active: {ex.Message}";
            }
            catch (Exception fallbackEx)
            {
                _backend = new NullMediaBackend(fallbackEx.Message);
                _activeDecodeApi = "Unavailable";
                _activeRenderPath = "Unavailable";
                _lastError = $"Primary backend failed: {ex.Message} | Fallback failed: {fallbackEx.Message}";
            }
        }

        _backend.FrameReady += OnFrameReady;
        _backend.PlaybackStateChanged += OnPlaybackStateChanged;
        _backend.TimelineChanged += OnTimelineChanged;
        _backend.ErrorOccurred += OnErrorOccurred;

        _backend.SetVolume((float)Volume);
        _backend.SetMuted(IsMuted);
        _backend.SetLooping(IsLooping);

    }

    public Uri? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool AutoPlay
    {
        get => GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    public double Volume
    {
        get => GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public bool IsMuted
    {
        get => GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public bool IsLooping
    {
        get => GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetAndRaise(IsPlayingProperty, ref _isPlaying, value);
    }

    public TimeSpan Position
    {
        get => _position;
        private set => SetAndRaise(PositionProperty, ref _position, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        private set => SetAndRaise(DurationProperty, ref _duration, value);
    }

    public int VideoWidth
    {
        get => _videoWidth;
        private set => SetAndRaise(VideoWidthProperty, ref _videoWidth, value);
    }

    public int VideoHeight
    {
        get => _videoHeight;
        private set => SetAndRaise(VideoHeightProperty, ref _videoHeight, value);
    }

    public string ActiveDecodeApi
    {
        get => _activeDecodeApi;
        private set => SetAndRaise(ActiveDecodeApiProperty, ref _activeDecodeApi, value);
    }

    public string ActiveRenderPath
    {
        get => _activeRenderPath;
        private set => SetAndRaise(ActiveRenderPathProperty, ref _activeRenderPath, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => SetAndRaise(LastErrorProperty, ref _lastError, value);
    }

    public void Play()
    {
        EnsureNotDisposed();
        _backend.Play();
        RequestNextFrameRendering();
    }

    public void Pause()
    {
        EnsureNotDisposed();
        _backend.Pause();
    }

    public void Stop()
    {
        EnsureNotDisposed();
        _backend.Stop();
        RequestNextFrameRendering();
    }

    public void Seek(TimeSpan position)
    {
        EnsureNotDisposed();
        _backend.Seek(position);
        RequestNextFrameRendering();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backend.FrameReady -= OnFrameReady;
        _backend.PlaybackStateChanged -= OnPlaybackStateChanged;
        _backend.TimelineChanged -= OnTimelineChanged;
        _backend.ErrorOccurred -= OnErrorOccurred;
        _backend.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (_disposed)
        {
            base.OnPropertyChanged(change);
            return;
        }

        if (change.Property == SourceProperty)
        {
            ApplySource(change.GetNewValue<Uri?>());
        }
        else if (change.Property == VolumeProperty)
        {
            _backend.SetVolume((float)Math.Clamp(Volume, 0, 100));
        }
        else if (change.Property == IsMutedProperty)
        {
            _backend.SetMuted(IsMuted);
        }
        else if (change.Property == IsLoopingProperty)
        {
            _backend.SetLooping(IsLooping);
        }

        base.OnPropertyChanged(change);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _renderer.Initialize(gl, GlVersion);
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer.Dispose(gl);
    }

    protected override void OnOpenGlLost()
    {
        _lastRenderedFrameSequence = -1;
        base.OnOpenGlLost();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        EnsureNotDisposed();

        var scale = VisualRoot?.RenderScaling ?? 1d;
        var pixelWidth = Math.Max(1, (int)(Bounds.Width * scale));
        var pixelHeight = Math.Max(1, (int)(Bounds.Height * scale));

        var latestSequence = _backend.LatestFrameSequence;
        if (latestSequence > _lastRenderedFrameSequence && _backend.TryAcquireFrame(out var frame))
        {
            using (frame)
            {
                _renderer.UploadFrame(gl, frame);
                _lastRenderedFrameSequence = frame.Sequence;
            }
        }

        _renderer.Render(gl, fb, pixelWidth, pixelHeight);

        if (_backend.IsPlaying)
        {
            RequestNextFrameRendering();
        }
    }

    private void ApplySource(Uri? source)
    {
        LastError = string.Empty;
        _lastRenderedFrameSequence = -1;
        VideoWidth = 0;
        VideoHeight = 0;

        if (source is null)
        {
            _backend.Stop();
            return;
        }

        try
        {
            _backend.Open(source);

            if (AutoPlay)
            {
                _backend.Play();
            }

            RequestNextFrameRendering();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private void OnFrameReady(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            IsPlaying = _backend.IsPlaying;
            ActiveDecodeApi = _backend.ActiveDecodeApi;
            ActiveRenderPath = _backend.ActiveRenderPath;
            RequestNextFrameRendering();
        }, DispatcherPriority.Background);
    }

    private void OnTimelineChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            Position = _backend.Position;
            Duration = _backend.Duration;
            VideoWidth = _backend.VideoWidth;
            VideoHeight = _backend.VideoHeight;
        }, DispatcherPriority.Background);
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => LastError = message, DispatcherPriority.Background);
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
