using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class PresetTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "mercury-presets-" + Guid.NewGuid().ToString("N"));

    public PresetTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Every_built_in_preset_is_valid_and_builds_a_plan()
    {
        foreach (var preset in PresetStore.BuiltIn)
        {
            Assert.True(preset.Validate() is null, $"{preset.Name}: {preset.Validate()}");

            var sample = Targets.Get(preset.Target).Kind switch
            {
                MediaKind.Video => @"C:\in\sample.mov",
                MediaKind.Audio => @"C:\in\sample.wav",
                MediaKind.Animation => @"C:\in\sample.mp4",
                _ => @"C:\in\sample.png",
            };

            var request = new ConversionRequest(sample, preset.Target, preset.Quality, preset.LosslessJpegRecompression, preset.Changes, preset.ExtraFfmpegArguments);
            var plan = CommandBuilder.Build(request, @"C:\out\result" + "." + Targets.Get(preset.Target).Extension, @"C:\Temp");
            Assert.NotEmpty(plan.Steps);
        }
    }

    [Fact]
    public void Saving_writes_only_user_presets_and_loading_restores_them()
    {
        var store = new PresetStore(Path.Combine(folder, "presets.json"));
        var mine = new Preset { Name = "My blog images", Target = TargetFormat.WebP, Quality = 65, Changes = new Transform(MaxWidth: 1200) };

        store.Save([.. PresetStore.BuiltIn, mine]);
        var loaded = store.Load();

        Assert.Equal(PresetStore.BuiltIn.Count + 1, loaded.Count);
        var restored = loaded.Single(p => p.Name == "My blog images");
        Assert.Equal(TargetFormat.WebP, restored.Target);
        Assert.Equal(65, restored.Quality);
        Assert.Equal(1200, restored.Changes.MaxWidth);
        Assert.Equal(1, File.ReadAllText(store.UserFilePath).Split("My blog images").Length - 1);
    }

    [Fact]
    public void Save_refuses_an_unsafe_preset()
    {
        var store = new PresetStore(Path.Combine(folder, "presets.json"));
        var unsafePreset = new Preset { Name = "Bad", Target = TargetFormat.Mp4, ExtraFfmpegArguments = "-i C:\\secret.mp4" };

        Assert.Throws<ArgumentException>(() => store.Save([unsafePreset]));
        Assert.False(File.Exists(store.UserFilePath));
    }

    [Fact]
    public void Import_accepts_valid_presets_with_new_ids_and_reports_bad_ones()
    {
        var path = Path.Combine(folder, "shared.json");
        var good = new Preset { Name = "Good", Target = TargetFormat.Mp3, Quality = 80 };
        var bad = new Preset { Name = "Bad quality", Target = TargetFormat.Mp3, Quality = 0 };
        PresetStore.Export([good, bad, PresetStore.BuiltIn[0]], path);

        var result = PresetStore.Import(path);

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal("Good", accepted.Name);
        Assert.NotEqual(good.Id, accepted.Id);
        Assert.False(accepted.IsBuiltIn);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void Import_rejects_a_file_that_is_not_json()
    {
        var path = Path.Combine(folder, "broken.json");
        File.WriteAllText(path, "this is not json");

        var result = PresetStore.Import(path);

        Assert.Empty(result.Accepted);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void Import_rejects_a_preset_with_a_null_transform()
    {
        var path = Path.Combine(folder, "null-transform.json");
        File.WriteAllText(path, """[{ "Name": "x", "Target": "Mp3", "Changes": null }]""");

        var result = PresetStore.Import(path);

        Assert.Empty(result.Accepted);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void A_damaged_preset_file_is_kept_aside_and_the_app_still_starts()
    {
        var userFile = Path.Combine(folder, "presets.json");
        File.WriteAllText(userFile, "{ damaged");
        var store = new PresetStore(userFile);

        var loaded = store.Load();

        Assert.Equal(PresetStore.BuiltIn.Count, loaded.Count);
        Assert.True(File.Exists(userFile + ".damaged"));
    }
}
