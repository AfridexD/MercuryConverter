using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.10.3", 1, 10, 3)]
    [InlineData("V2.0", 2, 0, 0)]
    public void Tags_become_versions(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), UpdateChecker.ParseVersion(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nightly")]
    [InlineData("v1.2.0-beta")]
    public void Odd_tags_are_ignored(string? tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }

    [Fact]
    public void A_release_answer_is_read()
    {
        var json = """{"tag_name":"v1.1.0","html_url":"https://github.com/AfridexD/MercuryConverter/releases/tag/v1.1.0","draft":false,"prerelease":false}""";

        var info = UpdateChecker.Parse(json);

        Assert.NotNull(info);
        Assert.Equal(new Version(1, 1, 0), info.Version);
        Assert.Equal("1.1.0", info.Text);
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.1.0","html_url":"https://github.com/AfridexD/MercuryConverter/releases/tag/v1.1.0","prerelease":true}""")]
    [InlineData("""{"tag_name":"v1.1.0","html_url":"https://github.com/AfridexD/MercuryConverter/releases/tag/v1.1.0","draft":true}""")]
    [InlineData("""{"tag_name":"v1.1.0","html_url":"https://evil.example/download"}""")]
    [InlineData("""{"tag_name":"v1.1.0"}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Unsafe_or_unreadable_answers_are_ignored(string json)
    {
        Assert.Null(UpdateChecker.Parse(json));
    }

    [Fact]
    public void Only_a_higher_version_counts_as_newer()
    {
        Assert.True(UpdateChecker.IsNewer(new Version(1, 0, 1), new Version(1, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(1, 0, 0), new Version(1, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(0, 9, 0), new Version(1, 0, 0)));
    }
}
