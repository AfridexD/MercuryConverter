namespace Mercury.Converter.Core;

/// <summary>Builds the command line that a right-click menu line gives to Mercury.</summary>
public static class ShellLaunch
{
    /// <summary>
    /// The longest command line used for one start of the program. Windows allows 32,767 characters. A right-click on
    /// thousands of files is split into several starts, and the program merges them into one window.
    /// </summary>
    public const int MaxArgumentLength = 24_000;

    /// <summary>"--convert webp [--quality 30] "file" "file"" for one line of the menu.</summary>
    public static string Arguments(ShellMenuEntry entry, IEnumerable<string> files)
    {
        var extension = Targets.Get(entry.Target).Extension;
        var quality = entry.Quality is { } value ? $" --quality {value}" : string.Empty;
        return $"--convert {extension}{quality} {string.Join(' ', files.Select(Quote))}";
    }

    /// <summary>Splits a long list of files so that no command line is too long.</summary>
    public static IEnumerable<IReadOnlyList<string>> Batches(ShellMenuEntry entry, IReadOnlyList<string> files)
    {
        var current = new List<string>();
        var length = Arguments(entry, []).Length;
        foreach (var file in files)
        {
            var added = Quote(file).Length + 1;
            if (current.Count > 0 && length + added > MaxArgumentLength)
            {
                yield return current;
                current = [];
                length = Arguments(entry, []).Length;
            }

            current.Add(file);
            length += added;
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", string.Empty, StringComparison.Ordinal) + "\"";
}
