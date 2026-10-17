using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mercury.Converter.Core;

/// <summary>Settings the app remembers between runs.</summary>
public sealed record AppSettings
{
    /// <summary>Where to save results. Null means next to each original file.</summary>
    public string? OutputFolder { get; init; }

    public TargetFormat LastTarget { get; init; } = TargetFormat.Jpeg;

    public int LastQuality { get; init; } = QualityMapper.Balanced;

    public bool KeepSmallerThanOriginal { get; init; } = true;

    public bool LosslessJpegRecompression { get; init; } = true;

    /// <summary>Last preset used, so the app can select it on start.</summary>
    public Guid? LastPresetId { get; init; }

    /// <summary>Taskbar progress on by default. Users who find it distracting can turn it off.</summary>
    public bool ShowTaskbarProgress { get; init; } = true;

    /// <summary>How much of the computer a conversion may use.</summary>
    public PerformanceMode Performance { get; init; } = PerformanceMode.Smart;

    /// <summary>How large the file previews are. Off shows a compact list.</summary>
    public ThumbnailSize Thumbnails { get; init; } = ThumbnailSize.Medium;

    /// <summary>
    /// Encode video on the graphics card. This uses much less processor time, but the files come out larger,
    /// so it is off unless the user turns it on.
    /// </summary>
    public bool UseGraphicsCardEncoder { get; init; }

    /// <summary>The format chosen for each kind of file. Each kind remembers its own choice.</summary>
    public TargetFormat PhotoTarget { get; init; } = FormatCatalog.Default(MediaGroup.Photo);

    public TargetFormat VideoTarget { get; init; } = FormatCatalog.Default(MediaGroup.Video);

    public TargetFormat GifTarget { get; init; } = FormatCatalog.Default(MediaGroup.Gif);

    public TargetFormat AudioTarget { get; init; } = FormatCatalog.Default(MediaGroup.Audio);

    /// <summary>
    /// When one file is converted from the right-click menu, show the small Quick Convert window.
    /// When off, the full window opens instead.
    /// </summary>
    public bool UseQuickWindow { get; init; } = true;

    /// <summary>The right-click tip is shown once. This remembers that it has been shown.</summary>
    public bool RightClickTipShown { get; init; }

    /// <summary>How fast video is encoded. Faster makes bigger files, slower makes smaller ones.</summary>
    public VideoSpeed VideoSpeed { get; init; } = VideoSpeed.Balanced;

    public AudioChannels AudioChannels { get; init; } = AudioChannels.KeepAsIs;

    /// <summary>Ask GitHub once a day whether a newer version exists. Off until the user says yes.</summary>
    public bool CheckForUpdates { get; init; }

    /// <summary>Remove location, camera and date details from photos, video and audio. Off unless the user turns it on.</summary>
    public bool StripMetadata { get; init; }

    /// <summary>Show a Windows notification when a run finishes while the window is not in front.</summary>
    public bool NotifyWhenDone { get; init; } = true;

    /// <summary>The first-run question about update checks has been answered, either way.</summary>
    public bool UpdatePromptAnswered { get; init; }

    /// <summary>A version the user chose not to hear about again, such as "1.1.0".</summary>
    public string? SkippedVersion { get; init; }

    public DateTime? LastUpdateCheckUtc { get; init; }

    /// <summary>Gets the saved format for a kind of file.</summary>
    public TargetFormat TargetFor(MediaGroup group) => group switch
    {
        MediaGroup.Photo => PhotoTarget,
        MediaGroup.Video => VideoTarget,
        MediaGroup.Gif => GifTarget,
        _ => AudioTarget,
    };
}

/// <summary>Reads and writes <see cref="AppSettings"/>. A damaged file falls back to defaults.</summary>
public sealed class AppSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettingsStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MercuryConverter",
        "settings.json"));

    public AppSettings Load()
    {
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, filePath, overwrite: true);
    }
}
