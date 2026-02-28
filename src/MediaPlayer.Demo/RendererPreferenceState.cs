using System;
using System.IO;

namespace MediaPlayer.Demo;

internal enum RendererPreference
{
    Auto,
    OpenGl,
    Vulkan,
    Metal,
    Software
}

internal static class RendererPreferenceState
{
    private const string EnvVariable = "MEDIAPLAYER_RENDERER";
    private static readonly string PreferenceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MediaPlayer",
        "renderer-preference.txt");

    public static RendererPreference EffectivePreference { get; private set; } = RendererPreference.Auto;
    public static string EffectivePreferenceSource { get; private set; } = "Default";
    public static RendererPreference RuntimePreference { get; private set; } = RendererPreference.OpenGl;
    public static string RuntimePreferenceNote { get; private set; } = string.Empty;

    public static void Initialize(string[]? args)
    {
        if (TryGetFromArgs(args, out var preference))
        {
            EffectivePreference = preference;
            EffectivePreferenceSource = "Command line";
            return;
        }

        var fromEnv = Environment.GetEnvironmentVariable(EnvVariable);
        if (TryParse(fromEnv, out preference))
        {
            EffectivePreference = preference;
            EffectivePreferenceSource = $"Environment ({EnvVariable})";
            return;
        }

        if (TryLoadFromDisk(out preference))
        {
            EffectivePreference = preference;
            EffectivePreferenceSource = "Saved preference";
            return;
        }

        EffectivePreference = RendererPreference.Auto;
        EffectivePreferenceSource = "Default";
    }

    public static bool SavePreference(RendererPreference preference, out string error)
    {
        error = string.Empty;

        try
        {
            var directory = Path.GetDirectoryName(PreferenceFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(PreferenceFilePath, ToToken(preference));
            EffectivePreference = preference;
            EffectivePreferenceSource = "Saved preference";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string ToDisplayName(RendererPreference preference)
    {
        return preference switch
        {
            RendererPreference.OpenGl => "OpenGL",
            RendererPreference.Vulkan => "Vulkan",
            RendererPreference.Metal => "Metal",
            RendererPreference.Software => "Software",
            _ => "Auto"
        };
    }

    public static void SetRuntimePreference(RendererPreference preference, string note)
    {
        RuntimePreference = preference;
        RuntimePreferenceNote = note;
    }

    private static bool TryGetFromArgs(string[]? args, out RendererPreference preference)
    {
        preference = RendererPreference.Auto;
        if (args is null || args.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Substring("--renderer=".Length);
                if (TryParse(value, out preference))
                {
                    return true;
                }
            }
            else if (string.Equals(arg, "--renderer", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(arg, "-r", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && TryParse(args[i + 1], out preference))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryLoadFromDisk(out RendererPreference preference)
    {
        preference = RendererPreference.Auto;
        try
        {
            if (!File.Exists(PreferenceFilePath))
            {
                return false;
            }

            var text = File.ReadAllText(PreferenceFilePath);
            return TryParse(text, out preference);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParse(string? value, out RendererPreference preference)
    {
        preference = RendererPreference.Auto;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty);
        preference = normalized switch
        {
            "auto" => RendererPreference.Auto,
            "default" => RendererPreference.Auto,
            "opengl" => RendererPreference.OpenGl,
            "gl" => RendererPreference.OpenGl,
            "vulkan" => RendererPreference.Vulkan,
            "vk" => RendererPreference.Vulkan,
            "metal" => RendererPreference.Metal,
            "software" => RendererPreference.Software,
            "cpu" => RendererPreference.Software,
            _ => RendererPreference.Auto
        };

        return normalized is "auto" or "default" or "opengl" or "gl" or "vulkan" or "vk" or "metal" or "software" or "cpu";
    }

    private static string ToToken(RendererPreference preference)
    {
        return preference switch
        {
            RendererPreference.OpenGl => "opengl",
            RendererPreference.Vulkan => "vulkan",
            RendererPreference.Metal => "metal",
            RendererPreference.Software => "software",
            _ => "auto"
        };
    }
}
