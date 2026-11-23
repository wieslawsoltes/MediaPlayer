using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MediaPlayer.Controls.Backends;

internal sealed class WindowsNativeMediaBackend : NativeFramePumpMediaBackend
{
    private static readonly object s_helperBuildGate = new();
    private static string? s_helperDllPath;
    private static string? s_helperBuildError;

    protected override string ProfileName => "Windows Native MediaFoundation";

    protected override string NativeDecodeApi => "MediaFoundation/Direct3D (native GPU decode, CPU fallback by platform)";

    protected override string NativeRenderPath => "MediaFoundation frame pump -> managed BGRA frame buffer -> Avalonia OpenGL texture";

    public WindowsNativeMediaBackend()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("WindowsNativeMediaBackend can only be used on Windows.");
        }
    }

    protected override bool TryEnsureHelperReady(out string error)
    {
        lock (s_helperBuildGate)
        {
            if (!string.IsNullOrWhiteSpace(s_helperDllPath) && File.Exists(s_helperDllPath))
            {
                error = string.Empty;
                return true;
            }

            var helperRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MediaPlayer",
                "native-helpers",
                "windows");
            Directory.CreateDirectory(helperRoot);

            var projectPath = Path.Combine(helperRoot, "MediaPlayer.WindowsNativeFramePump.csproj");
            var sourcePath = Path.Combine(helperRoot, "Program.cs");
            File.WriteAllText(projectPath, WindowsHelperProject);
            File.WriteAllText(sourcePath, WindowsHelperSource);

            var publishDir = Path.Combine(helperRoot, "publish");
            var helperDllPath = Path.Combine(publishDir, "MediaPlayer.WindowsNativeFramePump.dll");
            if (!File.Exists(helperDllPath)
                || File.GetLastWriteTimeUtc(helperDllPath) < File.GetLastWriteTimeUtc(projectPath)
                || File.GetLastWriteTimeUtc(helperDllPath) < File.GetLastWriteTimeUtc(sourcePath))
            {
                if (!TryBuildWindowsHelper(projectPath, publishDir, out var buildError))
                {
                    s_helperBuildError = buildError;
                    error = buildError;
                    return false;
                }
            }

            if (!File.Exists(helperDllPath))
            {
                error = "Windows native helper build succeeded but output DLL was not found.";
                return false;
            }

            s_helperDllPath = helperDllPath;
            s_helperBuildError = string.Empty;
            error = string.Empty;
            return true;
        }
    }

    protected override ProcessStartInfo CreateProbeProcess(Uri source)
    {
        var psi = CreateDotnetHelperProcess("probe");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(FormatMediaSource(source));
        return psi;
    }

    protected override ProcessStartInfo CreatePlaybackProcess(
        Uri source,
        TimeSpan startPosition,
        double playbackRate,
        float volume,
        bool muted)
    {
        var psi = CreateDotnetHelperProcess("play");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(FormatMediaSource(source));
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
        var psi = CreateDotnetHelperProcess("frame");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(FormatMediaSource(source));
        psi.ArgumentList.Add("--time");
        psi.ArgumentList.Add(FormatSeconds(position));
        return psi;
    }

    private static ProcessStartInfo CreateDotnetHelperProcess(string command)
    {
        var psi = CreateToolProcess(ProcessCommandResolver.ResolveDotnetHost());
        psi.ArgumentList.Add(s_helperDllPath!);
        psi.ArgumentList.Add(command);
        return psi;
    }

    private static bool TryBuildWindowsHelper(string projectPath, string outputDirectory, out string error)
    {
        error = string.Empty;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ProcessCommandResolver.ConfigureTool(psi, ProcessCommandResolver.ResolveDotnetHost());

            psi.ArgumentList.Add("publish");
            psi.ArgumentList.Add(projectPath);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Release");
            psi.ArgumentList.Add("-p:UseWPF=true");
            psi.ArgumentList.Add("-p:EnableWindowsTargeting=true");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputDirectory);

            using var process = Process.Start(psi);
            if (process is null)
            {
                error = "Unable to start dotnet publish for Windows native helper.";
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr)
                    ? $"dotnet publish failed. {stdout}".Trim()
                    : stderr.Trim();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Unable to build Windows native helper: {ex.Message}";
            return false;
        }
    }

    private const string WindowsHelperProject = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
""";

    private const string WindowsHelperSource = """
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new InvalidOperationException("Missing command.");
            }

            var command = args[0].Trim().ToLowerInvariant();
            var source = GetValue(args, "--source") ?? throw new InvalidOperationException("Missing --source argument.");
            var start = ParseDouble(GetValue(args, "--start"), 0d);
            var at = ParseDouble(GetValue(args, "--time"), 0d);
            var speed = ParseDouble(GetValue(args, "--speed"), 1d);
            var volume = ParseDouble(GetValue(args, "--volume"), 100d);
            var muted = ParseBool(GetValue(args, "--mute"));

            return command switch
            {
                "probe" => RunOnSta(() => Probe(source)),
                "play" => RunOnSta(() => Play(source, start, speed, volume, muted)),
                "frame" => RunOnSta(() => Frame(source, at)),
                _ => throw new InvalidOperationException($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunOnSta(Func<int> body)
    {
        var result = 1;
        Exception? captured = null;
        var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                done.Set();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }

            Dispatcher.Run();
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait();
        if (captured is not null)
        {
            throw captured;
        }

        return result;
    }

    private static int Probe(string source)
    {
        var player = CreatePlayer(source);
        try
        {
            WaitForOpen(player);

            var width = Math.Max(1, player.NaturalVideoWidth);
            var height = Math.Max(1, player.NaturalVideoHeight);
            var duration = player.NaturalDuration.HasTimeSpan ? Math.Max(0, player.NaturalDuration.TimeSpan.TotalSeconds) : 0d;
            var payload = JsonSerializer.Serialize(new { width, height, duration, frameRate = 0d });
            Console.Out.Write(payload);
            Console.Out.Flush();
            return 0;
        }
        finally
        {
            player.Close();
        }
    }

    private static int Play(string source, double startSeconds, double speed, double volume, bool muted)
    {
        var player = CreatePlayer(source);
        try
        {
            WaitForOpen(player);

            var width = Math.Max(1, player.NaturalVideoWidth);
            var height = Math.Max(1, player.NaturalVideoHeight);
            var frame = new byte[checked(width * height * 4)];
            var stdout = Console.OpenStandardOutput();
            var ended = false;

            player.MediaEnded += (_, _) => ended = true;
            player.MediaFailed += (_, e) => throw new InvalidOperationException(e.ErrorException?.Message ?? "Media playback failed.");
            if (startSeconds > 0)
            {
                player.Position = TimeSpan.FromSeconds(startSeconds);
            }

            if (speed > 0)
            {
                player.SpeedRatio = Math.Clamp(speed, 0.1d, 16d);
            }

            player.Volume = Math.Clamp(volume / 100d, 0d, 1d);
            player.IsMuted = muted;

            player.Play();
            var frameInterval = TimeSpan.FromMilliseconds(33 / Math.Max(0.1d, speed <= 0 ? 1d : speed));
            var stopwatch = Stopwatch.StartNew();
            var nextTick = TimeSpan.Zero;
            while (!ended)
            {
                PumpDispatcherOnce();
                if (stopwatch.Elapsed < nextTick)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (TryCaptureFrame(player, width, height, frame))
                {
                    stdout.Write(frame, 0, frame.Length);
                    stdout.Flush();
                }

                nextTick += frameInterval;
            }

            return 0;
        }
        finally
        {
            player.Close();
        }
    }

    private static int Frame(string source, double atSeconds)
    {
        var player = CreatePlayer(source);
        try
        {
            WaitForOpen(player);

            var width = Math.Max(1, player.NaturalVideoWidth);
            var height = Math.Max(1, player.NaturalVideoHeight);
            var frame = new byte[checked(width * height * 4)];
            var stdout = Console.OpenStandardOutput();

            if (atSeconds > 0)
            {
                player.Position = TimeSpan.FromSeconds(atSeconds);
            }

            player.Play();
            var deadline = DateTime.UtcNow.AddMilliseconds(240);
            while (DateTime.UtcNow < deadline)
            {
                PumpDispatcherOnce();
                Thread.Sleep(2);
            }

            player.Pause();
            if (TryCaptureFrame(player, width, height, frame))
            {
                stdout.Write(frame, 0, frame.Length);
                stdout.Flush();
            }

            return 0;
        }
        finally
        {
            player.Close();
        }
    }

    private static MediaPlayer CreatePlayer(string source)
    {
        var player = new MediaPlayer();
        var uri = CreateUri(source);
        player.Open(uri);
        return player;
    }

    private static void WaitForOpen(MediaPlayer player)
    {
        var opened = false;
        Exception? error = null;
        player.MediaOpened += (_, _) => opened = true;
        player.MediaFailed += (_, e) => error = e.ErrorException ?? new InvalidOperationException("Unable to open media.");

        var timeout = DateTime.UtcNow.AddSeconds(12);
        while (!opened && error is null)
        {
            if (DateTime.UtcNow > timeout)
            {
                throw new TimeoutException("Timed out waiting for media open.");
            }

            PumpDispatcherOnce();
            Thread.Sleep(4);
        }

        if (error is not null)
        {
            throw error;
        }
    }

    private static bool TryCaptureFrame(MediaPlayer player, int width, int height, byte[] target)
    {
        var rect = new Rect(0, 0, width, height);
        var drawing = new VideoDrawing
        {
            Player = player,
            Rect = rect
        };

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawDrawing(drawing);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.CopyPixels(target, width * 4, 0);
        return true;
    }

    private static void PumpDispatcherOnce()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static Uri CreateUri(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        return new Uri(Path.GetFullPath(source));
    }

    private static string? GetValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static double ParseDouble(string? text, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0d, value)
            : defaultValue;
    }

    private static bool ParseBool(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "1" => true,
            "true" => true,
            "yes" => true,
            "y" => true,
            _ => false
        };
    }
}
""";
}
