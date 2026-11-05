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
    private readonly QueueStore queueStore = QueueStore.Default;

    // Every path in the list, so adding thousands of files does not search the whole list for each one.
    private readonly HashSet<string> knownPaths = new(StringComparer.OrdinalIgnoreCase);

    // The cards that can be seen right now. Only these get a preview.
    private readonly HashSet<FileItem> shownCards = [];

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
        CheckForUpdates = settings.CheckForUpdates;
        Theme = settings.Theme;
        StripMetadata = settings.StripMetadata;
        NotifyWhenDone = settings.NotifyWhenDone;
        IncludeSubfolders = settings.IncludeSubfolders;
        KeepFolderLayout = settings.KeepFolderLayout;
        RememberList = settings.RememberList;
        ThumbnailSetting = settings.Thumbnails;
        SelectedMode = ModeChoices.First(choice => choice.Value == settings.Performance);
        SelectedSpeed = SpeedChoices.First(choice => choice.Value == settings.VideoSpeed);
        SelectedChannels = ChannelChoices.First(choice => choice.Value == settings.AudioChannels);
        SelectedSize = SizeChoices.All[0];
        RightClickOn = Services.ShellIntegration.IsInstalled();
        LoadUpdateState(settings);
        initializing = false;

        View.Apply(ThumbnailSetting);

        // Tiles drawn in code need new colors when the theme changes.
        // Only the cards on screen are recolored now. The others are recolored when they scroll into view, so changing
        // the theme with a list of tens of thousands of files is instant.
        Services.ThemeService.Changed += () =>
        {
            themeVersion++;
            foreach (var item in shownCards.ToList())
            {
                item.ThemeVersion = themeVersion;
                item.RefreshTheme();
            }
        };
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
    public BulkObservableCollection<FileItem> VisibleFiles { get; } = [];

    public ViewOptions View => ViewOptions.Shared;

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

    public IReadOnlyList<ListChoice<AppTheme>> ThemeChoices { get; } =
    [
        new("Use my Windows setting", AppTheme.System),
        new("Light", AppTheme.Light),
        new("Dark", AppTheme.Dark),
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
    public partial bool CheckForUpdates { get; set; }

    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    [ObservableProperty]
    public partial bool StripMetadata { get; set; }

    [ObservableProperty]
    public partial bool NotifyWhenDone { get; set; }

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    [ObservableProperty]
    public partial bool KeepFolderLayout { get; set; }

    [ObservableProperty]
    public partial bool RememberList { get; set; }

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

    partial void OnStripMetadataChanged(bool value) => SaveUnlessLoading();

    partial void OnThemeChanged(AppTheme value)
    {
        if (initializing)
        {
            return;
        }

        Services.ThemeService.SetPreference(value);
        SaveSettings();
    }

    partial void OnNotifyWhenDoneChanged(bool value) => SaveUnlessLoading();

    partial void OnIncludeSubfoldersChanged(bool value) => SaveUnlessLoading();

    partial void OnKeepFolderLayoutChanged(bool value) => SaveUnlessLoading();

    partial void OnRememberListChanged(bool value)
    {
        SaveUnlessLoading();
        if (!initializing)
        {
            SaveQueueNow();
        }
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (initializing)
        {
            return;
        }

        SaveSettings();
        if (value)
        {
            _ = CheckForUpdateAsync();
        }
        else
        {
            DismissUpdateCommand.Execute(null);
        }
    }

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

        // Only the cards on screen get a new picture. The others are made when they scroll into view.
        foreach (var item in Files)
        {
            item.Thumbnail = null;
            item.PreviewRequested = false;
        }

        foreach (var item in shownCards.ToList())
        {
            EnsurePreview(item);
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
        suppressRefresh = true;
        var (added, skipped) = AddToList(paths.Select(path => (path, -1L, (string?)null)));
        suppressRefresh = false;
        Refresh();
        ReportAdded(added, skipped);
        SaveQueueSoon();
        return (added, skipped);
    }

    /// <summary>Puts files in the list. A size of -1 means "look it up". Does not refresh the window.</summary>
    private (int Added, int Skipped) AddToList(IEnumerable<(string Path, long Size, string? Root)> files)
    {
        var added = 0;
        var skipped = 0;
        foreach (var (path, knownSize, root) in files)
        {
            if (knownPaths.Contains(path))
            {
                continue;
            }

            if (MediaGroups.Of(path) is not { } group)
            {
                if (File.Exists(path))
                {
                    skipped++;
                }

                continue;
            }

            var size = knownSize;
            if (size < 0)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    size = new FileInfo(path).Length;
                }
                catch (IOException)
                {
                    continue;
                }
            }

            var item = new FileItem(path, group, size, addedCounter++) { InList = true, RootFolder = root };
            knownPaths.Add(path);
            Files.Add(item);
            added++;
        }

        return (added, skipped);
    }

    private void ReportAdded(int added, int skipped)
    {
        Notice = (added, skipped) switch
        {
            (0, 0) => "No new supported files.",
            (_, 0) => null,
            _ => $"Skipped {skipped} file{(skipped == 1 ? string.Empty : "s")} Mercury can't read yet.",
        };
        OnPropertyChanged(nameof(Notice));
    }

    /// <summary>True while a big folder is being read and added, so a run cannot start with half a list.</summary>
    public bool IsAdding { get; private set; }

    /// <summary>A short message about the last add, for the status line. Null when there is nothing to say.</summary>
    public string? Notice { get; private set; }

    /// <summary>How many files go into the list at a time, so a huge folder never makes the window wait.</summary>
    private const int AddChunkSize = 500;

    /// <summary>
    /// Adds the supported files in a folder, and in the folders inside it when that setting is on. The folder is read in
    /// the background and the files are added in small steps, so a folder of tens of thousands of photos keeps the window alive.
    /// </summary>
    public async Task AddFolderAsync(string folder)
    {
        if (IsAdding)
        {
            return;
        }

        IsAdding = true;
        ConvertCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        try
        {
            SetNotice("Reading the folder…");
            var recurse = IncludeSubfolders;
            var found = await Task.Run(() => ReadFolder(folder, recurse));
            if (found is null)
            {
                SetNotice("That folder could not be read.");
                return;
            }

            var root = Path.GetFullPath(folder);
            var (added, skipped) = await AddInChunksAsync(found.Select(f => (f.Path, f.Size, (string?)root)).ToList());
            ReportAdded(added, skipped);
            SaveQueueSoon();
        }
        finally
        {
            IsAdding = false;
            ConvertCommand.NotifyCanExecuteChanged();
            ClearListCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Adds files picked or dropped by the user. A long list goes in small steps so the window stays alive.</summary>
    public async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count <= AddChunkSize)
        {
            AddFiles(paths);
            return;
        }

        if (IsAdding)
        {
            return;
        }

        IsAdding = true;
        ConvertCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        try
        {
            var (added, skipped) = await AddInChunksAsync(paths.Select(path => (path, -1L, (string?)null)).ToList());
            ReportAdded(added, skipped);
            SaveQueueSoon();
        }
        finally
        {
            IsAdding = false;
            ConvertCommand.NotifyCanExecuteChanged();
            ClearListCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Adds a long list in small steps, letting the window draw and answer between them.</summary>
    private async Task<(int Added, int Skipped)> AddInChunksAsync(List<(string Path, long Size, string? Root)> items)
    {
        int added = 0, skipped = 0;
        suppressRefresh = true;
        try
        {
            for (var start = 0; start < items.Count; start += AddChunkSize)
            {
                var (a, s) = AddToList(items.Skip(start).Take(AddChunkSize));
                added += a;
                skipped += s;
                if (items.Count > AddChunkSize)
                {
                    pendingNotice = string.Create(CultureInfo.InvariantCulture, $"Adding files… {Math.Min(start + AddChunkSize, items.Count):N0} of {items.Count:N0}");
                    suppressRefresh = false;
                    Refresh();
                    suppressRefresh = true;
                    await Task.Delay(1);
                }
            }
        }
        finally
        {
            suppressRefresh = false;
        }

        Refresh();
        return (added, skipped);
    }

    /// <summary>
    /// Brings back the files that were not converted yet when the app was last closed. Does nothing when the user has
    /// already added files, or when the setting is off.
    /// </summary>
    public async Task RestoreListAsync()
    {
        if (!RememberList || IsAdding || Files.Count > 0)
        {
            return;
        }

        IsAdding = true;
        ConvertCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        try
        {
            // Reading the list and checking each file still exists happens in the background.
            var saved = await Task.Run(() => queueStore.Load()
                .Select(file => (File: file, Info: new FileInfo(file.Path)))
                .Where(x => x.Info.Exists)
                .Select(x => (x.File.Path, x.Info.Length, x.File.Root))
                .ToList());

            if (saved.Count == 0 || Files.Count > 0)
            {
                return;
            }

            var (added, _) = await AddInChunksAsync(saved);
            SetNotice(added == 0 ? "Nothing left from last time." : $"Brought back {added:N0} file{(added == 1 ? string.Empty : "s")} from last time.");
        }
        finally
        {
            IsAdding = false;
            ConvertCommand.NotifyCanExecuteChanged();
            ClearListCommand.NotifyCanExecuteChanged();
        }
    }

    private bool queueSavePending;

    /// <summary>Saves the list shortly from now, once, however many changes come in between.</summary>
    private void SaveQueueSoon()
    {
        if (queueSavePending || initializing)
        {
            return;
        }

        queueSavePending = true;
        _ = SaveQueueLaterAsync();
    }

    private async Task SaveQueueLaterAsync()
    {
        await Task.Delay(IsBusy ? 8000 : 1500);
        queueSavePending = false;
        SaveQueueNow();
    }

    /// <summary>Writes down the files that are not converted yet. Called when the list changes and when the window closes.</summary>
    public void SaveQueueNow(bool wait = false)
    {
        var remaining = RememberList
            ? Files.Where(f => f.State != ItemState.Done).Select(f => new SavedFile(f.Path, f.RootFolder)).ToList()
            : [];

        if (wait)
        {
            queueStore.Save(remaining);
        }
        else
        {
            _ = Task.Run(() => queueStore.Save(remaining));
        }
    }

    /// <summary>Lists the files in a folder with their sizes. Runs in the background. Null when the folder cannot be read.</summary>
    private static List<(string Path, long Size)>? ReadFolder(string folder, bool recurse)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recurse,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 32,
            };

            return new DirectoryInfo(folder)
                .EnumerateFiles("*", options)
                .Where(file => MediaGroups.Of(file.FullName) is not null)
                .Select(file => (file.FullName, file.Length))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
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
            item.InList = false;
            knownPaths.Remove(item.Path);
        }

        // One pass from the end, so removing thousands of files at once stays fast.
        for (var i = Files.Count - 1; i >= 0; i--)
        {
            if (!Files[i].InList)
            {
                Files.RemoveAt(i);
            }
        }

        suppressRefresh = false;
        Refresh();
        SaveQueueSoon();
    }

    [RelayCommand(CanExecute = nameof(CanClearList))]
    private void ClearList()
    {
        foreach (var item in Files)
        {
            item.InList = false;
        }

        Files.Clear();
        knownPaths.Clear();
        filterKey = "all";
        BannerDismissed = false;
        Notice = null;
        Refresh();
        SaveQueueSoon();
    }

    private bool CanClearList() => !IsBusy && !IsAdding && Files.Count > 0;

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

    /// <summary>
    /// A card came into view. Its preview and length are made now rather than when the file was added, so a list of tens
    /// of thousands of files does not start tens of thousands of jobs, only the few cards that can be seen.
    /// </summary>
    public void CardShown(FileItem item)
    {
        shownCards.Add(item);
        if (item.ThemeVersion != themeVersion)
        {
            item.ThemeVersion = themeVersion;
            item.RefreshTheme();
        }

        EnsurePreview(item);
    }

    private int themeVersion;

    /// <summary>A card scrolled out of view. Its picture is let go, so memory stays small however long the list is.</summary>
    public void CardHidden(FileItem item)
    {
        shownCards.Remove(item);
        item.PreviewRequested = false;
        item.Thumbnail = null;
    }

    private void EnsurePreview(FileItem item)
    {
        if (!item.PreviewRequested && View.ThumbnailPixels > 0)
        {
            item.PreviewRequested = true;
            _ = LoadThumbnailAsync(item);
        }

        if (!item.DurationRequested)
        {
            item.DurationRequested = true;
            _ = LoadDurationAsync(item);
        }
    }

    private async Task LoadThumbnailAsync(FileItem item)
    {
        // The picture size follows the View setting. With previews off there is nothing to load.
        var pixels = View.ThumbnailPixels;
        if (pixels <= 0)
        {
            return;
        }

        try
        {
            var picture = await thumbnails.GetAsync(item.Path, pixels);
            // If the size was changed or the card scrolled away while this picture was being made, drop it.
            if (picture is not null && item.InList && item.PreviewRequested && pixels == View.ThumbnailPixels)
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
            if (seconds is not null && item.InList)
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
                CheckForUpdates = CheckForUpdates,
                StripMetadata = StripMetadata,
                Theme = Theme,
                NotifyWhenDone = NotifyWhenDone,
                IncludeSubfolders = IncludeSubfolders,
                KeepFolderLayout = KeepFolderLayout,
                RememberList = RememberList,
                UpdatePromptAnswered = UpdateQuestionAnswered,
                SkippedVersion = skippedVersion,
                LastUpdateCheckUtc = lastUpdateCheck,
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

        // Files that came from a folder keep their place in it when everything goes to one chosen folder.
        if (OutputFolder is not null && KeepFolderLayout && item.RootFolder is { } root
            && Path.GetDirectoryName(item.Path) is { } source
            && Path.GetRelativePath(root, source) is { } relative
            && relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
        {
            directory = Path.Combine(OutputFolder, relative);
        }

        return Path.Combine(directory, OutputFileName(item));
    }

    /// <summary>The size, rotation, and flips that apply to a kind of file.</summary>
    internal Transform ChangesFor(MediaGroup group)
    {
        if (MediaGroups.Of(targets[group]) == MediaGroup.Audio)
        {
            return Transform.None;
        }

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
