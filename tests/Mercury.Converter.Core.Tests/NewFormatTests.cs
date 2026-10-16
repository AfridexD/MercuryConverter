using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class NewFormatTests
{
    private static List<string> ArgsFor(string input, TargetFormat target, int quality = 75, ExecutionOptions? exec = null)
    {
        var request = new ConversionRequest(input, target, quality, Execution: exec);
        var plan = CommandBuilder.Build(request, @"C:\Out\result." + Targets.Get(target).Extension, @"C:\Temp");
        return plan.Steps[^1].Arguments.ToList();
    }

    [Theory]
    [InlineData(TargetFormat.Mkv, "libx264", "aac")]
    [InlineData(TargetFormat.Ogv, "libtheora", "libvorbis")]
    [InlineData(TargetFormat.Avi, "mpeg4", "libmp3lame")]
    [InlineData(TargetFormat.WebM, "libvpx-vp9", "libopus")]
    [InlineData(TargetFormat.Mp4, "libx264", "aac")]
    public void Video_formats_use_the_expected_codecs(TargetFormat target, string video, string audio)
    {
        var args = ArgsFor(@"C:\v\clip.mov", target);

        Assert.Equal(video, args[args.IndexOf("-c:v") + 1]);
        Assert.Equal(audio, args[args.IndexOf("-c:a") + 1]);
    }

    [Theory]
    [InlineData(TargetFormat.Ogg, "libvorbis")]
    [InlineData(TargetFormat.Wav, "pcm_s16le")]
    [InlineData(TargetFormat.Aac, "aac")]
    [InlineData(TargetFormat.Mp3, "libmp3lame")]
    [InlineData(TargetFormat.Flac, "flac")]
    public void Audio_formats_extract_audio_from_a_video(TargetFormat target, string codec)
    {
        var args = ArgsFor(@"C:\v\clip.mp4", target);

        Assert.Contains("-vn", args);
        Assert.Equal(codec, args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void Every_format_in_the_menu_can_be_made_from_a_video()
    {
        foreach (var target in new[]
        {
            TargetFormat.Mkv, TargetFormat.Mp4, TargetFormat.WebM, TargetFormat.Ogv, TargetFormat.Avi, TargetFormat.Gif,
            TargetFormat.Ogg, TargetFormat.Wav, TargetFormat.Mp3, TargetFormat.Aac,
        })
        {
            var args = ArgsFor(@"C:\v\clip.mp4", target);
            Assert.NotEmpty(args);
        }
    }

    [Fact]
    public void An_animated_gif_goes_straight_to_cjxl()
    {
        var plan = CommandBuilder.Build(
            new ConversionRequest(@"C:\p\anim.gif", TargetFormat.JpegXl), @"C:\Out\anim.jxl", @"C:\Temp");

        var step = Assert.Single(plan.Steps);
        Assert.Equal(Tool.CJxl, step.Tool);
    }

    [Fact]
    public void Video_still_cannot_become_jpeg_xl()
    {
        Assert.Throws<NotSupportedException>(() => CommandBuilder.Build(
            new ConversionRequest(@"C:\v\clip.mp4", TargetFormat.JpegXl), @"C:\Out\clip.jxl", @"C:\Temp"));
    }

    [Fact]
    public void Menu_names_look_like_File_Converter_names()
    {
        Assert.Equal("Mp4", Targets.MenuName(TargetFormat.Mp4));
        Assert.Equal("Jpg", Targets.MenuName(TargetFormat.Jpeg));
        Assert.Equal("Jxl", Targets.MenuName(TargetFormat.JpegXl));
        Assert.Equal("Tiff", Targets.MenuName(TargetFormat.Tiff));
        Assert.Equal("Aac", Targets.MenuName(TargetFormat.Aac));
    }

    [Fact]
    public void Quality_mappings_for_the_new_formats_stay_in_range()
    {
        Assert.Equal(10, QualityMapper.OggQuality(100));
        Assert.Equal(0, QualityMapper.OggQuality(1));
        Assert.Equal(2, QualityMapper.Mpeg4Quantizer(100));
        Assert.Equal(31, QualityMapper.Mpeg4Quantizer(1));
        Assert.Equal(256, QualityMapper.AacBitrateKbps(100));
        Assert.Equal(64, QualityMapper.AacBitrateKbps(1));
    }

    [Fact]
    public void Hardware_decoding_is_on_by_default_for_video_and_off_for_audio_only()
    {
        Assert.Contains("-hwaccel", ArgsFor(@"C:\v\clip.mp4", TargetFormat.Mp4));
        Assert.DoesNotContain("-hwaccel", ArgsFor(@"C:\v\clip.mp4", TargetFormat.Mp3));
    }

    [Fact]
    public void Hardware_decoding_can_be_turned_off()
    {
        var args = ArgsFor(@"C:\v\clip.mp4", TargetFormat.Mp4, exec: new ExecutionOptions(UseHardwareDecode: false));

        Assert.DoesNotContain("-hwaccel", args);
    }

    [Fact]
    public void Thread_limits_reach_every_tool()
    {
        var exec = new ExecutionOptions(Threads: 4);

        var video = ArgsFor(@"C:\v\clip.mp4", TargetFormat.Mp4, exec: exec);
        Assert.Equal("4", video[video.IndexOf("-threads") + 1]);

        var image = ArgsFor(@"C:\p\a.png", TargetFormat.WebP, exec: exec);
        Assert.Equal("thread", image[image.IndexOf("-limit") + 1]);
        Assert.Equal("4", image[image.IndexOf("-limit") + 2]);

        var jxl = ArgsFor(@"C:\p\a.png", TargetFormat.JpegXl, exec: exec);
        Assert.Contains("--num_threads=4", jxl);
    }

    [Theory]
    [InlineData(HardwareEncoder.Nvenc, "h264_nvenc")]
    [InlineData(HardwareEncoder.Amf, "h264_amf")]
    [InlineData(HardwareEncoder.Qsv, "h264_qsv")]
    [InlineData(HardwareEncoder.None, "libx264")]
    public void Graphics_card_encoders_replace_x264_for_mp4(HardwareEncoder encoder, string codec)
    {
        var args = ArgsFor(@"C:\v\clip.mov", TargetFormat.Mp4, exec: new ExecutionOptions(Encoder: encoder));

        Assert.Equal(codec, args[args.IndexOf("-c:v") + 1]);
    }

    [Fact]
    public void Graphics_card_encoders_do_not_change_other_video_formats()
    {
        var args = ArgsFor(@"C:\v\clip.mov", TargetFormat.WebM, exec: new ExecutionOptions(Encoder: HardwareEncoder.Amf));

        Assert.Equal("libvpx-vp9", args[args.IndexOf("-c:v") + 1]);
    }

    [Fact]
    public void Software_only_removes_all_graphics_card_use()
    {
        var software = new ExecutionOptions(Threads: 6, UseHardwareDecode: true, Encoder: HardwareEncoder.Amf).SoftwareOnly();

        Assert.False(software.UsesHardware);
        Assert.Equal(6, software.Threads);
    }
}
