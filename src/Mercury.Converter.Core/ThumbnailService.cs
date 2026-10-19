using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mercury.Converter.Core;

/// <summary>How large the previews in the file list are. Off shows a compact list with no previews.</summary>
public enum ThumbnailSize
{
    Off,
    Small,
    Medium,
    Large,
}

public static class ThumbnailSizes
{
    /// <summary>The longest side of the preview image, in pixels.</summary>
    public static int Pixels(ThumbnailSize size) => size switch
    {
        ThumbnailSize.Small => 96,
        ThumbnailSize.Medium => 160,
        ThumbnailSize.Large => 256,
        _ => 0,
    };
}

/// <summary>
/// Makes small preview pictures of files. Each preview is made once and kept in a cache folder, so adding the
/// same file again is instant. Audio files have no picture, so the caller shows an icon instead.
/// </summary>
public sealed class ThumbnailService(ToolLocator tools, string cacheFolder) : IDisposable
{
    /// <summary>At most two previews are made at once, so adding a big folder does not slow the computer down.</summary>
    private readonly SemaphoreSlim limit = new(2);

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    public string CacheFolder { get; } = cacheFolder;

    public static ThumbnailService CreateDefault(ToolLocator tools) => new(
        tools,
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MercuryConverter",
            "thumbnails"));

    /// <summary>Returns the path of a preview picture, or null if this kind of file has none or it could not be made.</summary>
    public async Task<string?> GetAsync(string path, int pixels, CancellationToken cancellationToken = default)
    {
        if (pixels <= 0 || !File.Exists(path) || !InputFormats.TryGetKind(path, out var kind) || kind == MediaKind.Audio)
        {
            return null;
        }

        Directory.CreateDirectory(CacheFolder);
        var target = Path.Combine(CacheFolder, CacheName(path, pixels));
        if (File.Exists(target))
        {
            // Touch the file so the cleanup keeps previews that are still in use.
            TryTouch(target);
            return target;
        }

        await limit.WaitAsync(cancellationToken);
        try
        {
            // Another request may have made it while this one waited.
            if (File.Exists(target))
            {
                return target;
            }

            var temp = Path.Combine(CacheFolder, $".{Guid.NewGuid():N}.jpg");
            try
            {
                var made = kind == MediaKind.Video
                    ? await MakeVideoThumbnailAsync(path, pixels, temp, cancellationToken)
                    : await MakeImageThumbnailAsync(path, pixels, temp, cancellationToken);

                if (!made || !File.Exists(temp) || new FileInfo(temp).Length == 0)
                {
                    return null;
                }

                File.Move(temp, target, overwrite: true);
                return target;
            }
            finally
            {
                SafeOutput.TryDelete(temp);
            }
        }
        finally
        {
            limit.Release();
        }
    }

    /// <summary>Deletes the least recently used previews until the cache is under the size limit.</summary>
    public void Prune(long maxBytes)
    {
        if (!Directory.Exists(CacheFolder))
        {
            return;
        }

        var files = new DirectoryInfo(CacheFolder).GetFiles("*.jpg")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
            if (total > maxBytes)
            {
                SafeOutput.TryDelete(file.FullName);
            }
        }
    }

    /// <summary>A file name that changes whenever the file or the requested size changes.</summary>
    public static string CacheName(string path, int pixels)
    {
        var info = new FileInfo(path);
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{pixels}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant() + ".jpg";
    }

    private async Task<bool> MakeImageThumbnailAsync(string path, int pixels, string output, CancellationToken cancellationToken)
    {
        var size = pixels.ToString(CultureInfo.InvariantCulture);

        // "[0]" is the first frame. -thumbnail is much faster than -resize and drops extra data from the file.
        return await RunAsync(Tool.ImageMagick,
        [
            "-limit", "thread", "1",
            path + "[0]",
            "-auto-orient",
            "-background", "white", "-alpha", "remove", "-alpha", "off",
            "-thumbnail", $"{size}x{size}>",
            "-quality", "82",
            output,
        ], cancellationToken);
    }

    private async Task<bool> MakeVideoThumbnailAsync(string path, int pixels, string output, CancellationToken cancellationToken)
    {
        var size = pixels.ToString(CultureInfo.InvariantCulture);

        // A frame a little way in avoids the black first frame that many videos start with.
        var duration = await MediaProbe.GetDurationSecondsAsync(path, tools, cancellationToken);
        var seek = duration is { } length ? Math.Min(length * 0.1, 5.0) : 0.0;

        return await RunAsync(Tool.FFmpeg,
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-threads", "1",
            "-ss", seek.ToString("0.###", CultureInfo.InvariantCulture),
            "-protocol_whitelist", "file,pipe",
            "-i", path,
            "-frames:v", "1",
            "-vf", $"scale='min({size},iw)':'min({size},ih)':force_original_aspect_ratio=decrease",
            "-q:v", "4",
            output,
        ], cancellationToken);
    }

    private async Task<bool> RunAsync(Tool tool, string[] arguments, CancellationToken cancellationToken)
    {
        var fileName = tools.Locate(tool);
        if (fileName is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        ToolEnvironment.Apply(startInfo, tool, fileName);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Priority is a courtesy. The preview is made either way.
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ToolTimeout);

            var drain = Task.WhenAll(
                process.StandardError.ReadToEndAsync(CancellationToken.None),
                process.StandardOutput.ReadToEndAsync(CancellationToken.None));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            await drain;
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose() => limit.Dispose();

    private static void TryTouch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to update the time only means the preview may be cleaned up a little earlier.
        }
    }
}
