using System.Xml.Linq;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

/// <summary>The right-click menu that goes into the Microsoft Store package.</summary>
public sealed class StoreMenuTests
{
    private static XElement Wrap(string extensions) => XElement.Parse(
        "<Extensions xmlns:uap=\"u1\" xmlns:uap2=\"u2\" xmlns:uap3=\"u3\">" + extensions + "</Extensions>");

    [Fact]
    public void Every_kind_of_file_gets_one_group_with_its_file_types_and_a_few_lines()
    {
        var root = Wrap(StoreMenu.BuildExtensions());
        var groups = root.Descendants().Where(e => e.Name.LocalName == "FileTypeAssociation").ToList();

        Assert.Equal(["mercuryphoto", "mercuryanimation", "mercuryvideo", "mercuryaudio"], groups.Select(g => (string)g.Attribute("Name")!));
        foreach (var group in groups)
        {
            Assert.NotEmpty(group.Descendants().Where(e => e.Name.LocalName == "FileType"));
            var verbs = group.Descendants().Where(e => e.Name.LocalName == "Verb").ToList();
            Assert.InRange(verbs.Count, 2, 3);
        }
    }

    [Fact]
    public void A_file_type_is_in_only_one_group_because_the_package_cannot_claim_it_twice()
    {
        var root = Wrap(StoreMenu.BuildExtensions());
        var types = root.Descendants().Where(e => e.Name.LocalName == "FileType").Select(e => e.Value).ToList();

        Assert.Equal(types.Count, types.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(types, type => Assert.StartsWith(".", type));
    }

    [Fact]
    public void Each_line_runs_the_same_convert_command_as_the_installed_menu()
    {
        var root = Wrap(StoreMenu.BuildExtensions());
        var verbs = root.Descendants().Where(e => e.Name.LocalName == "Verb").ToList();

        Assert.All(verbs, verb =>
        {
            var parameters = (string)verb.Attribute("Parameters")!;
            Assert.StartsWith("--convert ", parameters);
            Assert.EndsWith("\"%1\"", parameters);
            Assert.Equal("Player", (string)verb.Attribute("MultiSelectModel")!);
            Assert.StartsWith("To ", verb.Value);
        });

        // The convert command must be one the app understands.
        var parsed = ConvertCommand.Parse(["Mercury.exe", .. ("--convert webp C:\\a.jpg").Split(' ')]);
        Assert.Equal(TargetFormat.WebP, parsed.Target);
    }

    [Fact]
    public void Verb_names_are_unique_inside_a_group_and_use_only_letters_and_numbers()
    {
        var root = Wrap(StoreMenu.BuildExtensions());
        foreach (var group in root.Descendants().Where(e => e.Name.LocalName == "FileTypeAssociation"))
        {
            var ids = group.Descendants().Where(e => e.Name.LocalName == "Verb").Select(e => (string)e.Attribute("Id")!).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.Matches("^[a-z0-9]+$", id));
        }
    }
}
