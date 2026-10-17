using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mercury.Converter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Transform = Mercury.Converter.Core.Transform;

namespace Mercury.Converter.App.ViewModels;

/// <summary>
/// Everything the main window shows. This part holds the settings, the file list, and the commands that change them.
/// The other parts work out what to display (<c>MainViewModel.Display.cs</c>) and run conversions (<c>MainViewModel.Run.cs</c>).
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ToolLocator tools = ToolLocator.Default;
    private readonly ConversionRunner runner;
    private readonly ThumbnailService thumbnails;
    private readonly IResourceSampler sampler = new SystemResourceSampler();
    private readonly AppSettingsStore settingsStore = AppSettingsStore.Default;
    private readonly PresetStore presetStore = PresetStore.Default;
    private readonly Dictionary<MediaGroup, TargetFormat> targets = [];
    private readonly SemaphoreSlim probeLimit = new(2);

    private HardwareEncoder detectedEncoder = HardwareEncoder.None;
    private int addedCounter;
    private string filterKey = "all";
    private Guid? activePresetId;
    private bool suppressRefresh;
    private bool initializing;

    // When the right-click menu supplies a format or quality for one run, the user's own choices are kept here so they are not overwritten.
    private Dictionary<MediaGroup, TargetFormat>? savedTargets;
    private double? savedQuality;

    public MainViewModel()
    {
        runner = new ConversionRunner(tools);
        thumbnails = ThumbnailService.CreateDefault(tools);

        var settings = settingsStore.Load();
        foreach (var group in Enum.GetValues<MediaGroup>())
        {
            targets[group] = FormatCatalog.IsOffered(group, settings.TargetFor(group))
                ? settings.TargetFor(group)
                : FormatCatalog.Default(group);
        }

        // Setting the properties would normally save and refresh, so that is switched off while loading.
        initializing = true;
        Quality = Math.Clamp(Math.Round(settings.LastQuality / 5.0) * 5, 10, 100);
        NeverLarger = settings.KeepSmallerThanOriginal;
        OutputFolder = settings.OutputFolder;
        ShowTaskbarProgress = settings.ShowTaskbarProgress;
        UseGraphicsCardEncoder = settings.UseGraphicsCardEncoder;
        UseQuickWindow = settings.UseQuickWindow;
        RightClickTipShown = settings.RightClickTipShown;
        ThumbnailSetting = settings.Thumbnails;
        SelectedMode = ModeChoices.First(choice => choice.Value == settings.Performance);
        SelectedSpeed = SpeedChoices.First(choice => choice.Value == settings.VideoSpeed);
        SelectedChannels = ChannelChoices.First(choice => choice.Value == settings.AudioChannels);
        SelectedSize = SizeChoices.All[0];
        RightClickOn = Services.ShellIntegration.IsInstalled();
        initializing = false;

        View.Apply(ThumbnailSetting);
        Presets = new ObservableCollection<Preset>(presetStore.Load());

        // Housekeeping and hardware detection happen in the background so the window opens straight away.
        _ = Task.Run(() => thumbnails.Prune(200L * 1024 * 1024));
        _ = DetectGraphicsEncoderAsync();
        Refresh();
    }

    // ----- The file list ----------------------------------------------------------------------------------------

    /// <summary>Every file the user added, in the order added.</summary>
    public ObservableCollection<FileItem> Files { get; } = [];

    /// <summary>The files shown: filtered and sorted. Rebuilt by <see cref="Refresh"/>.</summary>
    public ObservableCollection<FileItem> VisibleFiles { get; } = [];

    public ViewOptions View { get; } = new();

    public ObservableCollection<Preset> Presets { get; }

    public IReadOnlyList<ListChoice<PerformanceMode>> ModeChoices { get; } =
    [
        new("Gentle", PerformanceMode.Gentle),
        new("Balanced", PerformanceMode.Smart),
        new("Fastest", PerformanceMode.Maximum),
    ];

    public IReadOnlyList<ListChoice<VideoSpeed>> SpeedChoices { get; } =
    [
        new("Faster", VideoSpeed.Faster),
        new("Balanced", VideoSpeed.Balanced),
        new("Slower", VideoSpeed.Slower),
    ];

    public IReadOnlyList<ListChoice<AudioChannels>> ChannelChoices { get; } =
    [
        new("Keep as is", AudioChannels.KeepAsIs),
        new("Mono", AudioChannels.Mono),
        new("Stereo", AudioChannels.Stereo),
    ];

    public IReadOnlyList<ListChoice<ThumbnailSize>> PreviewChoices { get; } =
    [
        new("Off", ThumbnailSize.Off),
        new("Small", ThumbnailSize.Small),
        new("Medium", ThumbnailSize.Medium),
        new("Large", ThumbnailSize.Large),
    ];

    public IReadOnlyList<ListChoice<SortMode>> SortChoices { get; } =
    [
        new("Added", SortMode.Added),
        new("Name", SortMode.Name),
        new("Size", SortMode.Size),
        new("Type", SortMode.Type),
    ];

    public IReadOnlyList<ListChoice<bool>> QuickWindowChoices { get; } =
    [
        new("Open the small window", true),
        new("Open the full window", false),
    ];

    public IReadOnlyList<SizeChoice> SizeList => SizeChoices.All;

    // ----- Settings the user can change -------------------------------------------------------------------------

    /// <summary>1 to 100 in steps of 5. The lowest value is 10.</summary>
    [ObservableProperty]
    public partial double Quality { get; set; }

    [ObservableProperty]
    public partial bool NeverLarger { get; set; }

    [ObservableProperty]
    public partial string? OutputFolder { get; set; }

    [ObservableProperty]
    public partial SizeChoice SelectedSize { get; set; }

    /// <summary>Clockwise turns of 90 degrees: 0, 90, 180, or 270.</summary>
    [ObservableProperty]
    public partial int Rotation { get; set; }

    [ObservableProperty]
    public partial bool FlipHorizontal { get; set; }

    [ObservableProperty]
    public partial bool FlipVertical { get; set; }

    [ObservableProperty]
    public partial ListChoice<VideoSpeed> SelectedSpeed { get; set; }

    [ObservableProperty]
    public partial ListChoice<AudioChannels> SelectedChannels { get; set; }

    [ObservableProperty]
    public partial ListChoice<PerformanceMode> SelectedMode { get; set; }

    [ObservableProperty]
    public partial bool UseGraphicsCardEncoder { get; set; }

    [ObservableProperty]
    public partial bool GraphicsEncoderAvailable { get; set; }

    [ObservableProperty]
    public partial string GraphicsEncoderText { get; set; } = "Checking your graphics card…";

    [ObservableProperty]
    public partial ThumbnailSize ThumbnailSetting { get; set; }

    [ObservableProperty]
    public partial bool ShowTaskbarProgress { get; set; }

    [ObservableProperty]
    public partial bool UseQuickWindow { get; set; }

    [ObservableProperty]
    public partial bool RightClickOn { get; set; }

    [ObservableProperty]
    public partial bool RightClickTipShown { get; set; }

    [ObservableProperty]
    public partial SortMode Sort { get; set; } = SortMode.Added;

    // ----- Window state -----------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    /// <summary>At narrow widths the inspector becomes a pane that opens over the files.</summary>
    [ObservableProperty]
    public partial bool IsPaneOpen { get; set; }

    [ObservableProperty]
    public partial bool MoreOptionsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Overall progress across the run, 0 to 1. Drives the taskbar progress bar.</summary>
    [ObservableProperty]
    public partial double OverallProgress { get; set; }

    // ----- Reacting to changes ----------------------------------------------------------------------------------

    partial void OnQualityChanged(double value)
    {
        if (initializing)
        {
            return;
        }

        savedQuality = null;
        ActivePresetCleared();
        SaveSettings();
        Refresh();
    }

    partial void OnNeverLargerChanged(bool value) => SaveUnlessLoading();

    partial void OnSelectedSizeChanged(SizeChoice value)
    {
        if (initializing)
        {
            return;
        }

        ActivePresetCleared();
        Refresh();
    }

    partial void OnRotationChanged(int value) => RefreshUnlessLoading();

    partial void OnFlipHorizontalChanged(bool value) => RefreshUnlessLoading();

    partial void OnFlipVerticalChanged(bool value) => RefreshUnlessLoading();

    partial void OnSelectedSpeedChanged(ListChoice<VideoSpeed> value) => SaveUnlessLoading();

    partial void OnSelectedChannelsChanged(ListChoice<AudioChannels> value) => SaveUnlessLoading();

    partial void OnSelectedModeChanged(ListChoice<PerformanceMode> value) => SaveUnlessLoading();

    partial void OnUseGraphicsCardEncoderChanged(bool value) => SaveUnlessLoading();

    partial void OnShowTaskbarProgressChanged(bool value) => SaveUnlessLoading();

    partial void OnUseQuickWindowChanged(bool value) => SaveUnlessLoading();

    partial void OnOutputFolderChanged(string? value)
    {
        if (initializing)
        {
            return;
        }

        SaveSettings();
        Refresh();
    }

    partial void OnSortChanged(SortMode value) => RefreshUnlessLoading();

    partial void OnMoreOptionsOpenChanged(bool value) => RefreshUnlessLoading();

    partial void OnIsBusyChanged(bool value)
    {
        ConvertCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        Refresh();
    }

    partial void OnThumbnailSettingChanged(ThumbnailSize value)
    {
        if (initializing)
        {
            return;
        }

        View.Apply(value);
        SaveSettings();
        foreach (var item in Files)
        {
            item.Thumbnail = null;
            _ = LoadThumbnailAsync(item);
        }
    }

    private void SaveUnlessLoading()
    {
        if (!initializing)
        {
            SaveSettings();
        }
    }

    private void RefreshUnlessLoading()
    {
        if (!initializing)
        {
            Refresh();
        }
    }

    private void ActivePresetCleared()
    {
        if (activePresetId is not null)
        {
            activePresetId = null;
        }
    }

    // ----- Adding and removing files ----------------------------------------------------------------------------

    /// <summary>Adds the files the app can read and skips the rest. Files already in the list are ignored.</summary>
    /// <returns>How many files were added and how many were skipped as unsupported.</returns>
    public (int Added, int Skipped) AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = 0;
        var existing = new HashSet<string>(Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

        suppressRefresh = true;
        foreach (var path in paths)
        {
            if (!File.Exists(path) || !existing.Add(path))
            {
                continue;
            }

            if (MediaGroups.Of(path) is not { } group)
            {
                skipped++;
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                continue;
            }

            var item = new FileItem(path, group, size, addedCounter++);
            Files.Add(item);
            added++;
            _ = LoadThumbnailAsync(item);
            _ = LoadDurationAsync(item);
        }

        suppressRefresh = false;
        Refresh();

        Notice = (added, skipped) switch
        {
            (0, 0) => "No new supported files.",
            (_, 0) => null,
            _ => $"Skipped {skipped} file{(skipped == 1 ? string.Empty : "s")} Mercury can't read yet.",
        };
        OnPropertyChanged(nameof(Notice));
        return (added, skipped);
    }

    /// <summary>A short message about the last add, for the status line. Null when there is nothing to say.</summary>
    public string? Notice { get; private set; }

    /// <summary>Adds the supported files directly inside a folder.</summary>
    public void AddFolder(string folder)
    {
        try
        {
            AddFiles(Directory.EnumerateFiles(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice = $"That folder could not be read: {ex.Message}";
            OnPropertyChanged(nameof(Notice));
        }
    }

    public void RemoveItems(IEnumerable<FileItem> items)
    {
        var remove = items.ToList();
        if (remove.Count == 0 || IsBusy && remove.Any(item => item.State is ItemState.Active))
        {
            return;
        }

        suppressRefresh = true;
        foreach (var item in remove)
        {
            Files.Remove(item);
        }

        suppressRefresh = false;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanClearList))]
    private void ClearList()
    {
        Files.Clear();
        filterKey = "all";
        BannerDismissed = false;
        Notice = null;
        Refresh();
    }

    private bool CanClearList() => !IsBusy && Files.Count > 0;

    // ----- Selection --------------------------------------------------------------------------------------------

    public IReadOnlyList<FileItem> SelectedItems => Files.Where(f => f.IsSelected).ToList();

    public void ToggleSelection(FileItem item)
    {
        item.IsSelected = !item.IsSelected;
        ActivePresetCleared();
        Refresh();
    }

    public void SelectAll()
    {
        foreach (var item in VisibleFiles)
        {
            item.IsSelected = true;
        }

        Refresh();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Files)
        {
            item.IsSelected = false;
        }

        Refresh();
    }

    // ----- Previews and durations -------------------------------------------------------------------------------

    private async Task LoadThumbnailAsync(FileItem item)
    {
        var pixels = ThumbnailSizes.Pixels(ThumbnailSetting == ThumbnailSize.Off ? ThumbnailSize.Medium : ThumbnailSetting);

        // The cards are 116 px tall at any width, so one medium-size preview is sharp enough for all of them.
        pixels = Math.Max(pixels, 160);

        try
        {
            var picture = await thumbnails.GetAsync(item.Path, pixels);
            if (picture is not null && Files.Contains(item))
            {
                item.Thumbnail = new BitmapImage { DecodePixelWidth = pixels, UriSource = new Uri(picture) };
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            // A missing preview just means the card keeps its colored tile and icon.
        }
    }

    private async Task LoadDurationAsync(FileItem item)
    {
        if (item.Group is MediaGroup.Photo or MediaGroup.Gif)
        {
            return;
        }

        await probeLimit.WaitAsync();
        try
        {
            var seconds = await MediaProbe.GetDurationSecondsAsync(item.Path, tools);
            if (seconds is not null && Files.Contains(item))
            {
                item.DurationText = SizeText.Duration(seconds.Value);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            // The length is only decoration. Without it the card simply has no badge.
        }
        finally
        {
            probeLimit.Release();
        }
    }

    // ----- Hardware ---------------------------------------------------------------------------------------------

    private async Task DetectGraphicsEncoderAsync()
    {
        try
        {
            detectedEncoder = await new HardwareProbe(tools).DetectEncoderAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            detectedEncoder = HardwareEncoder.None;
        }

        GraphicsEncoderAvailable = detectedEncoder != HardwareEncoder.None;
        GraphicsEncoderText = detectedEncoder switch
        {
            HardwareEncoder.Nvenc => "NVIDIA graphics card found",
            HardwareEncoder.Amf => "AMD graphics card found",
            HardwareEncoder.Qsv => "Intel graphics found",
            _ => "No supported graphics card found",
        };
    }

    // ----- Saving settings --------------------------------------------------------------------------------------

    /// <summary>Saves the remembered choices. Called when a setting changes and when the window closes.</summary>
    public void SaveSettings()
    {
        try
        {
            settingsStore.Save(new AppSettings
            {
                OutputFolder = OutputFolder,
                LastQuality = (int)Math.Round(savedQuality ?? Quality),
                KeepSmallerThanOriginal = NeverLarger,
                ShowTaskbarProgress = ShowTaskbarProgress,
                Performance = SelectedMode.Value,
                Thumbnails = ThumbnailSetting,
                UseGraphicsCardEncoder = UseGraphicsCardEncoder,
                PhotoTarget = (savedTargets ?? targets)[MediaGroup.Photo],
                VideoTarget = (savedTargets ?? targets)[MediaGroup.Video],
                GifTarget = (savedTargets ?? targets)[MediaGroup.Gif],
                AudioTarget = (savedTargets ?? targets)[MediaGroup.Audio],
                UseQuickWindow = UseQuickWindow,
                RightClickTipShown = RightClickTipShown,
                VideoSpeed = SelectedSpeed.Value,
                AudioChannels = SelectedChannels.Value,
            });
        }
        catch (IOException)
        {
            // Settings are a convenience. A failure here must never block a conversion.
        }
    }

    // ----- Helpers shared with the other parts ------------------------------------------------------------------

    internal TargetFormat TargetFor(MediaGroup group) => targets[group];

    /// <summary>The name a converted file gets: same name, new extension. A "-converted" suffix avoids clashing with the original.</summary>
    internal string OutputFileName(FileItem item)
    {
        var target = Targets.Get(targets[item.Group]);
        var baseName = Path.GetFileNameWithoutExtension(item.Path);
        var sameExtension = string.Equals(item.Extension, target.Extension, StringComparison.OrdinalIgnoreCase);
        return $"{(sameExtension ? baseName + "-converted" : baseName)}.{target.Extension}";
    }

    internal string OutputPathFor(FileItem item)
    {
        var directory = OutputFolder ?? Path.GetDirectoryName(item.Path) ?? string.Empty;
        return Path.Combine(directory, OutputFileName(item));
    }

    /// <summary>The size, rotation, and flips that apply to a kind of file.</summary>
    internal Transform ChangesFor(MediaGroup group)
    {
        var size = SelectedSize.Changes;
        return group switch
        {
            MediaGroup.Audio => Transform.None,
            MediaGroup.Gif => size,
            _ => size with { Rotation = Rotation, FlipHorizontal = FlipHorizontal, FlipVertical = FlipVertical },
        };
    }

    public static void ShowInFolder(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else if (Path.GetDirectoryName(path) is { } directory && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
    }

    public static void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public static void CopyText(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private static string Plural(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");
}
