using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MediaPlayer.Controls.Backends;

internal sealed class MacOsNativeMediaBackend : NativeFramePumpMediaBackend
{
    private static readonly object s_helperBuildGate = new();
    private static readonly object s_cacheGate = new();
    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };
    private static string? s_helperPath;
    private static string? s_helperBuildError;

    protected override string ProfileName => "macOS Native AVFoundation";

    protected override string NativeDecodeApi => "AVFoundation/VideoToolbox (native GPU decode, CPU fallback by platform)";

    protected override string NativeRenderPath => "AVFoundation frame output -> managed BGRA frame pump -> Avalonia OpenGL texture";

    public MacOsNativeMediaBackend()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("MacOsNativeMediaBackend can only be used on macOS.");
        }
    }

    protected override bool TryEnsureHelperReady(out string error)
    {
        lock (s_helperBuildGate)
        {
            if (!string.IsNullOrWhiteSpace(s_helperPath) && File.Exists(s_helperPath))
            {
                error = string.Empty;
                return true;
            }

            var helperRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MediaPlayer",
                "native-helpers",
                "macos");
            Directory.CreateDirectory(helperRoot);

            var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SwiftHelperSource)));
            var binaryPath = Path.Combine(helperRoot, $"avfoundation-frame-pump-{sourceHash}");
            if (!File.Exists(binaryPath))
            {
                // Build in a private directory on the cache volume so publication is atomic.
                var buildRoot = Directory.CreateDirectory(Path.Combine(helperRoot, $"build-{Guid.NewGuid():N}")).FullName;
                try
                {
                    var sourcePath = Path.Combine(buildRoot, "avfoundation-frame-pump.swift");
                    var outputPath = Path.Combine(buildRoot, "avfoundation-frame-pump");
                    File.WriteAllText(sourcePath, SwiftHelperSource);

                    if (!TryCompileSwiftHelper(sourcePath, outputPath, out var buildError))
                    {
                        s_helperBuildError = buildError;
                        error = buildError;
                        return false;
                    }

                    File.Move(outputPath, binaryPath, overwrite: true);
                }
                finally
                {
                    Directory.Delete(buildRoot, recursive: true);
                }
            }

            s_helperPath = binaryPath;
            s_helperBuildError = string.Empty;
            error = string.Empty;
            return true;
        }
    }

    protected override ProcessStartInfo CreateProbeProcess(Uri source)
    {
        var psi = CreateToolProcess(s_helperPath!);
        psi.ArgumentList.Add("probe");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(ResolveMediaSource(source));
        return psi;
    }

    protected override ProcessStartInfo CreatePlaybackProcess(
        Uri source,
        TimeSpan startPosition,
        double playbackRate,
        float volume,
        bool muted)
    {
        var psi = CreateToolProcess(s_helperPath!);
        psi.ArgumentList.Add("play");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(ResolveMediaSource(source));
        psi.ArgumentList.Add("--start");
        psi.ArgumentList.Add(FormatSeconds(startPosition));
        psi.ArgumentList.Add("--speed");
        psi.ArgumentList.Add(playbackRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--volume");
        psi.ArgumentList.Add(Math.Clamp(volume, 0f, 100f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--mute");
        psi.ArgumentList.Add(muted ? "1" : "0");
        return psi;
    }

    protected override ProcessStartInfo CreateSingleFrameProcess(Uri source, TimeSpan position)
    {
        var psi = CreateToolProcess(s_helperPath!);
        psi.ArgumentList.Add("frame");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(ResolveMediaSource(source));
        psi.ArgumentList.Add("--time");
        psi.ArgumentList.Add(FormatSeconds(position));
        return psi;
    }

    private static string ResolveMediaSource(Uri source)
    {
        if (source.IsFile)
        {
            return source.LocalPath;
        }

        if (source.Scheme == Uri.UriSchemeHttp || source.Scheme == Uri.UriSchemeHttps)
        {
            return EnsureRemoteMediaCached(source);
        }

        return source.ToString();
    }

    private static string EnsureRemoteMediaCached(Uri source)
    {
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MediaPlayer",
            "native-helpers",
            "macos",
            "cache");
        Directory.CreateDirectory(cacheRoot);

        var key = source.ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        var extension = Path.GetExtension(source.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            extension = ".mp4";
        }

        var destinationPath = Path.Combine(cacheRoot, $"{hash}{extension}");
        lock (s_cacheGate)
        {
            if (File.Exists(destinationPath))
            {
                return destinationPath;
            }
        }

        var tempPath = destinationPath + ".part";
        using var response = s_httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var sourceStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using (var target = File.Create(tempPath))
        {
            sourceStream.CopyTo(target);
        }

        lock (s_cacheGate)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(tempPath, destinationPath, overwrite: true);
            }
            else
            {
                File.Delete(tempPath);
            }
        }

        return destinationPath;
    }

    private static bool TryCompileSwiftHelper(string sourcePath, string outputPath, out string error)
    {
        error = string.Empty;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xcrun",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("swiftc");
            psi.ArgumentList.Add("-O");
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add("-framework");
            psi.ArgumentList.Add("AVFoundation");
            psi.ArgumentList.Add("-framework");
            psi.ArgumentList.Add("CoreVideo");
            psi.ArgumentList.Add("-framework");
            psi.ArgumentList.Add("CoreMedia");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputPath);

            using var process = Process.Start(psi);
            if (process is null)
            {
                error = "Unable to start xcrun for AVFoundation helper build.";
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr)
                    ? $"xcrun swiftc failed. {stdout}".Trim()
                    : stderr.Trim();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Unable to compile macOS native helper: {ex.Message}";
            return false;
        }
    }

    private const string SwiftHelperSource = """
import Foundation
import AVFoundation
import CoreMedia
import CoreVideo
import QuartzCore

struct ProbeInfo: Codable {
    let width: Int
    let height: Int
    let duration: Double
    let frameRate: Double
}

enum HelperError: Error {
    case invalidArgs(String)
    case mediaOpen(String)
    case readerInit(String)
    case streamUnavailable(String)
}

func value(for flag: String, args: [String]) -> String? {
    guard let idx = args.firstIndex(of: flag), idx + 1 < args.count else {
        return nil
    }
    return args[idx + 1]
}

func mediaURL(from source: String) -> URL {
    if source.hasPrefix("http://") || source.hasPrefix("https://") || source.hasPrefix("file://") {
        return URL(string: source) ?? URL(fileURLWithPath: source)
    }
    return URL(fileURLWithPath: source)
}

func waitForAsset(_ asset: AVAsset, keys: [String]) throws {
    let semaphore = DispatchSemaphore(value: 0)
    asset.loadValuesAsynchronously(forKeys: keys) {
        semaphore.signal()
    }
    semaphore.wait()

    for key in keys {
        var error: NSError?
        let status = asset.statusOfValue(forKey: key, error: &error)
        if status != .loaded {
            throw error ?? HelperError.mediaOpen("Asset key \(key) failed to load.")
        }
    }
}

func loadVideoTrack(_ asset: AVAsset) throws -> AVAssetTrack {
    try waitForAsset(asset, keys: ["tracks", "duration"])
    guard let track = asset.tracks(withMediaType: .video).first else {
        throw HelperError.mediaOpen("No video track found.")
    }
    return track
}

func transformedSize(for track: AVAssetTrack) -> (Int, Int) {
    let transformed = track.naturalSize.applying(track.preferredTransform)
    let width = max(1, Int(abs(transformed.width).rounded()))
    let height = max(1, Int(abs(transformed.height).rounded()))
    return (width, height)
}

func probe(source: String) throws {
    let url = mediaURL(from: source)
    let asset = AVURLAsset(url: url)
    let track = try loadVideoTrack(asset)
    let (width, height) = transformedSize(for: track)
    let durationSeconds = max(0.0, CMTimeGetSeconds(asset.duration))
    let fps = max(0.0, Double(track.nominalFrameRate))
    let payload = ProbeInfo(width: width, height: height, duration: durationSeconds, frameRate: fps)
    let data = try JSONEncoder().encode(payload)
    FileHandle.standardOutput.write(data)
}

func createReader(asset: AVAsset, track: AVAssetTrack, startSeconds: Double) throws -> (AVAssetReader, AVAssetReaderTrackOutput) {
    let reader = try AVAssetReader(asset: asset)
    let settings: [String: Any] = [
        kCVPixelBufferPixelFormatTypeKey as String: Int(kCVPixelFormatType_32BGRA)
    ]
    let output = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
    output.alwaysCopiesSampleData = false
    guard reader.canAdd(output) else {
        throw HelperError.readerInit("Unable to add track output.")
    }
    reader.add(output)

    if startSeconds > 0.0 {
        let start = CMTime(seconds: startSeconds, preferredTimescale: 600)
        let remaining = CMTimeSubtract(asset.duration, start)
        if CMTimeCompare(remaining, .zero) > 0 {
            reader.timeRange = CMTimeRange(start: start, duration: remaining)
        }
    }

    guard reader.startReading() else {
        throw HelperError.readerInit("Reader failed to start.")
    }

    return (reader, output)
}

func writeSample(_ sample: CMSampleBuffer, width: Int, height: Int, reusableBuffer: inout Data) throws {
    guard let pixelBuffer = CMSampleBufferGetImageBuffer(sample) else {
        throw HelperError.streamUnavailable("No image buffer in sample.")
    }

    CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
    defer { CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly) }

    guard let baseAddress = CVPixelBufferGetBaseAddress(pixelBuffer) else {
        throw HelperError.streamUnavailable("No base address in pixel buffer.")
    }

    let srcWidth = CVPixelBufferGetWidth(pixelBuffer)
    let srcHeight = CVPixelBufferGetHeight(pixelBuffer)
    let srcStride = CVPixelBufferGetBytesPerRow(pixelBuffer)
    let targetStride = width * 4
    let copyWidthBytes = min(width, srcWidth) * 4
    let copyRows = min(height, srcHeight)

    if reusableBuffer.count != targetStride * height {
        reusableBuffer = Data(count: targetStride * height)
    }

    reusableBuffer.withUnsafeMutableBytes { dstRaw in
        guard let dstBase = dstRaw.baseAddress else { return }
        for y in 0..<copyRows {
            let src = baseAddress.advanced(by: y * srcStride)
            let dst = dstBase.advanced(by: y * targetStride)
            memcpy(dst, src, copyWidthBytes)

            if copyWidthBytes < targetStride {
                memset(dst.advanced(by: copyWidthBytes), 0, targetStride - copyWidthBytes)
            }
        }

        if copyRows < height {
            let tail = dstBase.advanced(by: copyRows * targetStride)
            memset(tail, 0, (height - copyRows) * targetStride)
        }
    }

    FileHandle.standardOutput.write(reusableBuffer)
}

func parseBool(_ value: String?) -> Bool {
    guard let value else { return false }
    switch value.lowercased() {
    case "1", "true", "yes", "y":
        return true
    default:
        return false
    }
}

func pumpRunLoopSlice(_ interval: TimeInterval) {
    let bounded = max(0.0005, min(interval, 0.05))
    let deadline = Date(timeIntervalSinceNow: bounded)
    _ = RunLoop.current.run(mode: .default, before: deadline)
}

func waitForPlayerReady(_ player: AVPlayer, timeoutSeconds: Double) throws {
    guard let item = player.currentItem else {
        throw HelperError.mediaOpen("AVPlayer item is unavailable.")
    }

    let timeout = Date(timeIntervalSinceNow: max(0.5, timeoutSeconds))
    while item.status == .unknown {
        if Date() >= timeout {
            throw HelperError.mediaOpen("Timed out waiting for AVPlayer item readiness.")
        }

        pumpRunLoopSlice(0.01)
    }

    if item.status == .failed {
        let reason = item.error?.localizedDescription ?? "AVPlayer item failed to load."
        throw HelperError.mediaOpen(reason)
    }
}

func createAudioPlayer(source: String, startSeconds: Double, volume: Double, muted: Bool) -> AVPlayer {
    let item = AVPlayerItem(url: mediaURL(from: source))
    let player = AVPlayer(playerItem: item)
    player.isMuted = muted
    player.volume = Float(max(0.0, min(1.0, volume / 100.0)))

    let start = CMTime(seconds: max(0.0, startSeconds), preferredTimescale: 600)
    if CMTimeCompare(start, .zero) > 0 {
        let semaphore = DispatchSemaphore(value: 0)
        player.seek(to: start, toleranceBefore: .zero, toleranceAfter: .zero) { _ in
            semaphore.signal()
        }
        _ = semaphore.wait(timeout: DispatchTime.now() + .seconds(2))
    }

    return player
}

func streamFrames(source: String, startSeconds: Double, singleFrameOnly: Bool, speed: Double, volume: Double, muted: Bool) throws {
    let url = mediaURL(from: source)
    let asset = AVURLAsset(url: url)
    let track = try loadVideoTrack(asset)
    let (width, height) = transformedSize(for: track)
    let (_, output) = try createReader(asset: asset, track: track, startSeconds: startSeconds)

    var reusable = Data()
    var firstPts: Double?
    var firstAudioTime: Double?
    let wallStart = CACurrentMediaTime()
    var audioPlayer: AVPlayer?
    var useAudioClock = false
    var didWarnAudioClockFallback = false

    if !singleFrameOnly {
        let player = createAudioPlayer(source: source, startSeconds: startSeconds, volume: volume, muted: muted)
        try waitForPlayerReady(player, timeoutSeconds: 5.0)
        audioPlayer = player
        player.playImmediately(atRate: Float(max(0.1, speed)))
        useAudioClock = true
    }

    while let sample = output.copyNextSampleBuffer() {
        let pts = CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sample))
        if !singleFrameOnly {
            if firstPts == nil {
                firstPts = pts
                if let player = audioPlayer {
                    firstAudioTime = CMTimeGetSeconds(player.currentTime())
                }
            }

            if useAudioClock, let player = audioPlayer {
                guard let basePts = firstPts else {
                    throw HelperError.streamUnavailable("Unable to initialize audio/video sync.")
                }

                let targetRelative = max(0.0, pts - basePts)
                let baseAudioTime = firstAudioTime ?? CMTimeGetSeconds(player.currentTime())
                var previousAudioRelative = -1.0
                var stagnationCount = 0

                while true {
                    if let item = player.currentItem, item.status == .failed {
                        let reason = item.error?.localizedDescription ?? "AVPlayer audio output failed."
                        throw HelperError.streamUnavailable(reason)
                    }

                    let currentAudioTime = CMTimeGetSeconds(player.currentTime())
                    let audioRelative = max(0.0, currentAudioTime - baseAudioTime)
                    if audioRelative + 0.012 >= targetRelative {
                        break
                    }

                    if abs(audioRelative - previousAudioRelative) < 0.0005 {
                        stagnationCount += 1
                    } else {
                        stagnationCount = 0
                    }

                    if stagnationCount >= 32 {
                        useAudioClock = false
                        if !didWarnAudioClockFallback {
                            didWarnAudioClockFallback = true
                            if let warningData = "Audio clock stalled. Falling back to wall-clock sync.\n".data(using: .utf8) {
                                FileHandle.standardError.write(warningData)
                            }
                        }
                        break
                    }

                    previousAudioRelative = audioRelative
                    let remaining = targetRelative - audioRelative
                    pumpRunLoopSlice(min(0.008, max(0.001, remaining)))
                }
            }

            if !useAudioClock, let first = firstPts {
                let target = (pts - first) / max(0.1, speed)
                let elapsed = CACurrentMediaTime() - wallStart
                if target > elapsed {
                    Thread.sleep(forTimeInterval: target - elapsed)
                }
            }
        }

        try writeSample(sample, width: width, height: height, reusableBuffer: &reusable)
        if singleFrameOnly {
            break
        }

        pumpRunLoopSlice(0.0015)
    }

    audioPlayer?.pause()
    audioPlayer = nil
}

func run() throws {
    let args = CommandLine.arguments
    guard args.count >= 2 else {
        throw HelperError.invalidArgs("Missing command.")
    }

    let command = args[1].lowercased()
    let source = value(for: "--source", args: args) ?? ""
    if source.isEmpty {
        throw HelperError.invalidArgs("Missing --source argument.")
    }

    switch command {
    case "probe":
        try probe(source: source)
    case "play":
        let start = Double(value(for: "--start", args: args) ?? "0") ?? 0
        let speed = Double(value(for: "--speed", args: args) ?? "1") ?? 1
        let volume = Double(value(for: "--volume", args: args) ?? "100") ?? 100
        let muted = parseBool(value(for: "--mute", args: args))
        try streamFrames(
            source: source,
            startSeconds: max(0, start),
            singleFrameOnly: false,
            speed: max(0.1, speed),
            volume: max(0, min(100, volume)),
            muted: muted)
    case "frame":
        let at = Double(value(for: "--time", args: args) ?? "0") ?? 0
        try streamFrames(source: source, startSeconds: max(0, at), singleFrameOnly: true, speed: 1, volume: 100, muted: false)
    default:
        throw HelperError.invalidArgs("Unknown command: \(command)")
    }
}

do {
    try run()
} catch {
    let message = String(describing: error)
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(1)
}
""";
}
