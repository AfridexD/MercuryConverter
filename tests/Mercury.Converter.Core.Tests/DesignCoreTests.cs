using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class DesignCoreTests
{
    [Theory]
    [InlineData(@"C:\a\photo.jpg", MediaGroup.Photo)]
    [InlineData(@"C:\a\photo.HEIC", MediaGroup.Photo)]
    [InlineData(@"C:\a\clip.mov", MediaGroup.Video)]
    [InlineData(@"C:\a\anim.gif", MediaGroup.Gif)]
    [InlineData(@"C:\a\song.wav", MediaGroup.Audio)]
    public void Files_are_sorted_into_the_four_groups(string path, MediaGroup expected)
    {
        Assert.Equal(expected, MediaGroups.Of(path));
    }

    [Fact]
    public void Unreadable_files_have_no_group()
    {
        Assert.Null(MediaGroups.Of(@"C:\a\report.docx"));
    }

    [Fact]
    public void Every_group_has_formats_and_recommended_ones_come_with_a_tag()
    {
        foreach (var group in Enum.GetValues<MediaGroup>())
        {
            var formats = FormatCatalog.For(group);
            Assert.NotEmpty(formats);
            Assert.Contains(formats, choice => choice.IsRecommended);
            Assert.True(FormatCatalog.IsOffered(group, FormatCatalog.Default(group)));
        }
    }

    [Fact]
    public void Defaults_match_the_design()
    {
        Assert.Equal(TargetFormat.WebP, FormatCatalog.Default(MediaGroup.Photo));
        Assert.Equal(TargetFormat.Mp4, FormatCatalog.Default(MediaGroup.Video));
        Assert.Equal(TargetFormat.Gif, FormatCatalog.Default(MediaGroup.Gif));
        Assert.Equal(TargetFormat.Mp3, FormatCatalog.Default(MediaGroup.Audio));
    }

    [Fact]
    public void A_format_not_offered_for_a_group_falls_back_to_the_default()
    {
        // MP3 makes no sense for photos, so a stale choice must not break the list.
        Assert.False(FormatCatalog.IsOffered(MediaGroup.Photo, TargetFormat.Mp3));
        Assert.Equal(TargetFormat.WebP, FormatCatalog.Get(MediaGroup.Photo, TargetFormat.Mp3).Format);
    }

    [Fact]
    public void Every_offered_format_can_be_built_for_a_matching_file()
    {
        foreach (var group in Enum.GetValues<MediaGroup>())
        {
            var sample = group switch
            {
                MediaGroup.Photo => @"C:\in\a.png",
                MediaGroup.Video => @"C:\in\a.mp4",
                MediaGroup.Gif => @"C:\in\a.gif",
                _ => @"C:\in\a.wav",
            };

            foreach (var choice in FormatCatalog.For(group))
            {
                var plan = CommandBuilder.Build(
                    new ConversionRequest(sample, choice.Format),
                    @"C:\out\x." + Targets.Get(choice.Format).Extension,
                    @"C:\Temp");
                Assert.NotEmpty(plan.Steps);
            }
        }
    }

    [Fact]
    public void Lossless_formats_are_marked_lossless_in_the_catalog()
    {
        foreach (var choice in FormatCatalog.For(MediaGroup.Photo).Concat(FormatCatalog.For(MediaGroup.Audio)))
        {
            Assert.Equal(Targets.Get(choice.Format).Lossy, choice.Lossy);
        }
    }

    [Fact]
    public void A_higher_quality_gives_a_bigger_estimate_for_lossy_formats_only()
    {
        var webp = FormatCatalog.Get(MediaGroup.Photo, TargetFormat.WebP);
        var png = FormatCatalog.Get(MediaGroup.Photo, TargetFormat.Png);
        const long input = 4 * 1024 * 1024;

        Assert.True(SizeEstimator.Estimate(input, webp, 90) > SizeEstimator.Estimate(input, webp, 30));
        Assert.Equal(SizeEstimator.Estimate(input, png, 90), SizeEstimator.Estimate(input, png, 30));
    }

    [Fact]
    public void Estimates_follow_the_design_formula()
    {
        var jpeg = FormatCatalog.Get(MediaGroup.Photo, TargetFormat.Jpeg);

        // 10 MB x 0.32 x (0.35 + 0.75 x 1.1) = 3.76 MB
        var estimate = SizeEstimator.Estimate(10 * 1024 * 1024, jpeg, 75);

        Assert.InRange(estimate / (1024.0 * 1024), 3.74, 3.78);
    }

    [Theory]
    [InlineData(100, "1 KB")]
    [InlineData(512 * 1024, "512 KB")]
    [InlineData(4_404_019, "4.2 MB")]
    [InlineData(48L * 1024 * 1024, "48 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    public void Sizes_read_like_the_design(long bytes, string expected)
    {
        Assert.Equal(expected, SizeText.Of(bytes));
    }

    [Theory]
    [InlineData(42, "0:42")]
    [InlineData(185, "3:05")]
    [InlineData(3730, "1:02:10")]
    public void Durations_read_like_the_design(double seconds, string expected)
    {
        Assert.Equal(expected, SizeText.Duration(seconds));
    }

    [Fact]
    public void Time_left_is_rounded_to_friendly_values()
    {
        Assert.Equal("almost done", SizeText.TimeLeft(3));
        Assert.Equal("about 40 s left", SizeText.TimeLeft(41));
        Assert.Equal("about 2 min left", SizeText.TimeLeft(120));
    }

    [Fact]
    public void Plural_counts_use_the_right_word()
    {
        Assert.Equal("1 photo", MediaGroups.Count(MediaGroup.Photo, 1));
        Assert.Equal("3 videos", MediaGroups.Count(MediaGroup.Video, 3));
        Assert.Equal("2 audio files", MediaGroups.Count(MediaGroup.Audio, 2));
    }

    [Fact]
    public void Size_choices_describe_themselves_and_start_with_no_change()
    {
        Assert.True(SizeChoices.All[0].Changes.IsNone);
        Assert.Null(SizeChoices.Describe(Transform.None));
        Assert.Equal("Fit within 1920 px", SizeChoices.Describe(new Transform(MaxWidth: 1920)));
        Assert.Equal("Fit within 1080 px tall", SizeChoices.Describe(Transform.FitHeight(1080)));
        Assert.Equal("Half size", SizeChoices.Describe(Transform.Percent(50)));
    }

    [Fact]
    public void Rotation_does_not_count_as_a_size_choice()
    {
        var rotated = new Transform(MaxWidth: 1920, Rotation: 90, FlipHorizontal: true);

        Assert.Equal(new Transform(MaxWidth: 1920), SizeChoices.SizeOnly(rotated));
    }

    [Fact]
    public void Presets_know_their_group_chip_label_and_summary()
    {
        var web = PresetStore.BuiltIn.First(preset => preset.Name.StartsWith("Web photo", StringComparison.Ordinal));
        var gif = PresetStore.BuiltIn.First(preset => preset.Name.StartsWith("Short GIF", StringComparison.Ordinal));

        Assert.Equal(MediaGroup.Photo, web.Group);
        Assert.Equal("Web photo", web.ChipLabel);
        Assert.Equal("WebP · Quality 75 · Fit within 1920 px", web.Summary);
        Assert.Equal(MediaGroup.Gif, gif.Group);
    }

    [Fact]
    public void The_first_eight_built_in_presets_match_the_design_names()
    {
        var labels = PresetStore.BuiltIn.Take(8).Select(preset => preset.ChipLabel).ToList();

        Assert.Equal(
            ["Web photo", "Phone share", "Smallest photo", "Archive", "Video for sharing", "Video for web", "Short GIF", "Podcast audio"],
            labels);
    }

    [Fact]
    public void Lossless_presets_do_not_mention_quality_in_their_summary()
    {
        var archive = PresetStore.BuiltIn.First(preset => preset.ChipLabel == "Archive");

        Assert.DoesNotContain("Quality", archive.Summary);
    }

    [Fact]
    public void Speed_and_channel_choices_become_safe_ffmpeg_options()
    {
        Assert.Null(ConversionOptions.FfmpegOptions(TargetFormat.Mp4, VideoSpeed.Balanced, AudioChannels.KeepAsIs, false));
        Assert.Equal("-preset veryfast", ConversionOptions.FfmpegOptions(TargetFormat.Mp4, VideoSpeed.Faster, AudioChannels.KeepAsIs, false));
        Assert.Equal("-preset slower", ConversionOptions.FfmpegOptions(TargetFormat.Mkv, VideoSpeed.Slower, AudioChannels.KeepAsIs, false));
        Assert.Equal("-ac 1", ConversionOptions.FfmpegOptions(TargetFormat.Mp3, VideoSpeed.Faster, AudioChannels.Mono, false));

        // The preset option does not exist for these, so it must not be added.
        Assert.Null(ConversionOptions.FfmpegOptions(TargetFormat.WebM, VideoSpeed.Faster, AudioChannels.KeepAsIs, false));
        Assert.Null(ConversionOptions.FfmpegOptions(TargetFormat.Mp4, VideoSpeed.Faster, AudioChannels.KeepAsIs, graphicsCardEncoder: true));
    }

    [Fact]
    public void Every_option_the_app_builds_passes_the_ffmpeg_allowlist()
    {
        foreach (var speed in Enum.GetValues<VideoSpeed>())
        {
            foreach (var channels in Enum.GetValues<AudioChannels>())
            {
                foreach (var target in new[] { TargetFormat.Mp4, TargetFormat.Mkv, TargetFormat.Mp3, TargetFormat.Ogg })
                {
                    var options = ConversionOptions.FfmpegOptions(target, speed, channels, false);
                    if (options is not null)
                    {
                        FfmpegOptions.Parse(options);
                    }
                }
            }
        }
    }

    [Fact]
    public void Audio_channel_options_reach_the_audio_command()
    {
        var plan = CommandBuilder.Build(
            new ConversionRequest(@"C:\a\song.wav", TargetFormat.Mp3, ExtraFfmpegArguments: "-ac 1"),
            @"C:\out\song.mp3",
            @"C:\Temp");

        var args = plan.Steps[^1].Arguments.ToList();
        Assert.Equal("1", args[args.IndexOf("-ac") + 1]);
    }

    [Fact]
    public void Mkv_keeps_every_audio_track_and_subtitle()
    {
        var plan = CommandBuilder.Build(
            new ConversionRequest(@"C:\v\movie.mp4", TargetFormat.Mkv),
            @"C:\out\movie.mkv",
            @"C:\Temp");

        var args = plan.Steps[^1].Arguments.ToList();
        Assert.Contains("0:a?", args);
        Assert.Contains("0:s?", args);
        Assert.Equal("copy", args[args.IndexOf("-c:s") + 1]);
    }

    [Fact]
    public void New_settings_have_sensible_defaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.UseQuickWindow);
        Assert.False(settings.RightClickTipShown);
        Assert.Equal(TargetFormat.WebP, settings.TargetFor(MediaGroup.Photo));
        Assert.Equal(TargetFormat.Mp3, settings.TargetFor(MediaGroup.Audio));
        Assert.Equal(VideoSpeed.Balanced, settings.VideoSpeed);
    }
}
