using System.Xml.Linq;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

/// <summary>The right-click menu declared in the Microsoft Store package, and the command lines its lines run.</summary>
public sealed class StoreMenuTests
{
    private static XElement Wrap(string extensions) => XElement.Parse(
        "<Extensions xmlns:desktop4=\"d4\" xmlns:desktop5=\"d5\" xmlns:com=\"c\">" + extensions + "</Extensions>");

    private static IEnumerable<XElement> Named(XElement root, string name) => root.Descendants().Where(e => e.Name.LocalName == name);

    [Fact]
    public void There_is_one_menu_for_each_kind_of_file_with_its_own_fixed_id()
    {
        var kinds = StoreMenu.Menus.Select(menu => menu.Kind).ToList();

        Assert.Equal([MediaKind.Image, MediaKind.Animation, MediaKind.Video, MediaKind.Audio], kinds);
        Assert.Equal(4, StoreMenu.Menus.Select(menu => menu.Clsid).Distinct().Count());
        Assert.Equal(4, StoreMenu.Menus.Select(menu => menu.Verb).Distinct().Count());
    }

    [Fact]
    public void Every_readable_file_type_gets_exactly_one_menu()
    {
        var root = Wrap(StoreMenu.BuildExtensions());
        var types = Named(root, "ItemType").Select(e => (string)e.Attribute("Type")!).ToList();

        Assert.Equal(types.Count, types.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(InputFormats.All.Select(pair => "." + pair.Extension).Order(StringComparer.Ordinal), types.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_file_type_points_at_the_menu_of_its_own_kind()
    {
        var root = Wrap(StoreMenu.BuildExtensions());

        foreach (var itemType in Named(root, "ItemType"))
        {
            var extension = ((string)itemType.Attribute("Type")!).TrimStart('.');
            var kind = InputFormats.All.First(pair => string.Equals(pair.Extension, extension, StringComparison.OrdinalIgnoreCase)).Kind;
            var verb = Named(itemType, "Verb").Single();

            Assert.Equal(StoreMenu.For(kind).Clsid.ToString("D").ToUpperInvariant(), (string)verb.Attribute("Clsid")!);
            Assert.Equal(StoreMenu.For(kind).Verb, (string)verb.Attribute("Id")!);
        }
    }

    [Fact]
    public void The_helper_program_serves_every_menu_as_a_local_server()
    {
        var root = Wrap(StoreMenu.BuildExtensions());
        var server = Named(root, "ExeServer").Single();

        Assert.Equal(StoreMenu.HelperExecutable, (string)server.Attribute("Executable")!);
        Assert.Equal(StoreMenu.ServerArgument, (string)server.Attribute("Arguments")!);

        var classes = Named(server, "Class").Select(e => (string)e.Attribute("Id")!).Order().ToList();
        var expected = StoreMenu.Menus.Select(menu => menu.Clsid.ToString("D").ToUpperInvariant()).Order().ToList();
        Assert.Equal(expected, classes);
    }

    [Fact]
    public void Verb_names_use_only_letters_and_numbers()
    {
        Assert.All(StoreMenu.Menus, menu => Assert.Matches("^[A-Za-z0-9]+$", menu.Verb));
    }

    [Fact]
    public void A_menu_line_converts_the_files_with_the_chosen_format_and_quality()
    {
        var webp = ShellMenu.For(MediaKind.Image).First(entry => entry.Target == TargetFormat.WebP);
        var low = ShellMenu.For(MediaKind.Video).First(entry => entry.Target == TargetFormat.Mp4 && entry.Quality is not null);

        Assert.Equal("--convert webp \"C:\\a b\\one.jpg\" \"C:\\two.jpg\"", ShellLaunch.Arguments(webp, [@"C:\a b\one.jpg", @"C:\two.jpg"]));
        Assert.Equal($"--convert mp4 --quality {ShellMenu.LowQuality} \"C:\\v.mov\"", ShellLaunch.Arguments(low, [@"C:\v.mov"]));
    }

    [Fact]
    public void What_the_menu_runs_is_understood_by_the_program()
    {
        var entry = ShellMenu.For(MediaKind.Image).First(e => e.Target == TargetFormat.WebP);
        var arguments = ShellLaunch.Arguments(entry, [@"C:\a.jpg", @"C:\b.jpg"]);

        // The program is given its own path first, then these.
        var parsed = ConvertCommand.Parse(["MercuryConverter.exe", .. arguments.Replace("\"", string.Empty, StringComparison.Ordinal).Split(' ')]);

        Assert.Equal(TargetFormat.WebP, parsed.Target);
        Assert.Equal([@"C:\a.jpg", @"C:\b.jpg"], parsed.Files);
    }

    [Fact]
    public void Thousands_of_files_are_split_into_command_lines_that_are_short_enough()
    {
        var entry = ShellMenu.For(MediaKind.Image).First(e => e.Target == TargetFormat.WebP);
        var files = Enumerable.Range(0, 3000).Select(i => $@"C:\Users\someone\Pictures\Holiday 2026\IMG_{i:0000}.jpg").ToList();

        var batches = ShellLaunch.Batches(entry, files).ToList();

        Assert.True(batches.Count > 1);
        Assert.All(batches, batch => Assert.True(ShellLaunch.Arguments(entry, batch).Length <= ShellLaunch.MaxArgumentLength + 100));
        Assert.Equal(files, batches.SelectMany(batch => batch).ToList());
    }

    [Fact]
    public void A_quote_inside_a_file_name_cannot_break_out_of_the_command_line()
    {
        var entry = ShellMenu.For(MediaKind.Image).First(e => e.Target == TargetFormat.WebP);

        var arguments = ShellLaunch.Arguments(entry, ["C:\\evil\" --convert exe \"x.jpg"]);

        // Only the two quotes that wrap the name remain.
        Assert.Equal(2, arguments.Count(c => c == '"'));
    }
}
