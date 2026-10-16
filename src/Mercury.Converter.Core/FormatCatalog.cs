using System.Globalization;

namespace Mercury.Converter.Core;

/// <summary>
/// The four kinds of file the app shows separately. A GIF is its own group because it can stay a GIF or
/// become a much smaller video.
/// </summary>
public enum MediaGroup
{
    Photo,
    Video,
    Gif,
    Audio,
}

/// <summary>One format a group of files can be converted to, with the plain-language text shown in the app.</summary>
/// <param name="Tag">A short recommendation such as "Plays everywhere". Formats with a tag are listed first.</param>
/// <param name="SizeFactor">Typical output size compared with the input at full quality. Used for estimates only.</param>
public sealed record FormatChoice(
    MediaGroup Group,
    TargetFormat Format,
    string Name,
    string Description,
    string? Tag,
    bool Lossy,
    double SizeFactor)
{
    public bool IsRecommended => Tag is not null;
}

public static class MediaGroups
{
    public static string Label(MediaGroup group) => group switch
    {
        MediaGroup.Photo => "Photos",
        MediaGroup.Video => "Videos",
        MediaGroup.Gif => "GIFs",
        _ => "Audio",
    };

    public static string One(MediaGroup group) => group switch
    {
        MediaGroup.Photo => "photo",
        MediaGroup.Video => "video",
        MediaGroup.Gif => "GIF",
        _ => "audio file",
    };

    public static string Many(MediaGroup group) => group switch
    {
        MediaGroup.Photo => "photos",
        MediaGroup.Video => "videos",
        MediaGroup.Gif => "GIFs",
        _ => "audio files",
    };

    /// <summary>"1 photo", "3 photos".</summary>
    public static string Count(MediaGroup group, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? One(group) : Many(group))}");

    /// <summary>The group a file belongs to, or null if the app cannot read it.</summary>
    public static MediaGroup? Of(string path)
    {
        if (!InputFormats.TryGetKind(path, out var kind))
        {
            return null;
        }

        return kind switch
        {
            MediaKind.Video => MediaGroup.Video,
            MediaKind.Audio => MediaGroup.Audio,
            MediaKind.Animation => MediaGroup.Gif,
            _ => MediaGroup.Photo,
        };
    }

    /// <summary>The group a format naturally belongs to. Used to sort presets under the right kind of file.</summary>
    public static MediaGroup Of(TargetFormat format) => format switch
    {
        TargetFormat.Gif => MediaGroup.Gif,
        TargetFormat.Mp4 or TargetFormat.Mkv or TargetFormat.WebM or TargetFormat.Ogv or TargetFormat.Avi => MediaGroup.Video,
        TargetFormat.Mp3 or TargetFormat.Flac or TargetFormat.Ogg or TargetFormat.Wav or TargetFormat.Aac => MediaGroup.Audio,
        _ => MediaGroup.Photo,
    };
}

/// <summary>The formats offered for each kind of file, in the order and wording of the design.</summary>
public static class FormatCatalog
{
    private static readonly FormatChoice[] All =
    [
        new(MediaGroup.Photo, TargetFormat.WebP, "WebP", "Small and sharp. Works on every website.", "Smallest for the web", true, 0.16),
        new(MediaGroup.Photo, TargetFormat.Jpeg, "JPEG", "Opens in every app and on every device.", "Works everywhere", true, 0.32),
        new(MediaGroup.Photo, TargetFormat.JpegXl, "JPEG XL", "Smallest at the same quality. Newer apps only.", null, true, 0.12),
        new(MediaGroup.Photo, TargetFormat.Avif, "AVIF", "Very small. Newer browsers and apps only.", null, true, 0.13),
        new(MediaGroup.Photo, TargetFormat.Png, "PNG", "No quality loss. Keeps see-through backgrounds.", null, false, 1.05),
        new(MediaGroup.Photo, TargetFormat.Tiff, "TIFF", "For printing and archiving.", null, false, 1.6),
        new(MediaGroup.Photo, TargetFormat.Pdf, "PDF", "One page per photo.", null, false, 0.9),

        new(MediaGroup.Video, TargetFormat.Mp4, "MP4", "Plays on every phone, computer and TV.", "Plays everywhere", true, 0.28),
        new(MediaGroup.Video, TargetFormat.WebM, "WebM", "Small files for websites.", "Smallest for the web", true, 0.22),
        new(MediaGroup.Video, TargetFormat.Mkv, "MKV", "Keeps every audio track and subtitle.", null, true, 0.30),
        new(MediaGroup.Video, TargetFormat.Ogv, "OGV", "Open format for some web players.", null, true, 0.34),
        new(MediaGroup.Video, TargetFormat.Avi, "AVI", "For older players and TVs.", null, true, 0.60),
        new(MediaGroup.Video, TargetFormat.Gif, "GIF", "Short silent animation. Files get much larger.", null, true, 2.5),

        new(MediaGroup.Gif, TargetFormat.Gif, "GIF", "Animated image that plays everywhere.", "Stays a GIF", true, 0.75),
        new(MediaGroup.Gif, TargetFormat.Mp4, "MP4 video", "Much smaller, but no longer a GIF.", null, true, 0.12),
        new(MediaGroup.Gif, TargetFormat.WebM, "WebM video", "Smallest, for websites.", null, true, 0.10),

        new(MediaGroup.Audio, TargetFormat.Mp3, "MP3", "Plays on every device.", "Plays everywhere", true, 0.18),
        new(MediaGroup.Audio, TargetFormat.Aac, "AAC", "Small. Good for iPhone and Apple apps.", null, true, 0.15),
        new(MediaGroup.Audio, TargetFormat.Ogg, "OGG", "Open format.", null, true, 0.16),
        new(MediaGroup.Audio, TargetFormat.Flac, "FLAC", "No quality loss, about half the size of WAV.", null, false, 0.55),
        new(MediaGroup.Audio, TargetFormat.Wav, "WAV", "Uncompressed. Very large files.", null, false, 1.0),
    ];

    public static IReadOnlyList<FormatChoice> For(MediaGroup group) => All.Where(choice => choice.Group == group).ToList();

    /// <summary>The format picked first for each group: the one that works for most people.</summary>
    public static TargetFormat Default(MediaGroup group) => group switch
    {
        MediaGroup.Photo => TargetFormat.WebP,
        MediaGroup.Video => TargetFormat.Mp4,
        MediaGroup.Gif => TargetFormat.Gif,
        _ => TargetFormat.Mp3,
    };

    /// <summary>Finds a format in a group. Falls back to the group's default if the format is not offered there.</summary>
    public static FormatChoice Get(MediaGroup group, TargetFormat format) =>
        For(group).FirstOrDefault(choice => choice.Format == format) ?? For(group).First(choice => choice.Format == Default(group));

    public static bool IsOffered(MediaGroup group, TargetFormat format) =>
        For(group).Any(choice => choice.Format == format);
}

/// <summary>Rough output sizes, so the app can say "about 3 MB" before converting. These are estimates, not promises.</summary>
public static class SizeEstimator
{
    /// <summary>Expected output size in bytes for a file converted at a quality from 10 to 100.</summary>
    public static long Estimate(long inputBytes, FormatChoice choice, int quality)
    {
        var q = Math.Clamp(quality, QualityMapper.Lowest, QualityMapper.Highest);
        var qualityFactor = choice.Lossy ? 0.35 + q / 100.0 * 1.1 : 1.0;
        return (long)Math.Max(1, inputBytes * choice.SizeFactor * qualityFactor);
    }

    public static long Estimate(long inputBytes, MediaGroup group, TargetFormat format, int quality) =>
        Estimate(inputBytes, FormatCatalog.Get(group, format), quality);
}

/// <summary>A choice in the "Size" list, with the change it makes.</summary>
public sealed record SizeChoice(string Label, Transform Changes);

public static class SizeChoices
{
    public static IReadOnlyList<SizeChoice> All { get; } =
    [
        new("Original size", Transform.None),
        new("Half size (50%)", Transform.Percent(50)),
        new("Fit within 1920 px", new Transform(MaxWidth: 1920)),
        new("1080p (Full HD)", Transform.FitHeight(1080)),
        new("720p (HD)", Transform.FitHeight(720)),
        new("Fit within 480 px", new Transform(MaxWidth: 480)),
    ];

    /// <summary>Only the size part of a transform, without rotation or flips.</summary>
    public static Transform SizeOnly(Transform changes) => changes with { Rotation = 0, FlipHorizontal = false, FlipVertical = false };

    /// <summary>Plain text for the size change, or null when the size is not changed.</summary>
    public static string? Describe(Transform changes)
    {
        if (changes.ScalePercent is { } percent)
        {
            return percent == 50 ? "Half size" : $"Scaled to {percent}%";
        }

        if (changes.MaxWidth is { } width && changes.MaxHeight is { } height)
        {
            return $"Fit within {width} × {height} px";
        }

        if (changes.MaxWidth is { } onlyWidth)
        {
            return $"Fit within {onlyWidth} px";
        }

        return changes.MaxHeight is { } onlyHeight ? $"Fit within {onlyHeight} px tall" : null;
    }
}

public static class SizeText
{
    private const long Megabyte = 1024 * 1024;

    /// <summary>"640 KB", "4.2 MB", "48 MB", "1.2 GB".</summary>
    public static string Of(long bytes)
    {
        if (bytes < Megabyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (long)Math.Round(bytes / 1024.0))} KB");
        }

        if (bytes < 1024 * Megabyte)
        {
            var megabytes = bytes / (double)Megabyte;
            return megabytes < 10
                ? string.Create(CultureInfo.InvariantCulture, $"{megabytes:0.0} MB")
                : string.Create(CultureInfo.InvariantCulture, $"{megabytes:0} MB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / Megabyte:0.0} GB");
    }

    /// <summary>"0:42", "3:05", "1:02:10".</summary>
    public static string Duration(double seconds)
    {
        var total = (int)Math.Round(Math.Max(0, seconds));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{secs:00}");
    }

    /// <summary>"about 2 min left", "about 40 s left".</summary>
    public static string TimeLeft(double seconds) => seconds switch
    {
        < 8 => "almost done",
        < 90 => string.Create(CultureInfo.InvariantCulture, $"about {Math.Round(seconds / 5) * 5:0} s left"),
        < 5400 => string.Create(CultureInfo.InvariantCulture, $"about {Math.Round(seconds / 60):0} min left"),
        _ => string.Create(CultureInfo.InvariantCulture, $"about {seconds / 3600:0.#} h left"),
    };
}
