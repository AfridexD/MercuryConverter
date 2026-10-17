using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Mercury.Converter.Core;
using Microsoft.UI.Xaml.Media;

namespace Mercury.Converter.App.ViewModels;

/// <summary>Works out the text, lists, and button states the window shows, from the files and settings.</summary>
public sealed partial class MainViewModel
{
    private static readonly MediaGroup[] GroupOrder = [MediaGroup.Photo, MediaGroup.Video, MediaGroup.Gif, MediaGroup.Audio];

    private string? pendingNotice;

    // ----- Queue header -----------------------------------------------------------------------------------------

    public bool HasFiles { get; private set; }

    public bool IsEmpty { get; private set; } = true;

    public string QueueTitle { get; private set; } = string.Empty;

    public string QueueSub { get; private set; } = string.Empty;

    /// <summary>0 to 100.</summary>
    public double OverallPercent { get; private set; }

    public ObservableCollection<PillItem> Pills { get; } = [];

    // ----- Banner shown after a run -----------------------------------------------------------------------------

    public bool BannerDismissed { get; private set; }

    public bool ShowBanner { get; private set; }

    public string BannerTitle { get; private set; } = string.Empty;

    public string BannerBody { get; private set; } = string.Empty;

    public string BannerGlyph { get; private set; } = Glyphs.CheckCircle;

    public Brush BannerBackground { get; private set; } = Tokens.Brush("McOkSoft");

    public Brush BannerGlyphBrush { get; private set; } = Tokens.Brush("McOk");

    public bool BannerCanRetry { get; private set; }

    public string BannerRetryLabel { get; private set; } = "Retry failed";

    // ----- Inspector --------------------------------------------------------------------------------------------

    public string InspectorTitle { get; private set; } = "All files";

    public string InspectorSub { get; private set; } = string.Empty;

    public bool HasSelection { get; private set; }

    public ObservableCollection<KindRow> KindRows { get; } = [];

    public ObservableCollection<ChipItem> Chips { get; } = [];

    public string QualityLabel { get; private set; } = string.Empty;

    public string QualityAutomationText { get; private set; } = string.Empty;

    /// <summary>True when at least one chosen format loses quality, so the slider means something.</summary>
    public bool ShowQuality { get; private set; }

    public bool ShowLosslessNote { get; private set; }

    public string LosslessNote { get; private set; } = string.Empty;

    public string EstimateText { get; private set; } = string.Empty;

    public string SavingText { get; private set; } = string.Empty;

    public string MoreSummary { get; private set; } = string.Empty;

    public string MoreGlyph => MoreOptionsOpen ? Glyphs.ChevronUp : Glyphs.ChevronDown;

    public bool HasMoreOptions => MoreSummary.Length > 0;

    public bool ShowSizeOption { get; private set; }

    public bool ShowRotateOption { get; private set; }

    public bool ShowSpeedOption { get; private set; }

    public bool ShowChannelsOption { get; private set; }

    public bool ShowStripOption { get; private set; }

    public string RotateNote { get; private set; } = string.Empty;

    public string OutputFolderText => OutputFolder ?? "Next to the originals";

    public bool OutputFolderIsCustom => OutputFolder is not null;

    public string NamingText { get; private set; } = string.Empty;

    // ----- Action bar -------------------------------------------------------------------------------------------

    public string SummaryText { get; private set; } = "Drop files here, or press Ctrl+O to add files.";

    public string ConvertLabel { get; private set; } = "Convert";

    public bool ConvertIsEnabled { get; private set; }

    public bool ShowCancel { get; private set; }

    public bool ShowClearList { get; private set; }

    // ----- Refreshing -------------------------------------------------------------------------------------------

    /// <summary>
    /// Recalculates everything the window displays. Call it after any change to the files or settings.
    /// Lists are only touched when their contents change, so scrolling and focus are not disturbed.
    /// </summary>
    public void Refresh()
    {
        if (suppressRefresh || initializing)
        {
            return;
        }

        var files = Files.ToList();
        var selected = files.Where(f => f.IsSelected).ToList();
        var scope = selected.Count > 0 ? selected : files;
        var kinds = GroupOrder.Where(g => scope.Any(f => f.Group == g)).ToList();
        int CountIn(MediaGroup g) => scope.Count(f => f.Group == g);
        FormatChoice ChoiceFor(MediaGroup g) => FormatCatalog.Get(g, targets[g]);
        var quality = (int)Math.Round(Quality);

        var ready = files.Count(f => f.State == ItemState.Ready);
        var waiting = files.Count(f => f.State == ItemState.Waiting);
        var active = files.Count(f => f.State == ItemState.Active);
        var done = files.Count(f => f.State == ItemState.Done);
        var failed = files.Count(f => f.State == ItemState.Failed);
        var cancelled = files.Count(f => f.State == ItemState.Cancelled);
        var running = IsBusy;
        var hasResults = !running && done + failed + cancelled > 0;

        foreach (var item in files.Where(f => f.State == ItemState.Ready))
        {
            item.PlannedText = $"{SizeText.Of(item.Size)} → {ChoiceFor(item.Group).Name}";
        }

        // Only the next few queued files animate.
        var animated = 0;
        foreach (var item in files)
        {
            item.AnimateWait = item.State == ItemState.Waiting && animated++ < 4;
        }

        HasFiles = files.Count > 0;
        IsEmpty = !HasFiles;
        HasSelection = selected.Count > 0;

        BuildQueueHeader(files, running, hasResults, done, failed, active, runTotal: RunTotal, quality);
        BuildPills(files, running, hasResults, waiting, active, done, failed, cancelled);
        BuildBanner(hasResults, done, failed, cancelled);
        BuildVisibleFiles(files);

        BuildInspector(scope, selected, kinds, CountIn, ChoiceFor, quality);
        BuildActionBar(files, selected, kinds, ChoiceFor, running, hasResults, ready, waiting, active, done, failed, cancelled);

        ConvertCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(string.Empty);
    }

    private void BuildQueueHeader(List<FileItem> files, bool running, bool hasResults, int done, int failed, int active, int runTotal, int quality)
    {
        if (running)
        {
            var finished = RunItems.Count(item => item.State is ItemState.Done or ItemState.Failed or ItemState.Cancelled);
            var activeProgress = RunItems.Where(item => item.State == ItemState.Active).Sum(item => item.Progress);
            var fraction = runTotal == 0 ? 0 : (finished + activeProgress / 100.0) / runTotal;

            QueueTitle = $"{finished} of {runTotal} done";
            QueueSub = EstimateTimeLeft(fraction);
            OverallPercent = Math.Clamp(fraction * 100, 0, 100);
            OverallProgress = Math.Clamp(fraction, 0, 1);
            return;
        }

        OverallPercent = 0;
        OverallProgress = 0;

        if (hasResults)
        {
            var finishedFiles = files.Where(f => f.State == ItemState.Done).ToList();
            QueueTitle = "Finished";
            QueueSub = $"{done} converted · {SizeText.Of(finishedFiles.Sum(f => f.Size))} → {SizeText.Of(finishedFiles.Sum(f => f.OutputBytes))}";
            return;
        }

        var inBytes = files.Sum(f => f.Size);
        var outBytes = files.Sum(f => SizeEstimator.Estimate(f.Size, f.Group, targets[f.Group], quality));
        QueueTitle = Plural(files.Count, "file", "files");
        QueueSub = files.Count == 0 ? string.Empty : $"{SizeText.Of(inBytes)} → about {SizeText.Of(outBytes)}";
    }

    private void BuildPills(List<FileItem> files, bool running, bool hasResults, int waiting, int active, int done, int failed, int cancelled)
    {
        var desired = new List<PillItem>();
        PillItem Pill(string key, string label, int count, string? glyph, string brushKey) =>
            new(key, label, count, glyph, brushKey, filterKey == key);

        if (running || hasResults)
        {
            desired.Add(Pill("all", "All", files.Count, null, "McText2"));
            if (active > 0) desired.Add(Pill("active", "Converting", active, Glyphs.Sync, "McAccentText"));
            if (waiting > 0) desired.Add(Pill("waiting", "Waiting", waiting, Glyphs.Schedule, "McText2"));
            if (done > 0) desired.Add(Pill("done", "Done", done, Glyphs.CheckCircle, "McOk"));
            if (failed > 0) desired.Add(Pill("failed", "Failed", failed, Glyphs.Error, "McErr"));
            if (cancelled > 0) desired.Add(Pill("cancelled", "Cancelled", cancelled, Glyphs.Cancelled, "McText2"));
        }
        else
        {
            desired.Add(Pill("all", "All", files.Count, null, "McText2"));
            foreach (var group in GroupOrder)
            {
                var count = files.Count(f => f.Group == group);
                if (count > 0 && count < files.Count)
                {
                    var glyph = group switch { MediaGroup.Video => Glyphs.Video, MediaGroup.Audio => Glyphs.Audio, _ => Glyphs.Photo };
                    desired.Add(Pill(group.ToString().ToLowerInvariant(), MediaGroups.Label(group), count, glyph, "McText2"));
                }
            }
        }

        // A filter with nothing left in it falls back to "All".
        if (desired.All(p => p.Key != filterKey))
        {
            filterKey = "all";
            desired = desired.Select(p => new PillItem(p.Key, p.Label, p.Count, p.Glyph, p.Key switch
            {
                "active" => "McAccentText", "done" => "McOk", "failed" => "McErr", _ => "McText2",
            }, p.Key == "all")).ToList();
        }

        Sync(Pills, desired, p => $"{p.Key}|{p.Label}|{p.Count}|{p.IsSelected}");
    }

    private void BuildBanner(bool hasResults, int done, int failed, int cancelled)
    {
        ShowBanner = hasResults && !BannerDismissed;
        if (!ShowBanner)
        {
            return;
        }

        var bad = failed > 0;
        BannerGlyph = bad ? Glyphs.Error : Glyphs.CheckCircle;
        BannerBackground = Tokens.Brush(bad ? "McErrSoft" : "McOkSoft");
        BannerGlyphBrush = Tokens.Brush(bad ? "McErr" : "McOk");
        var notSmaller = Files.Count(f => f.State == ItemState.Failed && f.NotSmaller);
        var realFailures = failed - notSmaller;

        BannerTitle = bad ? $"{done} converted, {failed} {(notSmaller == failed ? "not made smaller" : "failed")}" : $"{done} converted";
        BannerBody = notSmaller == failed && bad
            ? "These files are already compact, so nothing was saved. Use Save anyway on a card to keep a larger copy."
            : bad ? "Open the failed file to see why." : OutputFolder is null ? "Saved next to the originals." : $"Saved to {OutputFolder}.";
        BannerCanRetry = realFailures + cancelled > 0;
        BannerRetryLabel = realFailures > 0 && cancelled > 0 ? "Retry failed and cancelled" : realFailures > 0 ? "Retry failed" : "Retry cancelled";
    }

    private void BuildVisibleFiles(List<FileItem> files)
    {
        IEnumerable<FileItem> list = files;
        if (filterKey != "all")
        {
            list = filterKey switch
            {
                "active" => files.Where(f => f.State == ItemState.Active),
                "waiting" => files.Where(f => f.State == ItemState.Waiting),
                "done" => files.Where(f => f.State == ItemState.Done),
                "failed" => files.Where(f => f.State == ItemState.Failed),
                "cancelled" => files.Where(f => f.State == ItemState.Cancelled),
                _ => files.Where(f => f.Group.ToString().Equals(filterKey, StringComparison.OrdinalIgnoreCase)),
            };
        }

        list = Sort switch
        {
            SortMode.Name => list.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
            SortMode.Size => list.OrderByDescending(f => f.Size),
            SortMode.Type => list.OrderBy(f => f.Group).ThenBy(f => f.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
            _ => list.OrderBy(f => f.AddedIndex),
        };

        var desired = list.ToList();
        if (!VisibleFiles.SequenceEqual(desired))
        {
            VisibleFiles.Clear();
            foreach (var item in desired)
            {
                VisibleFiles.Add(item);
            }
        }
    }

    private void BuildInspector(
        List<FileItem> scope,
        List<FileItem> selected,
        List<MediaGroup> kinds,
        Func<MediaGroup, int> countIn,
        Func<MediaGroup, FormatChoice> choiceFor,
        int quality)
    {
        var kindsText = string.Join(", ", kinds.Select(k => MediaGroups.Count(k, countIn(k))));
        InspectorTitle = selected.Count > 0 ? $"{selected.Count} selected" : "All files";
        InspectorSub = selected.Count > 0 ? $"{kindsText}. Changes apply to the selected files only." : kindsText;

        Sync(KindRows, kinds.Select(k => new KindRow(k, countIn(k), choiceFor(k))).ToList(),
            row => $"{row.Group}|{row.Label}|{row.FormatName}");

        var chips = ChipPresets(kinds).Select(p => new ChipItem(p, p.Id == activePresetId)).ToList();
        Sync(Chips, chips, chip => $"{chip.Preset.Id}|{chip.IsSelected}");

        var anyLossy = kinds.Any(k => choiceFor(k).Lossy);
        ShowQuality = anyLossy;
        ShowLosslessNote = !anyLossy && kinds.Count > 0;

        var scopeIn = scope.Sum(f => f.Size);
        var scopeOut = scope.Sum(f => SizeEstimator.Estimate(f.Size, f.Group, targets[f.Group], quality));
        EstimateText = $"About {SizeText.Of(scopeOut)} in total";
        SavingText = scopeOut < scopeIn
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round((1 - scopeOut / (double)scopeIn) * 100):0}% smaller than now")
            : "about the same as now";
        LosslessNote = $"These formats keep full quality, so there is nothing to adjust. {EstimateText}.";

        var label = QualityWords(quality);
        QualityLabel = $"{label} · {quality}";
        QualityAutomationText = $"{quality} of 100, {label}";

        bool AudioOut(MediaGroup g) => g == MediaGroup.Audio || MediaGroups.Of(targets[g]) == MediaGroup.Audio;
        ShowSizeOption = kinds.Any(k => !AudioOut(k));
        ShowRotateOption = kinds.Any(k => k is MediaGroup.Photo or MediaGroup.Video && !AudioOut(k));
        ShowSpeedOption = kinds.Contains(MediaGroup.Video) && !AudioOut(MediaGroup.Video) || kinds.Contains(MediaGroup.Gif) && targets[MediaGroup.Gif] is TargetFormat.Mp4;
        ShowChannelsOption = kinds.Any(AudioOut);
        ShowStripOption = kinds.Any(k => k != MediaGroup.Gif);
        RotateNote = kinds.Count > 1 && kinds.Any(k => k is MediaGroup.Audio or MediaGroup.Gif)
            ? "Applies to photos and videos only."
            : "Turns or mirrors every file.";

        var parts = new List<string>();
        if (ShowSizeOption) parts.Add("Size");
        if (ShowRotateOption) parts.Add("rotate");
        if (ShowSpeedOption) parts.Add("speed");
        if (ShowChannelsOption) parts.Add("Channels");
        if (ShowStripOption) parts.Add("privacy");
        MoreSummary = parts.Count == 0 ? string.Empty : string.Join(", ", parts).Replace("Size, ", "Size, ", StringComparison.Ordinal);
        if (MoreSummary.Length > 0)
        {
            MoreSummary = char.ToUpperInvariant(MoreSummary[0]) + MoreSummary[1..];
        }

        NamingText = scope.Count == 0
            ? string.Empty
            : $"{scope[0].Name} becomes {OutputFileName(scope[0])}. Existing files are never replaced.";
    }

    private void BuildActionBar(
        List<FileItem> files,
        List<FileItem> selected,
        List<MediaGroup> kinds,
        Func<MediaGroup, FormatChoice> choiceFor,
        bool running,
        bool hasResults,
        int ready,
        int waiting,
        int active,
        int done,
        int failed,
        int cancelled)
    {
        ConvertIsEnabled = false;
        ConvertLabel = "Convert";

        if (running)
        {
            ConvertLabel = "Converting…";
        }
        else if (selected.Count > 0)
        {
            ConvertIsEnabled = true;
            ConvertLabel = $"Convert {selected.Count} selected";
        }
        else if (ready > 0)
        {
            ConvertIsEnabled = true;
            ConvertLabel = $"Convert {Plural(ready, "file", "files")}";
        }

        ShowCancel = running;
        ShowClearList = !running && files.Count > 0;

        var summary = "Drop files here, or press Ctrl+O to add files.";
        if (files.Count > 0 && !running && !hasResults)
        {
            summary = selected.Count > 0
                ? $"{selected.Count} of {files.Count} selected · Ctrl+A selects all"
                : "Ready · " + string.Join(", ", kinds.Select(k => $"{MediaGroups.Label(k)} to {choiceFor(k).Name}"));
        }

        if (running)
        {
            summary = $"Converting {active} at a time · {waiting} waiting";
        }

        if (hasResults)
        {
            summary = $"Finished. {done} converted{(failed > 0 ? $", {failed} failed" : string.Empty)}{(cancelled > 0 ? $", {cancelled} cancelled" : string.Empty)}.";
        }

        SummaryText = pendingNotice ?? summary;
        pendingNotice = null;
    }

    /// <summary>The presets shown as chips: the user's own first (up to two), then the built-in ones, four in all.</summary>
    private List<Preset> ChipPresets(List<MediaGroup> kinds)
    {
        var fitting = Presets.Where(p => kinds.Contains(p.Group)).ToList();
        var own = fitting.Where(p => !p.IsBuiltIn).Take(2);
        var builtIn = fitting.Where(p => p.IsBuiltIn);
        return own.Concat(builtIn).Take(4).ToList();
    }

    private static string QualityWords(int quality) => quality switch
    {
        <= 30 => "Smallest file",
        <= 55 => "Small file",
        <= 80 => "Balanced",
        <= 92 => "High quality",
        _ => "Best quality",
    };

    private static void Sync<T>(ObservableCollection<T> target, List<T> desired, Func<T, string> signature)
    {
        if (target.Count == desired.Count && target.Zip(desired).All(pair => signature(pair.First) == signature(pair.Second)))
        {
            return;
        }

        target.Clear();
        foreach (var item in desired)
        {
            target.Add(item);
        }
    }

    // ----- Filter, sort, and selection commands -----------------------------------------------------------------

    public void SetFilter(string key)
    {
        filterKey = key;
        Refresh();
    }

    [RelayCommand]
    private void JumpToActive()
    {
        filterKey = "active";
        Refresh();
    }

    [RelayCommand]
    private void DismissBanner()
    {
        BannerDismissed = true;
        Refresh();
    }

    public void SetNotice(string message)
    {
        pendingNotice = message;
        Refresh();
    }
}
