using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class CommandBuilderTests
{
    private const string TempDir = @"C:\Temp";

    private static ConversionPlan Plan(string input, TargetFormat target, int quality = 75, bool losslessJpeg = false) =>
        CommandBuilder.Build(
            new ConversionRequest(input, target, quality, losslessJpeg),
            outputPath: @"C:\Out\result." + Targets.Get(target).Extension,
            tempDirectory: TempDir);

    [Fact]
    public void Jpeg_to_JpegXl_with_recompression_uses_lossless_jpeg()
    {
        var plan = Plan(@"C:\Photos\a.jpg", TargetFormat.JpegXl, losslessJpeg: true);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(Tool.CJxl, step.Tool);
        Assert.Contains("--lossless_jpeg=1", step.Arguments);
        Assert.Empty(plan.IntermediateFiles);
    }

    [Fact]
    public void Png_to_JpegXl_goes_straight_to_cjxl()
    {
        var plan = Plan(@"C:\Photos\a.png", TargetFormat.JpegXl, quality: 90);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(Tool.CJxl, step.Tool);
        Assert.Contains("1.0", step.Arguments);
        Assert.Empty(plan.IntermediateFiles);
    }

    [Fact]
    public void Bmp_to_JpegXl_decodes_to_temp_png_first()
    {
        var plan = Plan(@"C:\Photos\a.bmp", TargetFormat.JpegXl);

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(Tool.ImageMagick, plan.Steps[0].Tool);
        Assert.Equal(Tool.CJxl, plan.Steps[1].Tool);
        var intermediate = Assert.Single(plan.IntermediateFiles);
        Assert.StartsWith(TempDir, intermediate);
        Assert.EndsWith(".png", intermediate);
    }

    [Fact]
    public void Jpeg_output_flattens_transparency_onto_white()
    {
        var plan = Plan(@"C:\Photos\a.png", TargetFormat.Jpeg);

        var args = plan.Steps.Single().Arguments;
        Assert.Contains("-background", args);
        Assert.Contains("white", args);
    }

    [Fact]
    public void Png_output_keeps_transparency_and_has_no_quality_flag()
    {
        var plan = Plan(@"C:\Photos\a.png", TargetFormat.Png);

        Assert.DoesNotContain("-quality", plan.Steps.Single().Arguments);
    }

    [Fact]
    public void Mp4_uses_h264_with_crf_from_quality()
    {
        var plan = Plan(@"C:\Videos\clip.mov", TargetFormat.Mp4, quality: 100);

        var args = plan.Steps.Single().Arguments;
        Assert.Equal(Tool.FFmpeg, plan.Steps.Single().Tool);
        Assert.Contains("libx264", args);
        Assert.Equal("15", args[Array.IndexOf(args.ToArray(), "-crf") + 1]);
        Assert.Contains("pipe:1", args);
    }

    [Fact]
    public void WebM_uses_vp9_with_constant_quality()
    {
        var plan = Plan(@"C:\Videos\clip.mp4", TargetFormat.WebM);

        var args = plan.Steps.Single().Arguments;
        Assert.Contains("libvpx-vp9", args);
        Assert.Contains("-b:v", args);
    }

    [Fact]
    public void Gif_uses_a_palette_filter_graph()
    {
        var plan = Plan(@"C:\Videos\clip.mp4", TargetFormat.Gif, quality: 100);

        var args = plan.Steps.Single().Arguments;
        var graph = args[Array.IndexOf(args.ToArray(), "-filter_complex") + 1];
        Assert.Contains("palettegen", graph);
        Assert.Contains("paletteuse", graph);
        Assert.Contains("max_colors=256", graph);
    }

    [Fact]
    public void Mp3_from_video_drops_video_stream()
    {
        var plan = Plan(@"C:\Videos\clip.mp4", TargetFormat.Mp3);

        var args = plan.Steps.Single().Arguments;
        Assert.Contains("-vn", args);
        Assert.Contains("libmp3lame", args);
    }

    [Fact]
    public void Still_image_cannot_become_video()
    {
        Assert.Throws<NotSupportedException>(() => Plan(@"C:\Photos\a.jpg", TargetFormat.Mp4));
    }

    [Fact]
    public void Unknown_extension_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() => Plan(@"C:\Docs\report.docx", TargetFormat.Png));
    }

    [Fact]
    public void Paths_with_spaces_stay_as_single_arguments()
    {
        var plan = Plan(@"C:\My Photos\holiday pic.jpg", TargetFormat.WebP);

        Assert.Contains(@"C:\My Photos\holiday pic.jpg[0]", plan.Steps.Single().Arguments);
    }
}
