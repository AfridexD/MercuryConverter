using System.Xml.Linq;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

/// <summary>The file types listed in the Microsoft Store package, so Windows offers Mercury under "Open with".</summary>
public sealed class StoreMenuTests
{
    private static XElement Wrap(string extensions) => XElement.Parse(
        "<Extensions xmlns:uap=\"u1\" xmlns:uap3=\"u3\">" + extensions + "</Extensions>");

    [Fact]
    public void Every_kind_of_file_gets_one_group_with_its_file_types()
    {
        var root = Wrap(StoreMenu.BuildExtensions());
        var groups = root.Descendants().Where(e => e.Name.LocalName == "FileTypeAssociation").ToList();

        Assert.Equal(["mercuryphoto", "mercuryanimation", "mercuryvideo", "mercuryaudio"], groups.Select(g => (string)g.Attribute("Name")!));
        Assert.All(groups, group => Assert.NotEmpty(group.Descendants().Where(e => e.Name.LocalName == "FileType")));
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
    public void Opening_a_file_passes_it_to_the_program_and_a_selection_of_many_is_handled()
    {
        var root = Wrap(StoreMenu.BuildExtensions());

        Assert.All(root.Descendants().Where(e => e.Name.LocalName == "FileTypeAssociation"), group =>
        {
            Assert.Equal("\"%1\"", (string)group.Attribute("Parameters")!);
            Assert.Equal("Player", (string)group.Attribute("MultiSelectModel")!);
        });
    }

    [Fact]
    public void No_verbs_are_listed_because_Windows_only_shows_them_for_the_default_program()
    {
        var root = Wrap(StoreMenu.BuildExtensions());

        Assert.Empty(root.Descendants().Where(e => e.Name.LocalName is "Verb" or "SupportedVerbs"));
    }
}
