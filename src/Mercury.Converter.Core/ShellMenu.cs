namespace Mercury.Converter.Core;

/// <summary>One line in the File Explorer right-click menu, for example "To Mp4 (low quality)".</summary>
/// <param name="Kind">The kind of file the entry appears for.</param>
/// <param name="Quality">Quality to use instead of the app's setting. Null means use the setting.</param>
/// <param name="VerbKey">Registry key name. The number keeps the menu in the intended order.</param>
public sealed record ShellMenuEntry(MediaKind Kind, TargetFormat Target, int? Quality, string Label, string VerbKey)
{
    /// <summary>The command Explorer runs. %* is replaced with every selected file, each already quoted.</summary>
    public string CommandLine(string exePath)
    {
        var extension = Targets.Get(Target).Extension;
        var quality = Quality is { } value ? $" --quality {value}" : string.Empty;
        return $"\"{exePath}\" --convert {extension}{quality} %*";
    }
}

/// <summary>
/// The right-click menu. Every entry reads "To Format", in the same order as File Converter's menu,
/// and the formats offered for video files include the audio formats.
/// </summary>
public static class ShellMenu
{
    /// <summary>Quality used by the "(low quality)" entries.</summary>
    public const int LowQuality = 30;

    public static IReadOnlyList<ShellMenuEntry> Entries { get; } = BuildEntries();

    /// <summary>Entries for a kind of file, in menu order.</summary>
    public static IReadOnlyList<ShellMenuEntry> For(MediaKind kind) => Entries.Where(entry => entry.Kind == kind).ToList();

    private static List<ShellMenuEntry> BuildEntries()
    {
        var entries = new List<ShellMenuEntry>();

        Add(entries, MediaKind.Video,
            (TargetFormat.Mkv, false), (TargetFormat.Mp4, false), (TargetFormat.Mp4, true), (TargetFormat.WebM, false),
            (TargetFormat.Ogv, false), (TargetFormat.Avi, false), (TargetFormat.Gif, false), (TargetFormat.Gif, true),
            (TargetFormat.Ogg, false), (TargetFormat.Wav, false), (TargetFormat.Mp3, false), (TargetFormat.Aac, false));

        Add(entries, MediaKind.Animation,
            (TargetFormat.Mp4, false), (TargetFormat.Mp4, true), (TargetFormat.WebM, false), (TargetFormat.Mkv, false),
            (TargetFormat.Avi, false), (TargetFormat.Jpeg, false), (TargetFormat.Png, false));

        Add(entries, MediaKind.Audio,
            (TargetFormat.Aac, false), (TargetFormat.Flac, false), (TargetFormat.Mp3, false), (TargetFormat.Ogg, false),
            (TargetFormat.Wav, false));

        Add(entries, MediaKind.Image,
            (TargetFormat.JpegXl, false), (TargetFormat.WebP, false), (TargetFormat.Avif, false), (TargetFormat.Jpeg, false),
            (TargetFormat.Png, false), (TargetFormat.Tiff, false), (TargetFormat.Pdf, false));

        return entries;
    }

    private static void Add(List<ShellMenuEntry> entries, MediaKind kind, params (TargetFormat Target, bool Low)[] items)
    {
        for (var index = 0; index < items.Length; index++)
        {
            var (target, low) = items[index];
            var extension = Targets.Get(target).Extension;
            var label = "To " + Targets.MenuName(target) + (low ? " (low quality)" : string.Empty);
            var key = $"{index + 1:00}-{extension}{(low ? "-low" : string.Empty)}";
            entries.Add(new ShellMenuEntry(kind, target, low ? LowQuality : null, label, key));
        }
    }
}

/// <summary>What the right-click menu asked the app to do: convert these files to this format.</summary>
public sealed record ConvertCommand(TargetFormat? Target, int? Quality, IReadOnlyList<string> Files)
{
    public static readonly ConvertCommand None = new(null, null, []);

    /// <summary>
    /// Reads "--convert &lt;format&gt; [--quality N] file file ...". Anything else gives <see cref="None"/>.
    /// The first argument is the program path and is skipped.
    /// </summary>
    public static ConvertCommand Parse(IReadOnlyList<string> args)
    {
        var index = -1;
        for (var position = 1; position < args.Count; position++)
        {
            if (string.Equals(args[position], "--convert", StringComparison.OrdinalIgnoreCase))
            {
                index = position;
                break;
            }
        }

        if (index < 0 || index + 1 >= args.Count)
        {
            return None;
        }

        TargetFormat? target = null;
        foreach (var info in Targets.All)
        {
            if (string.Equals(info.Extension, args[index + 1], StringComparison.OrdinalIgnoreCase))
            {
                target = info.Format;
                break;
            }
        }

        var next = index + 2;
        int? quality = null;
        if (next + 1 < args.Count
            && string.Equals(args[next], "--quality", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[next + 1], out var parsed))
        {
            quality = QualityMapper.Clamp(parsed);
            next += 2;
        }

        return new ConvertCommand(target, quality, args.Skip(next).ToList());
    }
}
