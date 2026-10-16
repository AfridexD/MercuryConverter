using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class ShellMenuTests
{
    [Fact]
    public void The_video_menu_matches_the_File_Converter_menu_exactly()
    {
        var labels = ShellMenu.For(MediaKind.Video).Select(entry => entry.Label).ToList();

        Assert.Equal(
        [
            "To Mkv", "To Mp4", "To Mp4 (low quality)", "To Webm", "To Ogv", "To Avi",
            "To Gif", "To Gif (low quality)", "To Ogg", "To Wav", "To Mp3", "To Aac",
        ], labels.Select(label => label.Replace("WebM", "Webm")).ToList());
    }

    [Fact]
    public void Video_files_can_be_converted_to_every_format_in_the_screenshot()
    {
        var targets = ShellMenu.For(MediaKind.Video).Select(entry => entry.Target).ToHashSet();

        foreach (var wanted in new[]
        {
            TargetFormat.Mkv, TargetFormat.Mp4, TargetFormat.WebM, TargetFormat.Ogv, TargetFormat.Avi,
            TargetFormat.Gif, TargetFormat.Ogg, TargetFormat.Wav, TargetFormat.Mp3, TargetFormat.Aac,
        })
        {
            Assert.Contains(wanted, targets);
        }
    }

    [Fact]
    public void Low_quality_entries_carry_a_quality_and_the_others_do_not()
    {
        var entries = ShellMenu.For(MediaKind.Video);

        Assert.All(entries.Where(entry => entry.Label.Contains("low quality")), entry => Assert.Equal(ShellMenu.LowQuality, entry.Quality));
        Assert.All(entries.Where(entry => !entry.Label.Contains("low quality")), entry => Assert.Null(entry.Quality));
    }

    [Fact]
    public void Registry_keys_are_unique_and_sort_in_menu_order_within_each_kind()
    {
        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            var keys = ShellMenu.For(kind).Select(entry => entry.VerbKey).ToList();

            Assert.Equal(keys.Count, keys.Distinct().Count());
            Assert.Equal(keys.OrderBy(key => key, StringComparer.Ordinal).ToList(), keys);
        }
    }

    [Fact]
    public void Every_entry_makes_a_target_that_can_be_built_for_a_sample_file()
    {
        foreach (var entry in ShellMenu.Entries)
        {
            var sample = entry.Kind switch
            {
                MediaKind.Video => @"C:\in\a.mp4",
                MediaKind.Audio => @"C:\in\a.wav",
                MediaKind.Animation => @"C:\in\a.gif",
                _ => @"C:\in\a.png",
            };

            var request = new ConversionRequest(sample, entry.Target, entry.Quality ?? QualityMapper.Balanced);
            var plan = CommandBuilder.Build(request, @"C:\out\x." + Targets.Get(entry.Target).Extension, @"C:\Temp");

            Assert.NotEmpty(plan.Steps);
        }
    }

    [Fact]
    public void The_command_line_passes_every_selected_file_and_the_quality()
    {
        var low = ShellMenu.For(MediaKind.Video).Single(entry => entry.Label == "To Mp4 (low quality)");

        Assert.Equal("\"C:\\Apps\\Mercury Converter\\MercuryConverter.exe\" --convert mp4 --quality 30 %*", low.CommandLine(@"C:\Apps\Mercury Converter\MercuryConverter.exe"));
    }

    [Fact]
    public void Parsing_reads_format_quality_and_all_files()
    {
        var command = ConvertCommand.Parse([@"C:\app.exe", "--convert", "mp4", "--quality", "30", @"C:\a b.mov", @"C:\c.mov"]);

        Assert.Equal(TargetFormat.Mp4, command.Target);
        Assert.Equal(30, command.Quality);
        Assert.Equal([@"C:\a b.mov", @"C:\c.mov"], command.Files);
    }

    [Fact]
    public void Parsing_works_without_a_quality()
    {
        var command = ConvertCommand.Parse([@"C:\app.exe", "--convert", "jxl", @"C:\a.png"]);

        Assert.Equal(TargetFormat.JpegXl, command.Target);
        Assert.Null(command.Quality);
        Assert.Single(command.Files);
    }

    [Theory]
    [InlineData("--convert")]
    [InlineData("--convert", "nonsense", @"C:\a.png")]
    public void Parsing_ignores_incomplete_or_unknown_commands(params string[] arguments)
    {
        var command = ConvertCommand.Parse([@"C:\app.exe", .. arguments]);

        if (arguments.Length == 1)
        {
            Assert.Null(command.Target);
            Assert.Empty(command.Files);
        }
        else
        {
            Assert.Null(command.Target);
        }
    }

    [Fact]
    public void A_normal_launch_has_no_command()
    {
        Assert.Equal(ConvertCommand.None, ConvertCommand.Parse([@"C:\app.exe"]));
    }

    [Fact]
    public void Quality_from_the_command_line_is_kept_in_range()
    {
        var command = ConvertCommand.Parse([@"C:\app.exe", "--convert", "mp4", "--quality", "900", @"C:\a.mov"]);

        Assert.Equal(QualityMapper.Highest, command.Quality);
    }
}
