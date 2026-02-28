using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MediaPlayer.Controls.Backends;

internal sealed class FfmpegMediaBackend : IMediaBackend
{
    private readonly object _frameGate = new();
    private readonly object _stateGate = new();

    private Process? _videoProcess;
    private Process? _audioProcess;
    private CancellationTokenSource? _decodeCts;
    private Task? _decodeTask;
    private byte[]? _frameBuffer;
    private GCHandle _pinnedFrameBuffer;
    private int _frameWidth;
    private int _frameHeight;
    private int _frameStride;
    private bool _looping;
    private bool _disposed;

    private Uri? _source;
    private TimeSpan _duration;
    private TimeSpan _position;
    private DateTime _playbackStartedUtc;
    private TimeSpan _positionAtPlayStart;
    private bool _isPlaying;
    private long _latestFrameSequence;
    private readonly bool _ffplayAvailable;

    public FfmpegMediaBackend()
    {
        _ffplayAvailable = IsToolAvailable("ffplay");
    }

    public event EventHandler? FrameReady;
    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler<string>? ErrorOccurred;

    public string ActiveProfileName => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS (FFmpeg fallback)" : "FFmpeg fallback";

    public string ActiveDecodeApi => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? "VideoToolbox (ffmpeg hwaccel auto)"
        : "FFmpeg hwaccel auto";

    public string ActiveRenderPath => "ffmpeg raw BGRA frames -> Avalonia OpenGL texture";

    public bool IsPlaying
    {
        get
        {
            lock (_stateGate)
            {
                return _isPlaying;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_stateGate)
            {
                if (!_isPlaying)
                {
                    return _position;
                }

                var elapsed = DateTime.UtcNow - _playbackStartedUtc;
                var current = _positionAtPlayStart + elapsed;
                if (_duration > TimeSpan.Zero && current > _duration)
                {
                    current = _duration;
                }

                return current;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_stateGate)
            {
                return _duration;
            }
        }
    }

    public int VideoWidth => _frameWidth;

    public int VideoHeight => _frameHeight;

    public long LatestFrameSequence => Interlocked.Read(ref _latestFrameSequence);

    public void Open(Uri source)
    {
        ThrowIfDisposed();

        StopProcesses(resetPosition: false);
        ReleaseFrameBuffer();

        if (!TryProbeSource(source, out var width, out var height, out var duration, out var error))
        {
            throw new InvalidOperationException(error);
        }

        _source = source;
        _duration = duration;
        _position = TimeSpan.Zero;
        _frameWidth = width;
        _frameHeight = height;
        _frameStride = width * 4;
        AllocateFrameBuffer();

        TimelineChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        ThrowIfDisposed();

        if (_source is null)
        {
            ErrorOccurred?.Invoke(this, "No source loaded.");
            return;
        }

        lock (_stateGate)
        {
            if (_isPlaying)
            {
                return;
            }

            _positionAtPlayStart = _position;
            _playbackStartedUtc = DateTime.UtcNow;
            _isPlaying = true;
        }

        StartProcesses(_source, _position);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        ThrowIfDisposed();

        lock (_stateGate)
        {
            if (!_isPlaying)
            {
                return;
            }

            _position = Position;
            _isPlaying = false;
        }

        StopProcesses(resetPosition: false);
        TimelineChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        ThrowIfDisposed();
        lock (_stateGate)
        {
            _isPlaying = false;
            _position = TimeSpan.Zero;
        }

        StopProcesses(resetPosition: false);
        TimelineChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        ThrowIfDisposed();

        if (_source is null)
        {
            return;
        }

        var duration = Duration;
        var clamped = duration > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Clamp(position.TotalMilliseconds, 0, duration.TotalMilliseconds))
            : TimeSpan.FromMilliseconds(Math.Max(0, position.TotalMilliseconds));

        lock (_stateGate)
        {
            _position = clamped;
            _positionAtPlayStart = clamped;
            _playbackStartedUtc = DateTime.UtcNow;
        }

        if (IsPlaying)
        {
            StopProcesses(resetPosition: false);
            StartProcesses(_source, clamped);
        }

        TimelineChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(float volume)
    {
        // Not supported in ffmpeg pipe mode.
    }

    public void SetMuted(bool muted)
    {
        // Not supported in ffmpeg pipe mode.
    }

    public void SetLooping(bool looping)
    {
        _looping = looping;
    }

    public bool TryAcquireFrame(out MediaFrameLease frame)
    {
        frame = default;

        if (_disposed || !_pinnedFrameBuffer.IsAllocated || _frameBuffer is null)
        {
            return false;
        }

        Monitor.Enter(_frameGate);
        frame = new MediaFrameLease(
            _frameGate,
            _pinnedFrameBuffer.AddrOfPinnedObject(),
            _frameWidth,
            _frameHeight,
            _frameStride,
            MediaFramePixelFormat.Rgba32,
            LatestFrameSequence);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopProcesses(resetPosition: false);
        ReleaseFrameBuffer();
    }

    private void StartProcesses(Uri source, TimeSpan startPosition)
    {
        _decodeCts = new CancellationTokenSource();
        _videoProcess = StartVideoProcess(source, startPosition);

        if (_ffplayAvailable)
        {
            _audioProcess = TryStartAudioProcess(source, startPosition);
        }

        _decodeTask = Task.Run(() => ReadFramesLoopAsync(_videoProcess, _decodeCts.Token));
    }

    private void StopProcesses(bool resetPosition)
    {
        _decodeCts?.Cancel();

        KillProcess(_videoProcess);
        KillProcess(_audioProcess);

        _videoProcess = null;
        _audioProcess = null;

        if (_decodeTask is not null)
        {
            try
            {
                _decodeTask.Wait(TimeSpan.FromMilliseconds(40));
            }
            catch
            {
                // Ignore shutdown races.
            }
        }

        _decodeTask = null;
        _decodeCts?.Dispose();
        _decodeCts = null;

        if (resetPosition)
        {
            lock (_stateGate)
            {
                _position = TimeSpan.Zero;
            }
        }
    }

    private Process StartVideoProcess(Uri source, TimeSpan startPosition)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-hwaccel");
        psi.ArgumentList.Add("auto");

        if (startPosition > TimeSpan.Zero)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(startPosition.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        psi.ArgumentList.Add("-re");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(source.IsFile ? source.LocalPath : source.ToString());
        psi.ArgumentList.Add("-an");
        psi.ArgumentList.Add("-sn");
        psi.ArgumentList.Add("-dn");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("rgba");
        psi.ArgumentList.Add("pipe:1");

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Unable to start ffmpeg process.");

        _ = Task.Run(async () =>
        {
            try
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    ErrorOccurred?.Invoke(this, stderr.Trim());
                }
            }
            catch
            {
                // Ignore.
            }
        });

        return process;
    }

    private Process? TryStartAudioProcess(Uri source, TimeSpan startPosition)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffplay",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add("-nodisp");
            psi.ArgumentList.Add("-autoexit");

            if (startPosition > TimeSpan.Zero)
            {
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(startPosition.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            psi.ArgumentList.Add(source.IsFile ? source.LocalPath : source.ToString());
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    private async Task ReadFramesLoopAsync(Process process, CancellationToken cancellationToken)
    {
        var frameBytes = checked(_frameStride * _frameHeight);
        var stream = process.StandardOutput.BaseStream;
        var scratch = new byte[frameBytes];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await ReadExactlyAsync(stream, scratch, frameBytes, cancellationToken).ConfigureAwait(false);
            if (read < frameBytes)
            {
                break;
            }

            Monitor.Enter(_frameGate);
            try
            {
                if (_frameBuffer is not null)
                {
                    Buffer.BlockCopy(scratch, 0, _frameBuffer, 0, frameBytes);
                    Interlocked.Increment(ref _latestFrameSequence);
                }
            }
            finally
            {
                Monitor.Exit(_frameGate);
            }

            FrameReady?.Invoke(this, EventArgs.Empty);
            TimelineChanged?.Invoke(this, EventArgs.Empty);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (_looping && _source is not null)
        {
            lock (_stateGate)
            {
                _position = TimeSpan.Zero;
                _positionAtPlayStart = TimeSpan.Zero;
                _playbackStartedUtc = DateTime.UtcNow;
            }

            StopProcesses(resetPosition: false);
            StartProcesses(_source, TimeSpan.Zero);
            return;
        }

        lock (_stateGate)
        {
            _isPlaying = false;
            _position = _duration > TimeSpan.Zero ? _duration : _position;
        }

        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        TimelineChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, int expected, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < expected && !cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, expected - total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private bool TryProbeSource(Uri source, out int width, out int height, out TimeSpan duration, out string error)
    {
        width = 0;
        height = 0;
        duration = TimeSpan.Zero;
        error = string.Empty;

        if (!IsToolAvailable("ffprobe"))
        {
            error = "ffprobe is required for FFmpeg fallback backend.";
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("stream=width,height:format=duration");
        psi.ArgumentList.Add("-select_streams");
        psi.ArgumentList.Add("v:0");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add(source.IsFile ? source.LocalPath : source.ToString());

        using var process = Process.Start(psi);
        if (process is null)
        {
            error = "Unable to start ffprobe.";
            return false;
        }

        var output = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            error = string.IsNullOrWhiteSpace(stderr) ? "ffprobe failed to inspect media source." : stderr.Trim();
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;

            if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            {
                error = "No video streams found.";
                return false;
            }

            var stream = streams[0];
            width = stream.TryGetProperty("width", out var widthProp) ? widthProp.GetInt32() : 0;
            height = stream.TryGetProperty("height", out var heightProp) ? heightProp.GetInt32() : 0;

            if (root.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var durationProp)
                && double.TryParse(durationProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSec)
                && durationSec > 0)
            {
                duration = TimeSpan.FromSeconds(durationSec);
            }

            if (width <= 0 || height <= 0)
            {
                error = "Could not determine video dimensions.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Unable to parse ffprobe output: {ex.Message}";
            return false;
        }
    }

    private static bool IsToolAvailable(string toolName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = toolName,
                ArgumentList = { "-version" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(1000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void AllocateFrameBuffer()
    {
        lock (_frameGate)
        {
            ReleaseFrameBuffer();
            _frameBuffer = new byte[checked(_frameStride * _frameHeight)];
            _pinnedFrameBuffer = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);
            Interlocked.Exchange(ref _latestFrameSequence, 0);
        }
    }

    private void ReleaseFrameBuffer()
    {
        lock (_frameGate)
        {
            if (_pinnedFrameBuffer.IsAllocated)
            {
                _pinnedFrameBuffer.Free();
            }

            _frameBuffer = null;
        }
    }

    private static void KillProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(60);
            }
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            process.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
