using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class FeatureTests
{
    private static List<string> ArgsFor(ConversionRequest request, string output = @"C:\Out\result.bin")
    {
        var plan = CommandBuilder.Build(request, output, @"C:\Temp");
        return plan.Steps[^1].Arguments.ToList();
    }

    [Fact]
    public void Transform_validation_rejects_bad_rotation_and_size()
    {
        Assert.False(new Transform(Rotation: 45).IsValid);
        Assert.False(new Transform(MaxWidth: 0).IsValid);
        Assert.False(Transform.Percent(500).IsValid);
        Assert.True(new Transform(Rotation: 270, FlipHorizontal: true).IsValid);
    }

    [Fact]
    public void Image_resize_uses_shrink_only_geometry()
    {
        var args = ArgsFor(new ConversionRequest(@"C:\p\a.png", TargetFormat.Png, Changes: new Transform(MaxWidth: 1920)));
        Assert.Equal("1920x>", args[args.IndexOf("-resize") + 1]);
    }

    [Fact]
    public void Image_percent_resize_is_passed_to_imagemagick()
    {
        var args = ArgsFor(new ConversionRequest(@"C:\p\a.png", TargetFormat.Png, Changes: Transform.Percent(50)));
        Assert.Equal("50%", args[args.IndexOf("-resize") + 1]);
    }

    [Fact]
    public void Image_rotate_and_flip_are_applied_before_resize()
    {
        var args = ArgsFor(new ConversionRequest(@"C:\p\a.png", TargetFormat.Png,
            Changes: new Transform(Rotation: 90, FlipHorizontal: true, MaxHeight: 720)));

        var rotate = args.IndexOf("-rotate");
        var flop = args.IndexOf("-flop");
        var resize = args.IndexOf("-resize");
        Assert.Equal("90", args[rotate + 1]);
        Assert.True(rotate < flop && flop < resize);
        Assert.Equal("x720>", args[resize + 1]);
    }

    [Fact]
    public void Video_gets_transform_filters_and_even_dimensions()
    {
        var args = ArgsFor(new ConversionRequest(@"C:\v\clip.mov", TargetFormat.Mp4, Changes: new Transform(Rotation: 270)));

        var filter = args[args.IndexOf("-vf") + 1];
        Assert.StartsWith("transpose=2", filter);
        Assert.Contains("trunc(iw/2)*2", filter);
    }

    [Fact]
    public void Gif_with_custom_size_does_not_add_the_default_640_cap()
    {
        var args = ArgsFor(new ConversionRequest(@"C:\v\clip.mp4", TargetFormat.Gif, Changes: Transform.Percent(50)));

        var graph = args[args.IndexOf("-filter_complex") + 1];
        Assert.Contains("iw*0.5", graph);
        Assert.DoesNotContain("min(640,iw)", graph);
    }

    [Fact]
    public void Pdf_output_uses_imagemagick_with_flattened_background()
    {
        var plan = CommandBuilder.Build(new ConversionRequest(@"C:\p\a.png", TargetFormat.Pdf), @"C:\Out\a.pdf", @"C:\Temp");

        var step = Assert.Single(plan.Steps);
        Assert.Equal(Tool.ImageMagick, step.Tool);
        Assert.Equal(@"C:\Out\a.pdf", step.Arguments[^1]);
        Assert.Contains("white", step.Arguments);
    }

    [Fact]
    public void Lossless_jpeg_recompression_is_skipped_when_resizing()
    {
        var plan = CommandBuilder.Build(
            new ConversionRequest(@"C:\p\a.jpg", TargetFormat.JpegXl, LosslessJpegRecompression: true, Changes: Transform.Percent(50)),
            @"C:\Out\a.jxl",
            @"C:\Temp");

        Assert.Equal(2, plan.Steps.Count);
        Assert.DoesNotContain("--lossless_jpeg=1", plan.Steps[^1].Arguments);
    }

    [Theory]
    [InlineData(75, new[] { 75, 60, 45, 30 })]
    [InlineData(100, new[] { 100, 85, 70, 55 })]
    [InlineData(20, new[] { 20, 10 })]
    [InlineData(10, new[] { 10 })]
    public void Size_retry_steps_down_by_15_to_a_floor_of_10(int requested, int[] expected)
    {
        Assert.Equal(expected, ConversionRunner.SizeAttempts(requested));
    }

    [Fact]
    public void Extra_ffmpeg_options_are_added_to_video_commands()
    {
        var args = ArgsFor(
            new ConversionRequest(@"C:\v\clip.mov", TargetFormat.Mp4, ExtraFfmpegArguments: "-b:v 2M -r 30 -preset slow"),
            @"C:\Out\clip.mp4");

        Assert.Equal("2M", args[args.IndexOf("-b:v") + 1]);
        Assert.Equal("30", args[args.IndexOf("-r") + 1]);
        Assert.Equal("slow", args[args.IndexOf("-preset") + 1]);
    }

    [Theory]
    [InlineData("-i C:\\secret.mp4")]
    [InlineData("-vf hue=s=0")]
    [InlineData("-y")]
    [InlineData("-b:v 2M -c:v libx265")]
    [InlineData("-r")]
    [InlineData("-b:v lots")]
    [InlineData("-af \"volume=2;rm\"")]
    [InlineData("-preset \"unclosed")]
    public void Unsafe_or_invalid_ffmpeg_options_are_rejected(string options)
    {
        Assert.Throws<ArgumentException>(() => FfmpegOptions.Parse(options));
    }

    [Fact]
    public void Quoted_audio_filter_keeps_its_spaces()
    {
        var tokens = FfmpegOptions.Parse("-af \"volume=2 dB\"");

        Assert.Equal(["-af", "volume=2 dB"], tokens);
    }
}
