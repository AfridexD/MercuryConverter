using System.Diagnostics;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class ThumbnailTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "mercury-thumbs-" + Guid.NewGuid().ToString("N"));
    private readonly string? toolsRoot = FindToolsRoot();

    public ThumbnailTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private static string? FindToolsRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tools");
            if (File.Exists(Path.Combine(candidate, "libjxl", "cjxl.exe")) &&
                File.Exists(Path.Combine(candidate, "ffmpeg", "ffmpeg.exe")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private ThumbnailService Service() => new(new ToolLocator(toolsRoot!), Path.Combine(folder, "cache"));

    private string Make(string name, params string[] ffmpegArguments)
    {
        var path = Path.Combine(folder, name);
        var ffmpeg = new ToolLocator(toolsRoot!).Locate(Tool.FFmpeg)!;
        var startInfo = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(ffmpegArguments).Append(path))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return path;
    }

    private (int Width, int Height) SizeOf(string picture)
    {
        var magick = new ToolLocator(toolsRoot!).Locate(Tool.ImageMagick)!;
        var startInfo = new ProcessStartInfo(magick) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "identify", "-format", "%w %h", picture })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var text = process.StandardOutput.ReadToEnd().Trim().Split(' ');
        process.WaitForExit();
        return (int.Parse(text[0]), int.Parse(text[1]));
    }

    [Fact]
    public void Preview_sizes_are_in_pixels_and_off_means_none()
    {
        Assert.Equal(0, ThumbnailSizes.Pixels(ThumbnailSize.Off));
        Assert.True(ThumbnailSizes.Pixels(ThumbnailSize.Small) < ThumbnailSizes.Pixels(ThumbnailSize.Medium));
        Assert.True(ThumbnailSizes.Pixels(ThumbnailSize.Medium) < ThumbnailSizes.Pixels(ThumbnailSize.Large));
    }

    [Fact]
    public async Task An_image_gets_a_small_jpeg_preview_that_fits_the_requested_size()
    {
        if (toolsRoot is null) return;
        var image = Make("photo.png", "-f", "lavfi", "-i", "mandelbrot=size=1600x900", "-frames:v", "1");

        var preview = await Service().GetAsync(image, 160);

        Assert.NotNull(preview);
        var (width, height) = SizeOf(preview!);
        Assert.Equal(160, width);
        Assert.InRange(height, 85, 95);
        Assert.True(new FileInfo(preview!).Length < 40_000);
    }

    [Fact]
    public async Task A_video_gets_a_preview_taken_from_a_frame()
    {
        if (toolsRoot is null) return;
        var video = Make("clip.mp4", "-f", "lavfi", "-i", "testsrc=duration=3:size=640x360:rate=25", "-c:v", "libx264");

        var preview = await Service().GetAsync(video, 96);

        Assert.NotNull(preview);
        var (width, _) = SizeOf(preview!);
        Assert.Equal(96, width);
    }

    [Fact]
    public async Task An_animated_gif_gets_a_preview_of_its_first_frame()
    {
        if (toolsRoot is null) return;
        var gif = Make("anim.gif", "-f", "lavfi", "-i", "testsrc=duration=1:size=200x200:rate=10");

        var preview = await Service().GetAsync(gif, 160);

        Assert.NotNull(preview);
    }

    [Fact]
    public async Task Audio_files_have_no_preview()
    {
        if (toolsRoot is null) return;
        var audio = Make("tone.mp3", "-f", "lavfi", "-i", "sine=duration=1");

        Assert.Null(await Service().GetAsync(audio, 160));
    }

    [Fact]
    public async Task Off_and_missing_files_return_nothing()
    {
        if (toolsRoot is null) return;
        var image = Make("photo.png", "-f", "lavfi", "-i", "mandelbrot=size=300x200", "-frames:v", "1");

        Assert.Null(await Service().GetAsync(image, 0));
        Assert.Null(await Service().GetAsync(Path.Combine(folder, "missing.png"), 160));
    }

    [Fact]
    public async Task A_second_request_reuses_the_cached_preview()
    {
        if (toolsRoot is null) return;
        var image = Make("photo.png", "-f", "lavfi", "-i", "mandelbrot=size=800x600", "-frames:v", "1");
        var service = Service();

        var first = await service.GetAsync(image, 160);
        File.SetLastWriteTimeUtc(first!, DateTime.UtcNow.AddDays(-3));
        var before = File.ReadAllBytes(first!);
        var second = await service.GetAsync(image, 160);

        Assert.Equal(first, second);
        Assert.Equal(before, File.ReadAllBytes(second!));
    }

    [Fact]
    public async Task Each_size_and_each_version_of_a_file_gets_its_own_preview()
    {
        if (toolsRoot is null) return;
        var image = Make("photo.png", "-f", "lavfi", "-i", "mandelbrot=size=800x600", "-frames:v", "1");
        var service = Service();

        var small = await service.GetAsync(image, 96);
        var large = await service.GetAsync(image, 256);
        Assert.NotEqual(small, large);

        File.AppendAllText(image, "x");
        File.SetLastWriteTimeUtc(image, DateTime.UtcNow.AddMinutes(5));
        var changed = await service.GetAsync(image, 96);
        Assert.NotEqual(small, changed);
    }

    [Fact]
    public async Task Cleaning_the_cache_removes_the_oldest_previews_first()
    {
        if (toolsRoot is null) return;
        var service = Service();
        var paths = new List<string>();
        foreach (var name in new[] { "a.png", "b.png", "c.png" })
        {
            var image = Make(name, "-f", "lavfi", "-i", "mandelbrot=size=640x480", "-frames:v", "1");
            paths.Add((await service.GetAsync(image, 160))!);
        }

        File.SetLastWriteTimeUtc(paths[0], DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(paths[1], DateTime.UtcNow.AddDays(-1));
        service.Prune(new FileInfo(paths[2]).Length + 10);

        Assert.False(File.Exists(paths[0]));
        Assert.False(File.Exists(paths[1]));
        Assert.True(File.Exists(paths[2]));
    }
}
