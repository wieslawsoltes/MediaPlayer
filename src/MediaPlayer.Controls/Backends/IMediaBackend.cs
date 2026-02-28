using System;

namespace MediaPlayer.Controls.Backends;

internal interface IMediaBackend : IDisposable
{
    event EventHandler? FrameReady;
    event EventHandler? PlaybackStateChanged;
    event EventHandler? TimelineChanged;
    event EventHandler<string>? ErrorOccurred;

    string ActiveProfileName { get; }
    string ActiveDecodeApi { get; }
    string ActiveRenderPath { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int VideoWidth { get; }
    int VideoHeight { get; }
    long LatestFrameSequence { get; }

    void Open(Uri source);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void SetVolume(float volume);
    void SetMuted(bool muted);
    void SetLooping(bool looping);
    bool TryAcquireFrame(out MediaFrameLease frame);
}
