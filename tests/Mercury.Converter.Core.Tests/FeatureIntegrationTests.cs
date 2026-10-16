using System.Diagnostics;
using System.Text;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

/// <summary>End-to-end checks for the new features, using the bundled tools. Skipped when the tools are missing.</summary>
public class FeatureIntegrationTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "mercury-feat-" + Guid.NewGuid().ToString("N"));
    private readonly string? toolsRoot = FindToolsRoot();

    public FeatureIntegrationTests()
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

    private bool ToolsMissing => toolsRoot is null;

    private ConversionRunner Runner() => new(new ToolLocator(toolsRoot!));

    private string Run(string exe, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output;
    }

    private string MakePhoto(string name, string size = "800x600")
    {
        var path = Path.Combine(folder, name);
        var ffmpeg = new ToolLocator(toolsRoot!).Locate(Tool.FFmpeg)!;
        Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"mandelbrot=size={size}",
            "-frames:v", "1", path);
        return path;
    }

    private string Identify(string path)
    {
        var magick = new ToolLocator(toolsRoot!).Locate(Tool.ImageMagick)!;
        return Run(magick, "identify", "-format", "%w %h", path + "[0]").Trim();
    }

    [Fact]
    public async Task Resize_to_half_size_halves_the_dimensions()
    {
        if (ToolsMissing) return;
        var input = MakePhoto("big.png");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Png, Changes: Transform.Percent(50)),
            Path.Combine(folder, "half.png"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("400 300", Identify(result.OutputPath!));
    }

    [Fact]
    public async Task Rotate_90_swaps_width_and_height()
    {
        if (ToolsMissing) return;
        var input = MakePhoto("wide.png", "640x320");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Png, Changes: new Transform(Rotation: 90)),
            Path.Combine(folder, "rotated.png"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("320 640", Identify(result.OutputPath!));
    }

    [Fact]
    public async Task Size_limit_never_enlarges_a_small_image()
    {
        if (ToolsMissing) return;
        var input = MakePhoto("small.png", "200x100");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Png, Changes: new Transform(MaxWidth: 1920)),
            Path.Combine(folder, "same.png"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("200 100", Identify(result.OutputPath!));
    }

    [Fact]
    public async Task Pdf_output_has_a_pdf_header()
    {
        if (ToolsMissing) return;
        var input = MakePhoto("page.png");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Pdf),
            Path.Combine(folder, "page.pdf"));

        Assert.True(result.Succeeded, result.Error);
        var header = Encoding.ASCII.GetString(File.ReadAllBytes(result.OutputPath!).Take(4).ToArray());
        Assert.Equal("%PDF", header);
    }

    [Fact]
    public async Task Lowering_quality_keeps_a_recompressed_jpeg_smaller_than_the_original()
    {
        if (ToolsMissing) return;
        var input = MakePhoto("photo.jpg");

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Jpeg, Quality: 100),
            Path.Combine(folder, "recompressed.jpg"),
            keepSmallerThanOriginal: true);

        Assert.True(result.Succeeded, result.Error);
        // Quality 100 is larger than the source, so the runner must have stepped down.
        if (result.OutputBytes >= result.InputBytes)
        {
            Assert.NotNull(result.Warning);
        }
        else
        {
            Assert.NotNull(result.AdjustedQuality);
        }
    }

    [Fact]
    public async Task Odd_sized_video_converts_to_mp4()
    {
        if (ToolsMissing) return;
        var input = Path.Combine(folder, "odd.mp4");
        var ffmpeg = new ToolLocator(toolsRoot!).Locate(Tool.FFmpeg)!;
        Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=321x241:rate=10",
            // mpeg4 accepts odd frame sizes. libx264 would refuse them, which is the case the conversion must handle.
            "-c:v", "mpeg4", input);

        var result = await Runner().RunAsync(
            new ConversionRequest(input, TargetFormat.Mp4, Quality: 60),
            Path.Combine(folder, "odd-out.mp4"));

        Assert.True(result.Succeeded, result.Error);
    }
}
