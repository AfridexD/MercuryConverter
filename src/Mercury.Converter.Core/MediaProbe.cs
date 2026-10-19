using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Mercury.Converter.Core;

/// <summary>Reads the length of a video or audio file so progress can show a real percentage.</summary>
public static partial class MediaProbe
{
    [GeneratedRegex(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationPattern();

    /// <summary>Returns the duration in seconds, or null if it can't be read (for example, still images).</summary>
    public static async Task<double?> GetDurationSecondsAsync(string path, ToolLocator tools, CancellationToken cancellationToken = default)
    {
        var ffmpeg = tools.Locate(Tool.FFmpeg);
        if (ffmpeg is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-protocol_whitelist");
        startInfo.ArgumentList.Add("file,pipe");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        // ffmpeg exits with an error when no output is given, but it still prints the duration first.
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var match = DurationPattern().Match(stderr);
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var total = hours * 3600 + minutes * 60 + seconds;
        return total > 0 ? total : null;
    }
}
