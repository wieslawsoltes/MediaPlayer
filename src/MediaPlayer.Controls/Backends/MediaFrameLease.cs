using System;
using System.Threading;

namespace MediaPlayer.Controls.Backends;

internal enum MediaFramePixelFormat
{
    Bgra32,
    Rgba32
}

internal readonly struct MediaFrameLease(
    object frameGate,
    IntPtr data,
    int width,
    int height,
    int stride,
    MediaFramePixelFormat pixelFormat,
    long sequence) : IDisposable
{
    private readonly object _frameGate = frameGate;

    public IntPtr Data { get; } = data;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = stride;
    public MediaFramePixelFormat PixelFormat { get; } = pixelFormat;
    public long Sequence { get; } = sequence;

    public void Dispose()
    {
        Monitor.Exit(_frameGate);
    }
}
