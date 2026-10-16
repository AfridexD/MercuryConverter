using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Mercury.Converter.Core;
using Transform = Mercury.Converter.Core.Transform;

namespace Mercury.Converter.App.ViewModels;

/// <summary>Starting, cancelling, and retrying conversions, and applying presets and format choices.</summary>
public sealed partial class MainViewModel
{
    private CancellationTokenSource? cancellation;
    private readonly Stopwatch runClock = new();

    /// <summary>The files in the current run, in order.</summary>
    internal List<FileItem> RunItems { get; private set; } = [];

    internal int RunTotal => RunItems.Count;

    // ----- Choosing formats and presets -------------------------------------------------------------------------

    public void SetTarget(MediaGroup group, TargetFormat format)
    {
        savedTargets = null;
        targets[group] = format;
        activePresetId = null;
        SaveSettings();
        Refresh();
    }

    public TargetFormat CurrentTarget(MediaGroup group) => targets[group];

    /// <summary>
    /// Applies what the right-click menu asked for: adds the files and uses its format and quality for this run only.
    /// The user's remembered choices are not changed.
    /// </summary>
    public void ApplyStartup(Mercury.Converter.Core.ConvertCommand command)
    {
        AddFiles(command.Files);
        if (command.Target is not { } target)
        {
            return;
        }

        savedTargets = new Dictionary<MediaGroup, TargetFormat>(targets);
        savedQuality = Quality;
        targets[MediaGroups.Of(target)] = target;
        if (command.Quality is { } quality)
        {
            initializing = true;
            Quality = Math.Clamp(Math.Round(quality / 5.0) * 5, 10, 100);
            initializing = false;
        }

        Refresh();
    }

    public void ApplyPreset(Preset preset)
    {
        targets[preset.Group] = preset.Target;
        Quality = Math.Clamp(Math.Round(preset.Quality / 5.0) * 5, 10, 100);
        NeverLarger = preset.KeepSmallerThanOriginal;
        SelectedSize = SizeChoices.All.FirstOrDefault(s => s.Changes == SizeChoices.SizeOnly(preset.Changes)) ?? SizeChoices.All[0];
        Rotation = preset.Changes.Rotation;
        FlipHorizontal = preset.Changes.FlipHorizontal;
        FlipVertical = preset.Changes.FlipVertical;
        activePresetId = preset.Id;
        SaveSettings();
        Refresh();
    }

    /// <summary>Saves the current choices for the first kind of file in view as a new preset of the user's own.</summary>
    public Preset? SaveCurrentAsPreset(string name)
    {
        var scope = SelectedItems.Count > 0 ? SelectedItems : Files;
        var group = GroupOrder.FirstOrDefault(g => scope.Any(f => f.Group == g));
        if (scope.Count == 0)
        {
            return null;
        }

        var preset = new Preset
        {
            Name = name.Trim(),
            Target = targets[group],
            Quality = (int)Math.Round(Quality),
            KeepSmallerThanOriginal = NeverLarger,
            Changes = ChangesFor(group),
        };

        if (preset.Validate() is not null)
        {
            return null;
        }

        Presets.Add(preset);
        PersistPresets();
        activePresetId = preset.Id;
        Refresh();
        return preset;
    }

    public void RemovePreset(Preset preset)
    {
        if (preset.IsBuiltIn)
        {
            return;
        }

        Presets.Remove(preset);
        PersistPresets();
        Refresh();
    }

    public void RenamePreset(Preset preset, string name)
    {
        var index = Presets.IndexOf(preset);
        if (preset.IsBuiltIn || index < 0 || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Presets[index] = preset with { Name = name.Trim() };
        PersistPresets();
        Refresh();
    }

    public string ImportPresets(string path)
    {
        var result = PresetStore.Import(path);
        foreach (var preset in result.Accepted)
        {
            Presets.Add(preset with { IsBuiltIn = false });
        }

        PersistPresets();
        Refresh();
        return result.Rejected.Count == 0
            ? $"Added {result.Accepted.Count} preset{(result.Accepted.Count == 1 ? string.Empty : "s")}."
            : $"Added {result.Accepted.Count}. {result.Rejected.Count} could not be used.";
    }

    public void ExportPresets(string path) => PresetStore.Export(Presets.Where(p => !p.IsBuiltIn), path);

    private void PersistPresets()
    {
        try
        {
            presetStore.Save(Presets.Where(p => !p.IsBuiltIn));
        }
        catch (IOException)
        {
            // Presets are a convenience. A failed save must never interrupt the user.
        }
    }

    public void RotateBy(int degrees) => Rotation = ((Rotation + degrees) % 360 + 360) % 360;

    [RelayCommand]
    private void ToggleMoreOptions() => MoreOptionsOpen = !MoreOptionsOpen;

    // ----- Running ----------------------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        var selected = SelectedItems.Where(f => f.State == ItemState.Ready).ToList();
        var batch = selected.Count > 0 ? selected : Files.Where(f => f.State == ItemState.Ready).ToList();
        if (batch.Count == 0)
        {
            return;
        }

        await RunBatchAsync(batch);
    }

    private bool CanConvert() => !IsBusy && Files.Any(f => f.State == ItemState.Ready);

    public async Task RunBatchAsync(List<FileItem> batch)
    {
        BannerDismissed = false;
        filterKey = "all";
        RunItems = batch;
        foreach (var item in batch)
        {
            item.Progress = 0;
            item.State = ItemState.Waiting;
        }

        cancellation = new CancellationTokenSource();
        runClock.Restart();
        IsBusy = true;
        Refresh();

        var scheduler = new ConversionScheduler(sampler, SelectedMode.Value);
        var jobs = batch.Select(item => (item, KindOf(item.Group))).ToList();
        try
        {
            await scheduler.RunAsync(jobs, (item, budget, token) => ConvertOneAsync(item, budget, token), cancellation.Token);
        }
        catch (AggregateException)
        {
            // Each failed file already shows its own message.
        }

        foreach (var item in batch.Where(i => i.State is ItemState.Waiting or ItemState.Active))
        {
            item.State = ItemState.Cancelled;
        }

        runClock.Stop();
        IsBusy = false;
        cancellation.Dispose();
        cancellation = null;
        OverallProgress = 0;
        Refresh();
    }

    private static MediaKind KindOf(MediaGroup group) => group switch
    {
        MediaGroup.Video => MediaKind.Video,
        MediaGroup.Audio => MediaKind.Audio,
        MediaGroup.Gif => MediaKind.Animation,
        _ => MediaKind.Image,
    };

    private async Task ConvertOneAsync(FileItem item, ResourceBudget budget, CancellationToken token)
    {
        item.State = ItemState.Active;
        item.Progress = 0;

        var target = targets[item.Group];
        var useGpu = UseGraphicsCardEncoder && detectedEncoder != HardwareEncoder.None;
        var request = new ConversionRequest(
            item.Path,
            target,
            (int)Math.Round(Quality),
            Changes: ChangesFor(item.Group),
            ExtraFfmpegArguments: ConversionOptions.FfmpegOptions(target, SelectedSpeed.Value, SelectedChannels.Value, useGpu),
            Execution: new ExecutionOptions(
                budget.ThreadsPerJob,
                UseHardwareDecode: true,
                Encoder: useGpu ? detectedEncoder : HardwareEncoder.None,
                Priority: budget.Priority));

        double? duration = null;
        if (item.Group is MediaGroup.Video or MediaGroup.Audio)
        {
            duration = await MediaProbe.GetDurationSecondsAsync(item.Path, tools);
        }

        var progress = new Progress<double>(value => item.Progress = Math.Round(value * 100));
        var result = await runner.RunAsync(request, OutputPathFor(item), duration, progress, NeverLarger, token);

        if (result.Succeeded)
        {
            item.OutputPath = result.OutputPath;
            item.OutputBytes = result.OutputBytes;
            item.Progress = 100;
            item.DoneDetail = DoneDetailFor(result);
            item.State = ItemState.Done;
        }
        else if (result.WasCancelled)
        {
            item.State = ItemState.Cancelled;
        }
        else
        {
            item.Message = FriendlyError(result.Error);
            item.Technical = result.Error;
            item.PartialSeconds = result.PartialSeconds;
            item.State = ItemState.Failed;
        }

        Refresh();
    }

    private static string DoneDetailFor(ConversionResult result)
    {
        var change = result.InputBytes == 0 ? 0 : (int)Math.Round((result.OutputBytes - result.InputBytes) * 100.0 / result.InputBytes);
        var sign = change > 0 ? "+" : change < 0 ? "−" : string.Empty;
        return $"{SizeText.Of(result.InputBytes)} → {SizeText.Of(result.OutputBytes)} ({sign}{Math.Abs(change)}%)";
    }

    private static string FriendlyError(string? error) =>
        string.IsNullOrWhiteSpace(error) ? "This file could not be converted." : "This file could not be converted. Open it to see why.";

    private string EstimateTimeLeft(double fraction)
    {
        var elapsed = runClock.Elapsed.TotalSeconds;
        if (fraction < 0.03 || elapsed < 4)
        {
            return "working out the time left…";
        }

        return SizeText.TimeLeft(elapsed / fraction - elapsed);
    }

    [RelayCommand]
    private void Cancel() => cancellation?.Cancel();

    // ----- Retrying ---------------------------------------------------------------------------------------------

    public async Task RetryAsync(IEnumerable<FileItem> items)
    {
        var again = items.Where(i => i.State is ItemState.Failed or ItemState.Cancelled or ItemState.Done).ToList();
        if (again.Count == 0 || IsBusy)
        {
            return;
        }

        foreach (var item in again)
        {
            item.ResetForRetry();
        }

        await RunBatchAsync(again);
    }

    [RelayCommand]
    private Task RetryProblems() =>
        RetryAsync(Files.Where(f => f.State is ItemState.Failed or ItemState.Cancelled));
}
