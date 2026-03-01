using System.Collections.Generic;
using MediaPlayer.Native.Abstractions;
using MediaPlayer.Native.Interop;

namespace MediaPlayer.Controls.Backends;

internal static class MediaBackendSelectionPolicy
{
    public static MediaBackendSelectionResult Build(
        MediaPlayerNativeProviderMode mode,
        MediaBackendSelectionPlatform platform,
        IReadOnlyList<MediaPlayerInteropPlaybackProviderDescriptor> interopProviders)
    {
        var warnings = new List<string>();
        var candidates = new List<MediaBackendCandidate>();

        if (platform == MediaBackendSelectionPlatform.MacOs)
        {
            switch (mode)
            {
                case MediaPlayerNativeProviderMode.InteropOnly:
                    {
                        var hasInterop = AddInteropCandidates(candidates, warnings, interopProviders);
                        if (!hasInterop)
                        {
                            warnings.Add("InteropOnly mode is configured, but no interop playback provider is currently available. Falling back to legacy providers.");
                        }

                        candidates.Add(new MediaBackendCandidate("macOS Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.MacOsNativeHelper));
                        candidates.Add(new MediaBackendCandidate("macOS FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.MacOsFfmpegProfile));
                        candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                        break;
                    }
                case MediaPlayerNativeProviderMode.NativeBindingsOnly:
                    warnings.Add("NativeBindingsOnly mode is configured, but native bindings provider is not implemented yet. Trying interop provider then legacy providers.");
                    _ = AddInteropCandidates(candidates, warnings, interopProviders);
                    candidates.Add(new MediaBackendCandidate("macOS Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.MacOsNativeHelper));
                    candidates.Add(new MediaBackendCandidate("macOS FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.MacOsFfmpegProfile));
                    candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                    break;
                case MediaPlayerNativeProviderMode.AutoPreferBindings:
                    warnings.Add("AutoPreferBindings mode is configured, but native bindings provider is not implemented yet. Using legacy provider first, then interop fallback.");
                    candidates.Add(new MediaBackendCandidate("macOS Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.MacOsNativeHelper));
                    candidates.Add(new MediaBackendCandidate("macOS FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.MacOsFfmpegProfile));
                    _ = AddInteropCandidates(candidates, warnings, interopProviders);
                    candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                    break;
                case MediaPlayerNativeProviderMode.LegacyHelpers:
                    candidates.Add(new MediaBackendCandidate("macOS Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.MacOsNativeHelper));
                    candidates.Add(new MediaBackendCandidate("macOS FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.MacOsFfmpegProfile));
                    _ = AddInteropCandidates(candidates, warnings, interopProviders);
                    candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                    break;
                default:
                    {
                        var hasInterop = AddInteropCandidates(candidates, warnings, interopProviders);
                        if (!hasInterop)
                        {
                            warnings.Add("AutoPreferInterop mode is configured, but no interop playback provider is currently available. Using legacy providers.");
                        }

                        candidates.Add(new MediaBackendCandidate("macOS Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.MacOsNativeHelper));
                        candidates.Add(new MediaBackendCandidate("macOS FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.MacOsFfmpegProfile));
                        candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                        break;
                    }
            }

            return new MediaBackendSelectionResult(candidates, BuildWarning(warnings));
        }

        if (platform == MediaBackendSelectionPlatform.Windows)
        {
            switch (mode)
            {
                case MediaPlayerNativeProviderMode.InteropOnly:
                    {
                        var hasInterop = AddInteropCandidates(candidates, warnings, interopProviders);
                        if (!hasInterop)
                        {
                            warnings.Add("InteropOnly mode is configured, but no interop playback provider is currently available. Falling back to legacy providers.");
                        }

                        candidates.Add(new MediaBackendCandidate("Windows Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.WindowsNativeHelper));
                        candidates.Add(new MediaBackendCandidate("Windows FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.WindowsFfmpegProfile));
                        candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                        break;
                    }
                case MediaPlayerNativeProviderMode.NativeBindingsOnly:
                    warnings.Add("NativeBindingsOnly mode is configured, but native bindings provider is not implemented yet. Trying interop provider then legacy providers.");
                    _ = AddInteropCandidates(candidates, warnings, interopProviders);
                    candidates.Add(new MediaBackendCandidate("Windows Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.WindowsNativeHelper));
                    candidates.Add(new MediaBackendCandidate("Windows FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.WindowsFfmpegProfile));
                    candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                    break;
                case MediaPlayerNativeProviderMode.AutoPreferBindings:
                    warnings.Add("AutoPreferBindings mode is configured, but native bindings provider is not implemented yet. Using legacy provider first, then interop fallback.");
                    candidates.Add(new MediaBackendCandidate("Windows Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.WindowsNativeHelper));
                    candidates.Add(new MediaBackendCandidate("Windows FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.WindowsFfmpegProfile));
                    _ = AddInteropCandidates(candidates, warnings, interopProviders);
                    candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                    break;
                case MediaPlayerNativeProviderMode.LegacyHelpers:
                    candidates.Add(new MediaBackendCandidate("Windows Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.WindowsNativeHelper));
                    candidates.Add(new MediaBackendCandidate("Windows FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.WindowsFfmpegProfile));
                    _ = AddInteropCandidates(candidates, warnings, interopProviders);
                    candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                    break;
                default:
                    {
                        var hasInterop = AddInteropCandidates(candidates, warnings, interopProviders);
                        if (!hasInterop)
                        {
                            warnings.Add("AutoPreferInterop mode is configured, but no interop playback provider is currently available. Using legacy providers.");
                        }

                        candidates.Add(new MediaBackendCandidate("Windows Native Helper", MediaPlayerNativeProviderKind.LegacyHelper, MediaBackendKind.WindowsNativeHelper));
                        candidates.Add(new MediaBackendCandidate("Windows FFmpeg native profile", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.WindowsFfmpegProfile));
                        candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
                        break;
                    }
            }

            return new MediaBackendSelectionResult(candidates, BuildWarning(warnings));
        }

        if (mode == MediaPlayerNativeProviderMode.NativeBindingsOnly)
        {
            warnings.Add("NativeBindingsOnly mode is configured, but native bindings provider is not implemented on this platform. Trying interop provider and FFmpeg fallback.");
        }
        else if (mode is MediaPlayerNativeProviderMode.LegacyHelpers or MediaPlayerNativeProviderMode.AutoPreferBindings)
        {
            warnings.Add($"Native provider mode '{mode}' is not supported on this platform. Trying interop provider and FFmpeg fallback.");
        }
        else if (mode == MediaPlayerNativeProviderMode.InteropOnly)
        {
            warnings.Add("InteropOnly mode is configured for this platform.");
        }

        var addedInterop = AddInteropCandidates(candidates, warnings, interopProviders);
        if (mode == MediaPlayerNativeProviderMode.InteropOnly && !addedInterop)
        {
            warnings.Add("No interop playback providers are available on this platform.");
        }

        candidates.Add(new MediaBackendCandidate("FFmpeg fallback", MediaPlayerNativeProviderKind.FfmpegFallback, MediaBackendKind.FfmpegFallback));
        return new MediaBackendSelectionResult(candidates, BuildWarning(warnings));
    }

    private static bool AddInteropCandidates(
        List<MediaBackendCandidate> candidates,
        List<string> warnings,
        IReadOnlyList<MediaPlayerInteropPlaybackProviderDescriptor> interopProviders)
    {
        var addedAny = false;

        foreach (var descriptor in interopProviders)
        {
            if (!descriptor.IsAvailable)
            {
                if (!string.IsNullOrWhiteSpace(descriptor.UnavailableReason))
                {
                    warnings.Add(descriptor.UnavailableReason);
                }

                continue;
            }

            if (descriptor.Id == MediaPlayerInteropPlaybackProviderId.LibVlcManagedInterop)
            {
                candidates.Add(new MediaBackendCandidate(
                    descriptor.Name,
                    descriptor.ProviderKind,
                    MediaBackendKind.LibVlcInterop));
                addedAny = true;
                continue;
            }

            warnings.Add($"Unknown interop playback provider id '{descriptor.Id}' was skipped.");
        }

        return addedAny;
    }

    private static string BuildWarning(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" | ", warnings);
    }
}
