using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mercury.Converter.Core;

/// <summary>A saved set of conversion settings with a name, so the user can repeat it in one click.</summary>
public sealed record Preset
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public required TargetFormat Target { get; init; }

    public int Quality { get; init; } = QualityMapper.Balanced;

    public bool LosslessJpegRecompression { get; init; }

    public bool KeepSmallerThanOriginal { get; init; } = true;

    public Transform Changes { get; init; } = Transform.None;

    /// <summary>Advanced FFmpeg options. Checked against an allowlist before use. Empty for most presets.</summary>
    public string? ExtraFfmpegArguments { get; init; }

    /// <summary>Built-in presets are shown in the app but are never written to the user's file.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Returns the first problem with these settings, or null if they are safe to use.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80)
        {
            return "A preset needs a name of 1 to 80 characters.";
        }

        if (!Enum.IsDefined(Target))
        {
            return "The preset's output format is not recognised.";
        }

        if (Quality is < QualityMapper.Lowest or > QualityMapper.Highest)
        {
            return $"Quality must be between {QualityMapper.Lowest} and {QualityMapper.Highest}.";
        }

        if (Changes is null || !Changes.IsValid)
        {
            return "The resize, rotate, or flip settings are not valid.";
        }

        if (!string.IsNullOrWhiteSpace(ExtraFfmpegArguments))
        {
            try
            {
                FfmpegOptions.Parse(ExtraFfmpegArguments);
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
        }

        return null;
    }
}

/// <summary>Built-in presets and the user's own presets, saved as JSON.</summary>
public sealed class PresetStore(string userFilePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string UserFilePath { get; } = userFilePath;

    /// <summary>Default location: %LOCALAPPDATA%\MercuryConverter\presets.json.</summary>
    public static PresetStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MercuryConverter",
        "presets.json"));

    public static IReadOnlyList<Preset> BuiltIn { get; } =
    [
        new() { Name = "Web photo (WebP, 1920 px)", Target = TargetFormat.WebP, Quality = 75, Changes = new Transform(MaxWidth: 1920), IsBuiltIn = true },
        new() { Name = "Phone share (JPEG, 1080 px)", Target = TargetFormat.Jpeg, Quality = 80, Changes = new Transform(MaxHeight: 1080), IsBuiltIn = true },
        new() { Name = "Smallest photo (JPEG XL)", Target = TargetFormat.JpegXl, Quality = 60, IsBuiltIn = true },
        new() { Name = "Keep exact JPEG (JPEG XL, reversible)", Target = TargetFormat.JpegXl, Quality = 100, LosslessJpegRecompression = true, IsBuiltIn = true },
        new() { Name = "Archive (lossless PNG)", Target = TargetFormat.Png, Quality = 100, IsBuiltIn = true },
        new() { Name = "Video for sharing (MP4, 1080 p)", Target = TargetFormat.Mp4, Quality = 75, Changes = new Transform(MaxHeight: 1080), IsBuiltIn = true },
        new() { Name = "Video for web (WebM, 720 p)", Target = TargetFormat.WebM, Quality = 70, Changes = new Transform(MaxHeight: 720), IsBuiltIn = true },
        new() { Name = "Short GIF (480 px)", Target = TargetFormat.Gif, Quality = 70, Changes = new Transform(MaxWidth: 480), IsBuiltIn = true },
        new() { Name = "Podcast audio (MP3)", Target = TargetFormat.Mp3, Quality = 70, IsBuiltIn = true },
        new() { Name = "Photos to PDF", Target = TargetFormat.Pdf, IsBuiltIn = true },
    ];

    public IReadOnlyList<Preset> Load()
    {
        var user = ReadUserFile().Where(p => p.Validate() is null).ToList();
        return BuiltIn.Concat(user).ToList();
    }

    /// <summary>Saves the user's presets. Built-in presets are never written.</summary>
    public void Save(IEnumerable<Preset> presets)
    {
        var userOnly = presets.Where(p => !p.IsBuiltIn).ToList();
        foreach (var preset in userOnly)
        {
            var problem = preset.Validate();
            if (problem is not null)
            {
                throw new ArgumentException($"'{preset.Name}' cannot be saved: {problem}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(UserFilePath)!);
        var temp = UserFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(userOnly, JsonOptions));
        File.Move(temp, UserFilePath, overwrite: true);
    }

    /// <summary>Writes presets to a file the user can share.</summary>
    public static void Export(IEnumerable<Preset> presets, string path)
    {
        var shareable = presets.Where(p => !p.IsBuiltIn).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(shareable, JsonOptions));
    }

    /// <summary>
    /// Reads presets from a shared file. Each one is validated, and every preset gets a new id,
    /// so importing never overwrites or duplicates existing ones. Invalid entries are skipped and reported.
    /// </summary>
    public static ImportResult Import(string path)
    {
        var accepted = new List<Preset>();
        var rejected = new List<string>();

        List<Preset>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return new ImportResult([], ["The file is not a valid preset file."]);
        }

        foreach (var preset in parsed ?? [])
        {
            var problem = preset.Validate();
            if (problem is null)
            {
                accepted.Add(preset with { Id = Guid.NewGuid(), IsBuiltIn = false });
            }
            else
            {
                rejected.Add($"'{preset.Name}': {problem}");
            }
        }

        return new ImportResult(accepted, rejected);
    }

    private List<Preset> ReadUserFile()
    {
        if (!File.Exists(UserFilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(UserFilePath), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // A damaged file must not stop the app from starting. The bad file is kept for the user to look at.
            File.Copy(UserFilePath, UserFilePath + ".damaged", overwrite: true);
            return [];
        }
    }
}

public sealed record ImportResult(IReadOnlyList<Preset> Accepted, IReadOnlyList<string> Rejected);
