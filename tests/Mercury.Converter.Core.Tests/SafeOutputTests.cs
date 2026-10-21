using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class SafeOutputTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Mercury.Converter-tests-" + Guid.NewGuid().ToString("N"));

    public SafeOutputTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void UniquePath_returns_original_when_free()
    {
        var path = Path.Combine(folder, "photo.jpg");

        Assert.Equal(path, SafeOutput.UniquePath(path));
    }

    [Fact]
    public void UniquePath_adds_a_number_instead_of_overwriting()
    {
        var path = Path.Combine(folder, "photo.jpg");
        File.WriteAllText(path, "original");
        File.WriteAllText(Path.Combine(folder, "photo (2).jpg"), "second");

        var unique = SafeOutput.UniquePath(path);

        Assert.Equal(Path.Combine(folder, "photo (3).jpg"), unique);
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void TempPath_is_hidden_in_same_folder_and_keeps_extension()
    {
        var final = Path.Combine(folder, "clip.mp4");

        var temp = SafeOutput.TempPathFor(final);

        Assert.Equal(folder, Path.GetDirectoryName(temp));
        Assert.StartsWith(".", Path.GetFileName(temp));
        Assert.EndsWith(".mp4", temp);
    }

    [Fact]
    public void Commit_moves_temp_file_to_final_name()
    {
        var final = Path.Combine(folder, "done.png");
        var temp = SafeOutput.TempPathFor(final);
        File.WriteAllText(temp, "data");

        SafeOutput.Commit(temp, final);

        Assert.False(File.Exists(temp));
        Assert.Equal("data", File.ReadAllText(final));
    }

    [Fact]
    public void Commit_refuses_to_overwrite_existing_file()
    {
        var final = Path.Combine(folder, "keep.png");
        File.WriteAllText(final, "keep me");
        var temp = SafeOutput.TempPathFor(final);
        File.WriteAllText(temp, "new");

        Assert.ThrowsAny<IOException>(() => SafeOutput.Commit(temp, final));
        Assert.Equal("keep me", File.ReadAllText(final));
    }

    [Fact]
    public void TryDelete_ignores_missing_files()
    {
        SafeOutput.TryDelete(Path.Combine(folder, "missing.tmp"));
    }
}

public class CommitUniqueTests
{
    [Fact]
    public void Two_files_that_want_the_same_name_both_get_saved()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"commit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var wanted = Path.Combine(folder, "in.jpg");
            var first = Path.Combine(folder, ".a.jpg");
            var second = Path.Combine(folder, ".b.jpg");
            File.WriteAllText(first, "one");
            File.WriteAllText(second, "two");

            // Both asked for "in.jpg" while it was still free, as parallel conversions do.
            var savedFirst = SafeOutput.CommitUnique(first, wanted);
            var savedSecond = SafeOutput.CommitUnique(second, wanted);

            Assert.Equal(wanted, savedFirst);
            Assert.Equal(Path.Combine(folder, "in (2).jpg"), savedSecond);
            Assert.Equal("one", File.ReadAllText(savedFirst));
            Assert.Equal("two", File.ReadAllText(savedSecond));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_existing_file_is_never_overwritten()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"commit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var wanted = Path.Combine(folder, "keep.png");
            File.WriteAllText(wanted, "mine");
            var temp = Path.Combine(folder, ".t.png");
            File.WriteAllText(temp, "new");

            var saved = SafeOutput.CommitUnique(temp, wanted);

            Assert.NotEqual(wanted, saved);
            Assert.Equal("mine", File.ReadAllText(wanted));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
