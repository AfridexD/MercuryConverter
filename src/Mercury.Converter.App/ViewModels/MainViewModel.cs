using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mercury.Converter.Core;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Transform = Mercury.Converter.Core.Transform;

namespace Mercury.Converter.App.ViewModels;

/// <summary>Where a file is in its conversion.</summary>
public enum ItemState
{
    Waiting,
    Converting,
    Done,
    Failed,
    Cancelled,
}

/// <summary>Sizes for the file list. Shared by every item so changing the thumbnail size updates all of them at once.</summary>
public partial class ViewOptions : ObservableObject
{
    [ObservableProperty]
    public partial bool ShowThumbnails { get; set; } = true;

    [ObservableProperty]
    public partial double CardWidth { get; set; } = 184;

    [ObservableProperty]
    public partial double ThumbnailHeight { get; set; } = 120;

    public void Apply(ThumbnailSize size)
    {
        ShowThumbnails = size != ThumbnailSize.Off;
        (CardWidth, ThumbnailHeight) = size switch
        {
            ThumbnailSize.Small => (124d, 84d),
            ThumbnailSize.Large => (284d, 190d),
            ThumbnailSize.Off => (560d, 0d),
            _ => (184d, 120d),
        };
    }
}

/// <summary>One file in the list: its preview, its state, and its progress bar.</summary>
public partial class FileItem : ObservableObject
{
    public FileItem(string path, MediaKind kind, ViewOptions options)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Kind = kind;
        Options = options;
    }

    public string Path { get; }

    public string Name { get; }

    public MediaKind Kind { get; }

    public ViewOptions Options { get; }

    /// <summary>A Segoe Fluent Icons symbol shown while there is no preview, or for audio files.</summary>
    public string KindGlyph => Kind switch
    {
        MediaKind.Video => "",
        MediaKind.Audio => "",
        _ => "",
    };

    [ObservableProperty]
    public partial ItemState State { get; set; } = ItemState.Waiting;

    /// <summary>0 to 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    /// <summary>Shows the saved location, a warning, or the error message when the user points at the file.</summary>
    [ObservableProperty]
    public partial string? Tooltip { get; set; }

    /// <summary>A short result such as "4.2 MB → 1.1 MB".</summary>
    [ObservableProperty]
    public partial string? Detail { get; set; }

    /// <summary>The waiting animation: moving bar before the conversion starts.</summary>
    public bool IsWaiting => State == ItemState.Waiting;

    public bool IsFailed => State == ItemState.Failed;

    public bool HasThumbnail => Thumbnail is not null;

    public bool ShowGlyph => Thumbnail is null;

    public string StatusText => State switch
    {
        ItemState.Converting => string.Create(CultureInfo.InvariantCulture, $"Converting {Progress:0}%"),
        ItemState.Done => "Done",
        ItemState.Failed => "Failed",
        ItemState.Cancelled => "Cancelled",
        _ => "Waiting",
    };

    partial void OnStateChanged(ItemState value)
    {
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(StatusText));

    partial void OnDetailChanged(string? value) => OnPropertyChanged(nameof(StatusText));

    partial void OnThumbnailChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(ShowGlyph));
    }

    public void Reset()
    {
        State = ItemState.Waiting;
        Progress = 0;
        Tooltip = null;
        Detail = null;
    }
}

/// <summary>A choice in the Speed list.</summary>
public sealed record ModeOption(string Label, string Description, PerformanceMode Mode);

/// <summary>A size choice in the Size list. Changes is what the converter applies.</summary>
public sealed record SizeOption(string Label, Transform Changes);

/// <summary>A rotation choice in the Rotate list.</summary>
public sealed record RotationOption(string Label, int Degrees);

public partial class MainViewModel : ObservableObject
{
    private readonly ToolLocator tools = ToolLocator.Default;
    private readonly ConversionRunner runner;
    private readonly ThumbnailService thumbnails;
    private readonly IResourceSampler sampler = new SystemResourceSampler();
    private readonly AppSettingsStore settingsStore = AppSettingsStore.Default;
    private readonly PresetStore presetStore = PresetStore.Default;
    private CancellationTokenSource? cancellation;
    private HardwareEncoder detectedEncoder = HardwareEncoder.None;

    private sealed record Job(FileItem Item, int Index);

    public MainViewModel()
    {
        runner = new ConversionRunner(tools);
        thumbnails = ThumbnailService.CreateDefault(tools);

        SelectedSize = SizeOptions[0];
        SelectedRotation = RotationOptions[0];
        var settings = settingsStore.Load();

        SelectedTarget = Targets.Get(settings.LastTarget);
        Quality = settings.LastQuality;
        KeepSmallerThanOriginal = settings.KeepSmallerThanOriginal;
        LosslessJpeg = settings.LosslessJpegRecompression;
        OutputFolder = settings.OutputFolder;
        ShowTaskbarProgress = settings.ShowTaskbarProgress;
        UseGraphicsCardEncoder = settings.UseGraphicsCardEncoder;
        SelectedMode = ModeOptions.First(mode => mode.Mode == settings.Performance);
        ThumbnailSetting = settings.Thumbnails;
        View.Apply(ThumbnailSetting);

        Presets = new ObservableCollection<Preset>(presetStore.Load());
        SelectedPreset = Presets.FirstOrDefault(p => p.Id == settings.LastPresetId);

        Files.CollectionChanged += OnFilesChanged;

        // Housekeeping and hardware detection happen in the background so the window opens straight away.
        _ = Task.Run(() => thumbnails.Prune(200L * 1024 * 1024));
        _ = DetectGraphicsEncoderAsync();
    }

    public ObservableCollection<FileItem> Files { get; } = [];

    public ViewOptions View { get; } = new();

    public IReadOnlyList<TargetInfo> AvailableTargets { get; } = Targets.All;

    public IReadOnlyList<ModeOption> ModeOptions { get; } =
    [
        new("Smart (recommended)", "Uses more of your PC when it is idle, and less when you are busy.", PerformanceMode.Smart),
        new("Gentle", "Uses about half of what is free, at low priority. Good while you work or play.", PerformanceMode.Gentle),
        new("Maximum speed", "Uses every processor core.", PerformanceMode.Maximum),
    ];

    public ObservableCollection<SizeOption> SizeOptions { get; } =
    [
        new("Original size", Transform.None),
        new("Half size (50%)", Transform.Percent(50)),
        new("Fit within 1920 px wide", new Transform(MaxWidth: 1920)),
        new("Fit within 1080p (1080 px tall)", Transform.FitHeight(1080)),
        new("Fit within 720p (720 px tall)", Transform.FitHeight(720)),
        new("Small (480 px wide, good for GIF)", new Transform(MaxWidth: 480)),
    ];

    public IReadOnlyList<RotationOption> RotationOptions { get; } =
    [
        new("No rotation", 0),
        new("Rotate right (90°)", 90),
        new("Upside down (180°)", 180),
        new("Rotate left (270°)", 270),
    ];

    public ObservableCollection<Preset> Presets { get; }

    [ObservableProperty]
    public partial TargetInfo SelectedTarget { get; set; } = Targets.Get(TargetFormat.Jpeg);

    /// <summary>Slider position, 1 (smallest file) to 100 (best quality).</summary>
    [ObservableProperty]
    public partial double Quality { get; set; } = QualityMapper.Balanced;

    /// <summary>Keeps JPEG pixels and allows an exact restore when converting JPEG to JPEG XL.</summary>
    [ObservableProperty]
    public partial bool LosslessJpeg { get; set; } = true;

    /// <summary>Retries at lower quality when a lossy result is larger than the original.</summary>
    [ObservableProperty]
    public partial bool KeepSmallerThanOriginal { get; set; } = true;

    [ObservableProperty]
    public partial SizeOption SelectedSize { get; set; }

    [ObservableProperty]
    public partial RotationOption SelectedRotation { get; set; }

    [ObservableProperty]
    public partial bool FlipHorizontal { get; set; }

    [ObservableProperty]
    public partial bool FlipVertical { get; set; }

    /// <summary>Advanced FFmpeg options for video. Only a safe allowlist is accepted.</summary>
    [ObservableProperty]
    public partial string? ExtraFfmpegArguments { get; set; }

    /// <summary>Where converted files are saved. Null means next to each original.</summary>
    [ObservableProperty]
    public partial string? OutputFolder { get; set; }

    [ObservableProperty]
    public partial Preset? SelectedPreset { get; set; }

    [ObservableProperty]
    public partial string PresetName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ModeOption SelectedMode { get; set; }

    /// <summary>Encode on the graphics card: much less processor use, but larger files.</summary>
    [ObservableProperty]
    public partial bool UseGraphicsCardEncoder { get; set; }

    [ObservableProperty]
    public partial bool GraphicsEncoderAvailable { get; set; }

    [ObservableProperty]
    public partial string GraphicsEncoderText { get; set; } = "Checking your graphics card…";

    [ObservableProperty]
    public partial ThumbnailSize ThumbnailSetting { get; set; } = ThumbnailSize.Medium;

    /// <summary>Overall progress across all files, 0 to 1. Drives the taskbar progress bar.</summary>
    [ObservableProperty]
    public partial double OverallProgress { get; set; }

    [ObservableProperty]
    public partial bool ShowTaskbarProgress { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "Drop files here, or click Add files.";

    public bool HasFiles => Files.Count > 0;

    public string QualityLabel => Quality switch
    {
        >= 90 => "Best quality, largest file",
        >= 60 => "Balanced",
        _ => "Smallest file, lower quality",
    };

    public bool ShowLosslessOption => SelectedTarget.Format == TargetFormat.JpegXl;

    public bool ShowVideoOptions => SelectedTarget.Kind == MediaKind.Video;

    /// <summary>The graphics-card option only applies to formats that use the H.264 encoder.</summary>
    public bool ShowGraphicsOption => SelectedTarget.Format is TargetFormat.Mp4 or TargetFormat.Mkv;

    public string OutputFolderText => OutputFolder ?? "Next to each original file";

    public string ModeDescription => SelectedMode.Description;

    /// <summary>The settings the user has chosen, as one transform.</summary>
    public Transform CurrentChanges => new(
        ScalePercent: SelectedSize.Changes.ScalePercent,
        MaxWidth: SelectedSize.Changes.MaxWidth,
        MaxHeight: SelectedSize.Changes.MaxHeight,
        Rotation: SelectedRotation.Degrees,
        FlipHorizontal: FlipHorizontal,
        FlipVertical: FlipVertical);

    partial void OnQualityChanged(double value) => OnPropertyChanged(nameof(QualityLabel));

    partial void OnSelectedTargetChanged(TargetInfo value)
    {
        OnPropertyChanged(nameof(ShowLosslessOption));
        OnPropertyChanged(nameof(ShowVideoOptions));
        OnPropertyChanged(nameof(ShowGraphicsOption));
    }

    partial void OnOutputFolderChanged(string? value) => OnPropertyChanged(nameof(OutputFolderText));

    partial void OnSelectedModeChanged(ModeOption value) => OnPropertyChanged(nameof(ModeDescription));

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFiles));
        ConvertCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Finds out which graphics-card encoder works here, so the option can be offered or explained.</summary>
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
            HardwareEncoder.Nvenc => "NVIDIA graphics card found.",
            HardwareEncoder.Amf => "AMD graphics card found.",
            HardwareEncoder.Qsv => "Intel graphics found.",
            _ => "No supported graphics card was found.",
        };
    }

    /// <summary>Starts from the right-click menu: picks the format, the quality if given, and adds the files.</summary>
    public void ApplyCommand(ConvertCommand command)
    {
        if (command.Target is { } target)
        {
            SelectedTarget = Targets.Get(target);
        }

        if (command.Quality is { } quality)
        {
            Quality = quality;
        }

        AddFiles(command.Files);
    }

    /// <summary>Adds supported files and skips the rest. Duplicates are ignored.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (!InputFormats.TryGetKind(path, out var kind))
            {
                skipped++;
                continue;
            }

            if (Files.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var item = new FileItem(path, kind, View);
            Files.Add(item);
            _ = LoadThumbnailAsync(item);
            added++;
        }

        Summary = skipped switch
        {
            0 when added == 0 => "No new supported files.",
            0 => $"Added {added} file(s). Pick a format, then press Convert.",
            _ => $"Added {added} file(s). Skipped {skipped} unsupported file(s).",
        };
    }

    /// <summary>Adds the supported files directly inside a folder.</summary>
    public void AddFolder(string folder)
    {
        try
        {
            AddFiles(Directory.EnumerateFiles(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Summary = $"That folder could not be read: {ex.Message}";
        }
    }

    /// <summary>Changes how large the previews are, or turns them off.</summary>
    public void SetThumbnailSize(ThumbnailSize size)
    {
        ThumbnailSetting = size;
        View.Apply(size);

        foreach (var item in Files)
        {
            item.Thumbnail = null;
            _ = LoadThumbnailAsync(item);
        }
    }

    private async Task LoadThumbnailAsync(FileItem item)
    {
        var pixels = ThumbnailSizes.Pixels(ThumbnailSetting);
        if (pixels == 0)
        {
            return;
        }

        try
        {
            var picture = await thumbnails.GetAsync(item.Path, pixels);

            // The user may have changed the size while the preview was being made.
            if (picture is not null && pixels == ThumbnailSizes.Pixels(ThumbnailSetting))
            {
                item.Thumbnail = new BitmapImage { DecodePixelWidth = pixels, UriSource = new Uri(picture) };
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            // A missing preview just means the file shows an icon instead.
        }
    }

    /// <summary>Sets every control to match a preset.</summary>
    public void ApplyPreset(Preset preset)
    {
        SelectedTarget = Targets.Get(preset.Target);
        Quality = preset.Quality;
        LosslessJpeg = preset.LosslessJpegRecompression;
        KeepSmallerThanOriginal = preset.KeepSmallerThanOriginal;
        ExtraFfmpegArguments = preset.ExtraFfmpegArguments;

        FlipHorizontal = preset.Changes.FlipHorizontal;
        FlipVertical = preset.Changes.FlipVertical;
        SelectedRotation = RotationOptions.First(r => r.Degrees == preset.Changes.Rotation);

        var sizeOnly = preset.Changes with { Rotation = 0, FlipHorizontal = false, FlipVertical = false };
        SelectedSize = SizeOptions.FirstOrDefault(s => s.Changes == sizeOnly) ?? AddSizeOption(preset.Name, sizeOnly);

        Summary = $"Loaded preset '{preset.Name}'.";
    }

    private SizeOption AddSizeOption(string label, Transform changes)
    {
        var option = new SizeOption($"{label} size", changes);
        SizeOptions.Add(option);
        return option;
    }

    /// <summary>Saves the current settings as a named preset.</summary>
    [RelayCommand]
    private void SavePreset()
    {
        var name = PresetName.Trim();
        var preset = new Preset
        {
            Name = name,
            Target = SelectedTarget.Format,
            Quality = (int)Math.Round(Quality),
            LosslessJpegRecompression = LosslessJpeg,
            KeepSmallerThanOriginal = KeepSmallerThanOriginal,
            Changes = CurrentChanges,
            ExtraFfmpegArguments = string.IsNullOrWhiteSpace(ExtraFfmpegArguments) ? null : ExtraFfmpegArguments.Trim(),
        };

        var problem = preset.Validate();
        if (problem is not null)
        {
            Summary = problem;
            return;
        }

        var user = Presets.Where(p => !p.IsBuiltIn).Where(p => p.Name != name).Append(preset).ToList();
        if (!TrySavePresets(user))
        {
            return;
        }

        ReplacePresets(user);
        SelectedPreset = preset;
        PresetName = string.Empty;
        Summary = $"Saved preset '{name}'.";
    }

    /// <summary>Imports presets from a shared file. Invalid presets are reported, not loaded.</summary>
    public void ImportPresets(string path)
    {
        var result = PresetStore.Import(path);
        var user = Presets.Where(p => !p.IsBuiltIn).ToList();
        user.AddRange(result.Accepted);

        if (!TrySavePresets(user))
        {
            return;
        }

        ReplacePresets(user);
        Summary = result.Rejected.Count == 0
            ? $"Imported {result.Accepted.Count} preset(s)."
            : $"Imported {result.Accepted.Count} preset(s). Skipped {result.Rejected.Count}: {string.Join(" ", result.Rejected)}";
    }

    /// <summary>Exports the user's presets to a shared file.</summary>
    public void ExportPresets(string path)
    {
        PresetStore.Export(Presets, path);
        Summary = $"Exported presets to {Path.GetFileName(path)}.";
    }

    private bool TrySavePresets(List<Preset> user)
    {
        try
        {
            presetStore.Save(user);
            return true;
        }
        catch (IOException ex)
        {
            Summary = $"The presets could not be saved: {ex.Message}";
            return false;
        }
    }

    private void ReplacePresets(List<Preset> user)
    {
        Presets.Clear();
        foreach (var item in PresetStore.BuiltIn.Concat(user))
        {
            Presets.Add(item);
        }
    }

    private bool CanConvert() => !IsBusy && Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        var extra = string.IsNullOrWhiteSpace(ExtraFfmpegArguments) ? null : ExtraFfmpegArguments.Trim();
        if (extra is not null && ShowVideoOptions)
        {
            try
            {
                FfmpegOptions.Parse(extra);
            }
            catch (ArgumentException ex)
            {
                Summary = ex.Message;
                return;
            }
        }
        else
        {
            extra = null;
        }

        SaveSettings();

        IsBusy = true;
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var target = SelectedTarget;
        var changes = CurrentChanges;
        var quality = (int)Math.Round(Quality);
        var lossless = LosslessJpeg;
        var keepSmaller = KeepSmallerThanOriginal;
        var gpuEncoder = UseGraphicsCardEncoder && target.Format is TargetFormat.Mp4 or TargetFormat.Mkv
            ? detectedEncoder
            : HardwareEncoder.None;

        var pending = Files.Where(f => f.State is ItemState.Waiting or ItemState.Cancelled or ItemState.Failed).ToList();
        foreach (var item in pending)
        {
            item.Reset();
        }

        Summary = pending.Count == 1 ? "Converting 1 file…" : $"Converting {pending.Count} files…";

        var fractions = new double[pending.Count];
        var done = 0;
        var failed = 0;

        // Progress reporters are made here, on the window's thread, so their updates reach the screen safely.
        var reporters = pending
            .Select((item, index) => new Progress<double>(value =>
            {
                item.Progress = value * 100;
                fractions[index] = value;
                OverallProgress = fractions.Sum() / fractions.Length;
            }))
            .ToList();

        var work = pending
            .Select((item, index) => (new Job(item, index), item.Kind))
            .ToList();

        var scheduler = new ConversionScheduler(sampler, SelectedMode.Mode);

        try
        {
            await scheduler.RunAsync(work, async (job, budget, jobToken) =>
            {
                var item = job.Item;
                item.State = ItemState.Converting;

                var duration = await MediaProbe.GetDurationSecondsAsync(item.Path, tools, jobToken);

                var execution = new ExecutionOptions(
                    Threads: budget.ThreadsPerJob,
                    UseHardwareDecode: true,
                    Encoder: gpuEncoder,
                    Priority: budget.Priority);

                var request = new ConversionRequest(item.Path, target.Format, quality, lossless, changes, extra, execution);

                var result = await runner.RunAsync(
                    request,
                    OutputPathFor(item.Path, target),
                    duration,
                    reporters[job.Index],
                    keepSmaller,
                    jobToken);

                if (result.Succeeded)
                {
                    done++;
                    item.Progress = 100;
                    fractions[job.Index] = 1;
                    item.Detail = DescribeResult(result);
                    item.Tooltip = result.Warning
                        ?? (result.AdjustedQuality is { } adjusted
                            ? $"Quality was lowered to {adjusted} so the file stays smaller than the original. Saved to {result.OutputPath}"
                            : $"Saved to {result.OutputPath}");
                    item.State = ItemState.Done;
                }
                else if (result.WasCancelled)
                {
                    item.State = ItemState.Cancelled;
                }
                else
                {
                    failed++;
                    item.Tooltip = result.Error;
                    item.Detail = ShortReason(result.Error);
                    item.State = ItemState.Failed;
                }
            }, token);
        }
        catch (AggregateException ex)
        {
            failed += ex.InnerExceptions.Count;
        }
        finally
        {
            // Jobs that never started because of Cancel stay in the list, marked as cancelled.
            foreach (var item in pending.Where(i => i.State is ItemState.Waiting or ItemState.Converting))
            {
                item.State = ItemState.Cancelled;
            }

            IsBusy = false;
            cancellation.Dispose();
            cancellation = null;
            OverallProgress = 0;
        }

        Summary = failed == 0
            ? $"Finished. {done} file(s) converted."
            : $"Finished. {done} converted, {failed} failed. Point at a failed file for details.";
    }

    /// <summary>The first sentence of an error, short enough to read on a card. The full text stays in the tooltip.</summary>
    private static string? ShortReason(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var text = error.Trim();
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        if (end > 0)
        {
            text = text[..(end + 1)];
        }

        return text.Length > 90 ? text[..87] + "…" : text;
    }

    private static string DescribeResult(ConversionResult result)
    {
        var change = result.InputBytes > 0
            ? (int)Math.Round((result.OutputBytes - result.InputBytes) * 100.0 / result.InputBytes)
            : 0;
        var sign = change > 0 ? "+" : string.Empty;
        return $"{FormatBytes(result.InputBytes)} → {FormatBytes(result.OutputBytes)} ({sign}{change}%)";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => cancellation?.Cancel();

    private bool CanClear() => !IsBusy && Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Files.Clear();
        Summary = "Drop files here, or click Add files.";
    }

    /// <summary>Saves the remembered choices. Called before each conversion and when the window closes.</summary>
    public void SaveSettings()
    {
        try
        {
            settingsStore.Save(new AppSettings
            {
                OutputFolder = OutputFolder,
                LastTarget = SelectedTarget.Format,
                LastQuality = (int)Math.Round(Quality),
                KeepSmallerThanOriginal = KeepSmallerThanOriginal,
                LosslessJpegRecompression = LosslessJpeg,
                LastPresetId = SelectedPreset?.Id,
                ShowTaskbarProgress = ShowTaskbarProgress,
                Performance = SelectedMode.Mode,
                Thumbnails = ThumbnailSetting,
                UseGraphicsCardEncoder = UseGraphicsCardEncoder,
            });
        }
        catch (IOException)
        {
            // Settings are a convenience. A failure here must never block a conversion.
        }
    }

    /// <summary>Saves next to the original, or in the chosen folder. Uses a "-converted" suffix when the extension would not change.</summary>
    private string OutputPathFor(string inputPath, TargetInfo target)
    {
        var directory = OutputFolder ?? Path.GetDirectoryName(inputPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var sameExtension = string.Equals(
            Path.GetExtension(inputPath).TrimStart('.'),
            target.Extension,
            StringComparison.OrdinalIgnoreCase);

        var outputName = sameExtension ? $"{name}-converted" : name;
        return Path.Combine(directory, $"{outputName}.{target.Extension}");
    }
}
