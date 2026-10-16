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
