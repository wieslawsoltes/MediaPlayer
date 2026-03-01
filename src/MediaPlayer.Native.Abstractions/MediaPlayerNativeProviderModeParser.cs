using System;

namespace MediaPlayer.Native.Abstractions;

public static class MediaPlayerNativeProviderModeParser
{
    public static bool TryParse(string? raw, out MediaPlayerNativeProviderMode mode)
    {
        mode = MediaPlayerNativeProviderMode.AutoPreferInterop;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "legacy":
            case "legacyhelpers":
            case "legacy-helper":
            case "legacy_helpers":
            case "0":
                mode = MediaPlayerNativeProviderMode.LegacyHelpers;
                return true;
            case "interop":
            case "interoponly":
            case "interop-only":
            case "interop_only":
            case "1":
                mode = MediaPlayerNativeProviderMode.InteropOnly;
                return true;
            case "bindings":
            case "nativebindings":
            case "native-bindings":
            case "native_bindings":
            case "bindingsonly":
            case "2":
                mode = MediaPlayerNativeProviderMode.NativeBindingsOnly;
                return true;
            case "auto":
            case "autopreferinterop":
            case "auto-prefer-interop":
            case "auto_prefer_interop":
            case "3":
                mode = MediaPlayerNativeProviderMode.AutoPreferInterop;
                return true;
            case "autopreferbindings":
            case "auto-prefer-bindings":
            case "auto_prefer_bindings":
            case "4":
                mode = MediaPlayerNativeProviderMode.AutoPreferBindings;
                return true;
            default:
                if (Enum.TryParse(raw, ignoreCase: true, out MediaPlayerNativeProviderMode parsedMode)
                    && Enum.IsDefined(parsedMode))
                {
                    mode = parsedMode;
                    return true;
                }

                return false;
        }
    }
}
