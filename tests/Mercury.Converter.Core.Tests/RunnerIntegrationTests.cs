using System.Diagnostics;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

/// <summary>
/// Runs the real bundled tools. Each test returns early (and passes) when the tools folder is missing,
/// so the unit tests still run on machines that have not fetched the tools.
/// </summary>
public class RunnerIntegrationTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "mercury-it-" + Guid.NewGuid().ToString("N"));
    private readonly string? toolsRoot = FindToolsRoot();

    public RunnerIntegrationTests()
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

    private ConversionRunner Runner() => new(new ToolLocator(toolsRoot!));

    private bool ToolsMissing => toolsRoot is null;

    /// <summary>Creates a small test image or clip with FFmpeg's built-in test patterns.</summary>
    private string MakeTestMedia(string fileName, string ffmpegArguments)
    {
        var path = Path.Combine(folder, fileName);
        var ffmpeg = new ToolLocator(toolsRoot!).Locate(Tool.FFmpeg)!;
        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var argument in ParseArguments(ffmpegArguments.Replace("{out}", path)))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return path;
    }

    private static IEnumerable<string> ParseArguments(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Png_to_JpegXl_writes_a_smaller_file()
    {
        if (ToolsMissing) return;

        // A photo-like source. Flat test patterns compress better as PNG than as JPEG XL, so they would not show the saving.
        var input = MakeTestMedia("photo.png", "-hide_banner -loglevel error -y -f lavfi -i mandelbrot=size=1280x720 -frames:v 1 {out}");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.JpegXl, Quality: 75),
            Path.Combine(folder, "photo.jxl"));

        Assert.True(result.Succeeded, result.Error);
        Assert.EndsWith(".jxl", result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
        Assert.True(result.OutputBytes < result.InputBytes);
    }

    [Fact]
    public async Task Jpeg_to_JpegXl_lossless_recompression_is_smaller_and_reversible()
    {
        if (ToolsMissing) return;
        var input = MakeTestMedia("photo.jpg", "-hide_banner -loglevel error -y -f lavfi -i testsrc=size=800x600:rate=1 -frames:v 1 -q:v 3 {out}");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.JpegXl, LosslessJpegRecompression: true),
            Path.Combine(folder, "photo.jxl"));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.OutputBytes < result.InputBytes);

        // Turning the JXL back into a JPEG must give back the original bytes.
        var restored = Path.Combine(folder, "restored.jpg");
        var djxl = new ToolLocator(toolsRoot!).Locate(Tool.DJxl)!;
        using var process = Process.Start(new ProcessStartInfo(djxl)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { result.OutputPath!, restored, "--reconstruct_jpeg" },
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(restored));
    }

    [Fact]
    public async Task Png_to_WebP_uses_ImageMagick()
    {
        if (ToolsMissing) return;
        var input = MakeTestMedia("photo.png", "-hide_banner -loglevel error -y -f lavfi -i testsrc=size=640x480:rate=1 -frames:v 1 {out}");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.WebP, Quality: 60),
            Path.Combine(folder, "photo.webp"));

        Assert.True(result.Succeeded, result.Error);
        Assert.EndsWith(".webp", result.OutputPath);
    }

    [Fact]
    public async Task Video_to_mp4_reports_progress_and_finishes()
    {
        if (ToolsMissing) return;
        var input = MakeTestMedia("clip.mp4", "-hide_banner -loglevel error -y -f lavfi -i testsrc=duration=3:size=640x360:rate=30 -f lavfi -i sine=duration=3 -c:v libx264 -c:a aac -shortest {out}");
        var reports = new List<double>();
        var progress = new Progress<double>(value => reports.Add(value));

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Mp4, Quality: 75),
            Path.Combine(folder, "clip-out.mp4"),
            durationSeconds: 3.0,
            progress: progress);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(File.Exists(result.OutputPath));
        // Progress events are delivered asynchronously, so give them a moment to arrive.
        await Task.Delay(100);
        Assert.Contains(reports, value => value >= 1.0);
    }

    [Fact]
    public async Task Video_to_gif_produces_an_animated_gif()
    {
        if (ToolsMissing) return;
        var input = MakeTestMedia("clip.mp4", "-hide_banner -loglevel error -y -f lavfi -i testsrc=duration=2:size=320x240:rate=30 -c:v libx264 {out}");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Gif, Quality: 80),
            Path.Combine(folder, "clip.gif"));

        Assert.True(result.Succeeded, result.Error);
        var header = File.ReadAllBytes(result.OutputPath!).Take(6).ToArray();
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(header));
    }

    [Fact]
    public async Task Existing_file_is_never_overwritten()
    {
        if (ToolsMissing) return;
        var input = MakeTestMedia("photo.png", "-hide_banner -loglevel error -y -f lavfi -i testsrc=size=320x240:rate=1 -frames:v 1 {out}");
        var existing = Path.Combine(folder, "keep.webp");
        File.WriteAllText(existing, "my original file");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.WebP),
            existing);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(Path.Combine(folder, "keep (2).webp"), result.OutputPath);
        Assert.Equal("my original file", File.ReadAllText(existing));
    }

    [Fact]
    public async Task Cancelled_conversion_leaves_no_output_or_temp_files()
    {
        if (ToolsMissing) return;
        var input = MakeTestMedia("photo.png", "-hide_banner -loglevel error -y -f lavfi -i testsrc=size=1280x720:rate=1 -frames:v 1 {out}");
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Jpeg),
            Path.Combine(folder, "cancelled.jpg"),
            cancellationToken: cancel.Token);

        Assert.False(result.Succeeded);
        Assert.True(result.WasCancelled);
        Assert.Empty(Directory.GetFiles(folder, "cancelled*"));
        Assert.Empty(Directory.GetFiles(folder, ".mercury-converter-*"));
    }
}
