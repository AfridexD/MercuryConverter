using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "mercury-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Settings_round_trip_through_the_file()
    {
        var store = new AppSettingsStore(Path.Combine(folder, "settings.json"));
        var settings = new AppSettings
        {
            OutputFolder = @"D:\Exports",
            LastTarget = TargetFormat.WebP,
            LastQuality = 55,
            KeepSmallerThanOriginal = false,
            ShowTaskbarProgress = false,
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(@"D:\Exports", loaded.OutputFolder);
        Assert.Equal(TargetFormat.WebP, loaded.LastTarget);
        Assert.Equal(55, loaded.LastQuality);
        Assert.False(loaded.KeepSmallerThanOriginal);
        Assert.False(loaded.ShowTaskbarProgress);
    }

    [Fact]
    public void Missing_or_damaged_settings_fall_back_to_defaults()
    {
        var missing = new AppSettingsStore(Path.Combine(folder, "none.json")).Load();
        Assert.Equal(QualityMapper.Balanced, missing.LastQuality);

        var damagedPath = Path.Combine(folder, "damaged.json");
        File.WriteAllText(damagedPath, "{ nope");
        var damaged = new AppSettingsStore(damagedPath).Load();
        Assert.True(damaged.KeepSmallerThanOriginal);
    }
}
