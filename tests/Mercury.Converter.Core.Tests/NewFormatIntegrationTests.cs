using System.Diagnostics;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

/// <summary>Real conversions to every new format, using the bundled tools. Skipped when the tools are missing.</summary>
public class NewFormatIntegrationTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "mercury-newfmt-" + Guid.NewGuid().ToString("N"));
    private readonly string? toolsRoot = FindToolsRoot();

    public NewFormatIntegrationTests()
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

    private ToolLocator Tools => new(toolsRoot!);

    private string MakeClip(bool oddSize = false)
    {
        var path = Path.Combine(folder, "source.mp4");
        var size = oddSize ? "322x242" : "640x360";
        var ffmpeg = Tools.Locate(Tool.FFmpeg)!;
        var startInfo = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"testsrc=duration=3:size={size}:rate=25",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-c:v", oddSize ? "mpeg4" : "libx264", "-c:a", "aac", "-shortest", path,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return path;
    }

    [Theory]
    [InlineData(TargetFormat.Mkv)]
    [InlineData(TargetFormat.Mp4)]
    [InlineData(TargetFormat.WebM)]
    [InlineData(TargetFormat.Ogv)]
    [InlineData(TargetFormat.Avi)]
    [InlineData(TargetFormat.Gif)]
    [InlineData(TargetFormat.Ogg)]
    [InlineData(TargetFormat.Wav)]
    [InlineData(TargetFormat.Mp3)]
    [InlineData(TargetFormat.Aac)]
    public async Task Every_menu_format_converts_a_real_video(TargetFormat target)
    {
        if (toolsRoot is null) return;
        var input = MakeClip();
        var extension = Targets.Get(target).Extension;

        var result = await new ConversionRunner(Tools).RunAsync(
            new ConversionRequest(input, target, Quality: 70),
            Path.Combine(folder, "out." + extension),
            durationSeconds: 3.0,
            keepSmallerThanOriginal: false);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(new FileInfo(result.OutputPath!).Length > 0);

        // The result must be a real file of the right kind, with about the same length as the source.
        var length = await MediaProbe.GetDurationSecondsAsync(result.OutputPath!, Tools);
        if (target != TargetFormat.Gif)
        {
            Assert.NotNull(length);

            // A little padding is normal: AAC and MP3 write whole frames.
            Assert.InRange(length!.Value, 2.5, 3.7);
        }
    }

    [Fact]
    public async Task Odd_sized_video_converts_to_ogv_and_avi()
    {
        if (toolsRoot is null) return;
        var input = MakeClip(oddSize: true);

        foreach (var target in new[] { TargetFormat.Ogv, TargetFormat.Avi, TargetFormat.Mkv })
        {
            var result = await new ConversionRunner(Tools).RunAsync(
                new ConversionRequest(input, target, Quality: 60),
                Path.Combine(folder, "odd." + Targets.Get(target).Extension),
                keepSmallerThanOriginal: false);

            Assert.True(result.Succeeded, $"{target}: {result.Error}");
        }
    }

    [Fact]
    public async Task Hardware_decoding_gives_a_valid_result_and_matches_software_length()
    {
        if (toolsRoot is null) return;
        var input = MakeClip();

        var hardware = await new ConversionRunner(Tools).RunAsync(
            new ConversionRequest(input, TargetFormat.Mp4, Quality: 70, Execution: new ExecutionOptions(UseHardwareDecode: true)),
            Path.Combine(folder, "hw.mp4"),
            keepSmallerThanOriginal: false);
        var software = await new ConversionRunner(Tools).RunAsync(
            new ConversionRequest(input, TargetFormat.Mp4, Quality: 70, Execution: new ExecutionOptions(UseHardwareDecode: false)),
            Path.Combine(folder, "sw.mp4"),
            keepSmallerThanOriginal: false);

        Assert.True(hardware.Succeeded, hardware.Error);
        Assert.True(software.Succeeded, software.Error);

        var hwLength = await MediaProbe.GetDurationSecondsAsync(hardware.OutputPath!, Tools);
        var swLength = await MediaProbe.GetDurationSecondsAsync(software.OutputPath!, Tools);
        Assert.Equal(swLength!.Value, hwLength!.Value, precision: 1);
    }

    [Fact]
    public async Task A_detected_graphics_card_encoder_makes_a_playable_mp4()
    {
        if (toolsRoot is null) return;
        var encoder = await new HardwareProbe(Tools).DetectEncoderAsync();
        if (encoder == HardwareEncoder.None) return;

        var input = MakeClip();
        var result = await new ConversionRunner(Tools).RunAsync(
            new ConversionRequest(input, TargetFormat.Mp4, Quality: 70, Execution: new ExecutionOptions(Encoder: encoder)),
            Path.Combine(folder, "gpu.mp4"),
            keepSmallerThanOriginal: false);

        Assert.True(result.Succeeded, $"{encoder}: {result.Error}");
        var length = await MediaProbe.GetDurationSecondsAsync(result.OutputPath!, Tools);
        Assert.InRange(length!.Value, 2.5, 3.5);
    }

    [Fact]
    public async Task Probe_returns_without_hanging()
    {
        if (toolsRoot is null) return;

        var encoder = await new HardwareProbe(Tools).DetectEncoderAsync();

        Assert.True(Enum.IsDefined(encoder));
    }
}
