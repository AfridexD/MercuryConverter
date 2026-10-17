using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class PrivacyAndAudioTests
{
    private static ConversionPlan Plan(string input, TargetFormat target, bool strip = false, bool losslessJpeg = false) =>
        CommandBuilder.Build(
            new ConversionRequest(input, target, 75, losslessJpeg, StripMetadata: strip),
            outputPath: @"C:\Out\result." + Targets.Get(target).Extension,
            tempDirectory: @"C:\Temp");

    [Fact]
    public void Stripping_a_photo_removes_details_but_keeps_the_color_profile()
    {
        var step = Assert.Single(Plan(@"C:\a.jpg", TargetFormat.WebP, strip: true).Steps);

        var arguments = step.Arguments.ToList();
        var index = arguments.IndexOf("+profile");
        Assert.True(index >= 0);
        Assert.Equal("!icc,*", arguments[index + 1]);

        // The photo is turned upright before its orientation tag is removed.
        Assert.True(arguments.IndexOf("-auto-orient") < index);
    }

    [Fact]
    public void A_photo_keeps_its_details_by_default()
    {
        var step = Assert.Single(Plan(@"C:\a.jpg", TargetFormat.WebP).Steps);

        Assert.DoesNotContain("+profile", step.Arguments);
    }

    [Fact]
    public void Stripping_a_video_drops_tags_and_chapters()
    {
        var step = Assert.Single(Plan(@"C:\a.mov", TargetFormat.Mp4, strip: true).Steps);

        var arguments = step.Arguments.ToList();
        Assert.Equal("-1", arguments[arguments.IndexOf("-map_metadata") + 1]);
        Assert.Equal("-1", arguments[arguments.IndexOf("-map_chapters") + 1]);
    }

    [Fact]
    public void Stripping_a_song_drops_its_tags()
    {
        var step = Assert.Single(Plan(@"C:\a.wav", TargetFormat.Mp3, strip: true).Steps);

        Assert.Contains("-map_metadata", step.Arguments);
    }

    [Fact]
    public void Stripping_stops_the_lossless_jpeg_shortcut_because_that_copies_the_details()
    {
        var plan = Plan(@"C:\a.jpg", TargetFormat.JpegXl, strip: true, losslessJpeg: true);

        Assert.DoesNotContain(plan.Steps, step => step.Arguments.Contains("--lossless_jpeg=1"));
        Assert.Contains(plan.Steps, step => step.Arguments.Contains("+profile"));
    }

    [Theory]
    [InlineData(TargetFormat.Mp3)]
    [InlineData(TargetFormat.Aac)]
    [InlineData(TargetFormat.Flac)]
    public void Videos_can_be_turned_into_audio(TargetFormat format)
    {
        Assert.True(FormatCatalog.IsOffered(MediaGroup.Video, format));

        var step = Assert.Single(Plan(@"C:\clip.mp4", format).Steps);
        Assert.Contains("-vn", step.Arguments);
    }

    [Fact]
    public void Audio_only_choices_are_not_recommended_ahead_of_video_formats()
    {
        var recommended = FormatCatalog.For(MediaGroup.Video).Where(c => c.IsRecommended).Select(c => c.Format);

        Assert.DoesNotContain(TargetFormat.Mp3, recommended);
    }

    [Fact]
    public void Audio_taken_from_video_is_estimated_far_smaller_than_the_video()
    {
        var estimate = SizeEstimator.Estimate(100_000_000, MediaGroup.Video, TargetFormat.Mp3, 75);

        Assert.InRange(estimate, 1_000_000, 15_000_000);
    }
}
