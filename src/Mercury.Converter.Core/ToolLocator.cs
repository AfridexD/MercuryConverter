namespace Mercury.Converter.Core;

/// <summary>
/// Finds the converter programs. Bundled copies live in a "tools" folder next to the app.
/// The system PATH is a fallback for developers.
/// </summary>
public sealed class ToolLocator(string toolsRoot)
{
    /// <summary>Default layout used by the installed app: tools\ffmpeg\ffmpeg.exe, tools\imagemagick\magick.exe, and so on.</summary>
    public static ToolLocator Default { get; } =
        new(Path.Combine(AppContext.BaseDirectory, "tools"));

    public string? Locate(Tool tool)
    {
        var (folder, exe) = tool switch
        {
            Tool.FFmpeg => ("ffmpeg", "ffmpeg.exe"),
            Tool.ImageMagick => ("imagemagick", "magick.exe"),
            Tool.CJxl => ("libjxl", "cjxl.exe"),
            Tool.DJxl => ("libjxl", "djxl.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
        };

        var bundled = Path.Combine(toolsRoot, folder, exe);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        return FindOnPath(exe);
    }

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
