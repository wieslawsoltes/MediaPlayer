using System;

namespace MediaPlayer.Controls.Backends;

#pragma warning disable CS0067
internal sealed class NullMediaBackend(string reason) : IMediaBackend
{
    private readonly string _reason = reason;

    public event EventHandler? FrameReady;
    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler<string>? ErrorOccurred;

    public string ActiveProfileName => "Unavailable";
    public string ActiveDecodeApi => "Unavailable";
    public string ActiveRenderPath => "Unavailable";
    public bool IsPlaying => false;
    public TimeSpan Position => TimeSpan.Zero;
    public TimeSpan Duration => TimeSpan.Zero;
    public int VideoWidth => 0;
    public int VideoHeight => 0;
    public long LatestFrameSequence => 0;

    public void Open(Uri source) => ErrorOccurred?.Invoke(this, _reason);

    public void Play() => ErrorOccurred?.Invoke(this, _reason);

    public void Pause()
    {
    }

    public void Stop()
    {
    }

    public void Seek(TimeSpan position)
    {
    }

    public void SetVolume(float volume)
    {
    }

    public void SetMuted(bool muted)
    {
    }

    public void SetLooping(bool looping)
    {
    }

    public bool TryAcquireFrame(out MediaFrameLease frame)
    {
        frame = default;
        return false;
    }

    public void Dispose()
    {
    }
}
#pragma warning restore CS0067
