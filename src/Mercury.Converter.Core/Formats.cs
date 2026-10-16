namespace Mercury.Converter.Core;

/// <summary>The kind of media a file contains.</summary>
public enum MediaKind
{
    Image,
    Animation,
    Video,
    Audio,
}

/// <summary>Every output format Mercury.Converter can write.</summary>
public enum TargetFormat
{
    Jpeg,
    Png,
    WebP,
    Avif,
    JpegXl,
    Tiff,
    Pdf,
    Gif,
    Mp4,
    Mkv,
    WebM,
    Ogv,
    Avi,
    Mp3,
    Flac,
    Ogg,
    Wav,
    Aac,
}

/// <summary>Describes one output format in plain terms for the UI.</summary>
public sealed record TargetInfo(
    TargetFormat Format,
    string Label,
    string Extension,
    MediaKind Kind,
    bool Lossy);

public static class Targets
{
    public static readonly IReadOnlyList<TargetInfo> All =
    [
        new(TargetFormat.Jpeg, "JPEG (photo)", "jpg", MediaKind.Image, Lossy: true),
        new(TargetFormat.Png, "PNG (lossless, transparency)", "png", MediaKind.Image, Lossy: false),
        new(TargetFormat.WebP, "WebP (small web images)", "webp", MediaKind.Image, Lossy: true),
        new(TargetFormat.Avif, "AVIF (smallest images)", "avif", MediaKind.Image, Lossy: true),
        new(TargetFormat.JpegXl, "JPEG XL (modern, small)", "jxl", MediaKind.Image, Lossy: true),
        new(TargetFormat.Tiff, "TIFF (print and archive)", "tif", MediaKind.Image, Lossy: false),
        new(TargetFormat.Pdf, "PDF (one page per image)", "pdf", MediaKind.Image, Lossy: false),
        new(TargetFormat.Gif, "GIF (animated)", "gif", MediaKind.Animation, Lossy: true),
        new(TargetFormat.Mp4, "MP4 video (most compatible)", "mp4", MediaKind.Video, Lossy: true),
        new(TargetFormat.Mkv, "MKV video (flexible container)", "mkv", MediaKind.Video, Lossy: true),
        new(TargetFormat.WebM, "WebM video (web)", "webm", MediaKind.Video, Lossy: true),
        new(TargetFormat.Ogv, "OGV video (open format)", "ogv", MediaKind.Video, Lossy: true),
        new(TargetFormat.Avi, "AVI video (older players)", "avi", MediaKind.Video, Lossy: true),
        new(TargetFormat.Mp3, "MP3 audio", "mp3", MediaKind.Audio, Lossy: true),
        new(TargetFormat.Flac, "FLAC audio (lossless)", "flac", MediaKind.Audio, Lossy: false),
        new(TargetFormat.Ogg, "OGG audio (Vorbis)", "ogg", MediaKind.Audio, Lossy: true),
        new(TargetFormat.Wav, "WAV audio (uncompressed)", "wav", MediaKind.Audio, Lossy: false),
        new(TargetFormat.Aac, "AAC audio", "aac", MediaKind.Audio, Lossy: true),
    ];

    public static TargetInfo Get(TargetFormat format) =>
        All.Single(t => t.Format == format);

    /// <summary>The short name shown in the right-click menu, for example "Jpg" or "Mp4".</summary>
    public static string MenuName(TargetFormat format) => format switch
    {
        TargetFormat.Tiff => "Tiff",
        TargetFormat.JpegXl => "Jxl",
        _ => char.ToUpperInvariant(Get(format).Extension[0]) + Get(format).Extension[1..],
    };
}

/// <summary>Maps file extensions to what Mercury.Converter can read.</summary>
public static class InputFormats
{
    private static readonly Dictionary<string, MediaKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Still images
        ["jpg"] = MediaKind.Image,
        ["jpeg"] = MediaKind.Image,
        ["jfif"] = MediaKind.Image,
        ["png"] = MediaKind.Image,
        ["bmp"] = MediaKind.Image,
        ["tif"] = MediaKind.Image,
        ["tiff"] = MediaKind.Image,
        ["webp"] = MediaKind.Image,
        ["avif"] = MediaKind.Image,
        ["heic"] = MediaKind.Image,
        ["heif"] = MediaKind.Image,
        ["jxl"] = MediaKind.Image,
        ["ico"] = MediaKind.Image,
        ["svg"] = MediaKind.Image,
        ["tga"] = MediaKind.Image,
        ["dng"] = MediaKind.Image,
        ["cr2"] = MediaKind.Image,
        ["nef"] = MediaKind.Image,
        ["arw"] = MediaKind.Image,
        ["raf"] = MediaKind.Image,
        // Animations
        ["gif"] = MediaKind.Animation,
        // Video
        ["mp4"] = MediaKind.Video,
        ["m4v"] = MediaKind.Video,
        ["mov"] = MediaKind.Video,
        ["mkv"] = MediaKind.Video,
        ["webm"] = MediaKind.Video,
        ["avi"] = MediaKind.Video,
        ["wmv"] = MediaKind.Video,
        ["flv"] = MediaKind.Video,
        ["mpg"] = MediaKind.Video,
        ["mpeg"] = MediaKind.Video,
        ["3gp"] = MediaKind.Video,
        ["3gpp"] = MediaKind.Video,
        ["ogv"] = MediaKind.Video,
        ["ts"] = MediaKind.Video,
        ["m2ts"] = MediaKind.Video,
        ["vob"] = MediaKind.Video,
        // Audio
        ["mp3"] = MediaKind.Audio,
        ["m4a"] = MediaKind.Audio,
        ["aac"] = MediaKind.Audio,
        ["flac"] = MediaKind.Audio,
        ["wav"] = MediaKind.Audio,
        ["ogg"] = MediaKind.Audio,
        ["opus"] = MediaKind.Audio,
        ["wma"] = MediaKind.Audio,
        ["oga"] = MediaKind.Audio,
        ["aiff"] = MediaKind.Audio,
        ["ape"] = MediaKind.Audio,
        ["m4b"] = MediaKind.Audio,
    };

    /// <summary>Every readable file extension (without the dot) and what kind of file it is.</summary>
    public static IEnumerable<(string Extension, MediaKind Kind)> All => Kinds.Select(pair => (pair.Key, pair.Value));

    public static bool TryGetKind(string path, out MediaKind kind)
    {
        var extension = Path.GetExtension(path).TrimStart('.');
        return Kinds.TryGetValue(extension, out kind);
    }

    public static bool IsSupported(string path) => TryGetKind(path, out _);
}
