using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Mercury.Converter.Core;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Mercury.Converter.App.ViewModels;

/// <summary>Where a file is in its life: added, queued, running, or finished one way or another.</summary>
public enum ItemState
{
    Ready,
    Waiting,
    Active,
    Done,
    Failed,
    Cancelled,
}

public enum SortMode
{
    Added,
    Name,
    Size,
    Type,
}

/// <summary>Segoe Fluent Icons characters used by the design. Each comment gives the design's icon name.</summary>
public static class Glyphs
{
    public const string Add = "";
    public const string NewFolder = "";
    public const string GridView = "";
    public const string Sort = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Settings = "";
    public const string Tune = "";
    public const string Download = "";
    public const string Check = "";
    public const string CheckCircle = "";
    public const string Error = "";
    public const string Sync = "";
    public const string Schedule = "";
    public const string Cancelled = "";
    public const string Locate = "";
    public const string More = "";
    public const string Photo = "";
    public const string Video = "";
    public const string Audio = "";
    public const string Refresh = "";
    public const string FolderOpen = "";
    public const string Copy = "";
    public const string Delete = "";
    public const string OpenNew = "";
    public const string Info = "";
    public const string Close = "";
    public const string Back = "";
    public const string Forward = "";
    public const string Mouse = "";
    public const string Window = "";
    public const string Shield = "";
    public const string Picture = "";
    public const string Speed = "";
    public const string Chip = "";
    public const string Pulse = "";
    public const string Code = "";
    public const string Bug = "";
    public const string Document = "";
    public const string License = "";
    public const string Folder = "";
    public const string Verified = "";
    public const string Rotate = "";
    public const string Edit = "";
    public const string Play = "";
}

/// <summary>A color or brush from the design tokens.</summary>
public static class Tokens
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>The soft tile colors shown behind a file until its preview is ready. They come from the file name, so each file keeps its own hue.</summary>
public static class TileColors
{
    public static Brush For(string name, MediaGroup group)
    {
        if (group == MediaGroup.Audio)
        {
            return Tokens.Brush("McAudioTile");
        }

        var dark = Services.ThemeService.IsDark;
        var hue = 0;
        foreach (var c in name)
        {
            hue = (hue * 31 + c) % 360;
        }

        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = FromOklch(dark ? 0.46 : 0.86, 0.06, hue), Offset = 0 },
                new GradientStop { Color = FromOklch(dark ? 0.34 : 0.72, 0.07, (hue + 40) % 360), Offset = 1 },
            },
        };
    }

    /// <summary>Converts a color from the OKLCH color space (lightness, chroma, hue in degrees) to sRGB.</summary>
    public static Color FromOklch(double l, double c, double hueDegrees)
    {
        var h = hueDegrees * Math.PI / 180;
        var a = c * Math.Cos(h);
        var b = c * Math.Sin(h);

        var l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        var m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        var s_ = l - 0.0894841775 * a - 1.2914855480 * b;

        var lc = l_ * l_ * l_;
        var mc = m_ * m_ * m_;
        var sc = s_ * s_ * s_;

        var r = 4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc;
        var g = -1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc;
        var bl = -0.0041960863 * lc - 0.7034186147 * mc + 1.7076147010 * sc;

        return Color.FromArgb(255, ToByte(r), ToByte(g), ToByte(bl));
    }

    private static byte ToByte(double linear)
    {
        var clamped = Math.Clamp(linear, 0, 1);
        var gamma = clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * Math.Pow(clamped, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(gamma, 0, 1) * 255);
    }
}

/// <summary>One file card: its preview, its state, and the text and colors that go with that state.</summary>
public partial class FileItem : ObservableObject
{
    public FileItem(string path, MediaGroup group, long size, int addedIndex)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Extension = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        Group = group;
        Size = size;
        AddedIndex = addedIndex;
        tileBrush = TileColors.For(Name, group);
    }

    public string Path { get; }

    public string Name { get; }

    public string Extension { get; }

    public MediaGroup Group { get; }

    public long Size { get; }

    public int AddedIndex { get; }

    private Brush tileBrush;

    public Brush TileBrush => tileBrush;

    /// <summary>Called when light or dark changes, so the tile gets colors that suit it.</summary>
    public void RefreshTheme()
    {
        tileBrush = TileColors.For(Name, Group);
        OnPropertyChanged(nameof(TileBrush));
        OnPropertyChanged(nameof(TileIconBrush));
        NotifyDisplay();
    }

    public Brush TileIconBrush => Group == MediaGroup.Audio
        ? Tokens.Brush("McText2")
        : new SolidColorBrush(Color.FromArgb(217, 255, 255, 255));

    /// <summary>Where the converted file was saved. Set when the file is done.</summary>
    public string? OutputPath { get; set; }

    public long OutputBytes { get; set; }

    /// <summary>If the file was damaged part-way, how far the good part reaches. Used by "save the part before it".</summary>
    public double? PartialSeconds { get; set; }

    [ObservableProperty]
    public partial ItemState State { get; set; } = ItemState.Ready;

    /// <summary>0 to 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial string? DurationText { get; set; }

    /// <summary>Shown before converting, for example "4.2 MB → WebP".</summary>
    [ObservableProperty]
    public partial string PlannedText { get; set; } = string.Empty;

    /// <summary>The plain-language reason a file failed.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>The technical details behind a failure, for the "Technical details" box.</summary>
    [ObservableProperty]
    public partial string? Technical { get; set; }

    /// <summary>The "4.2 MB → 1.1 MB (−74%)" line shown once a file is done.</summary>
    [ObservableProperty]
    public partial string? DoneDetail { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The result would have been larger than the original, so nothing was saved.</summary>
    [ObservableProperty]
    public partial bool NotSmaller { get; set; }

    /// <summary>The user chose to save the file even though it comes out larger.</summary>
    public bool AllowLarger { get; set; }

    /// <summary>Only the next few queued files animate, so a long queue stays calm.</summary>
    [ObservableProperty]
    public partial bool AnimateWait { get; set; }

    public string KindGlyph => Group switch
    {
        MediaGroup.Video => Glyphs.Play,
        MediaGroup.Audio => Glyphs.Audio,
        _ => Glyphs.Photo,
    };

    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>The shared card sizes, so every card changes when the View menu does.</summary>
    public ViewOptions View => ViewOptions.Shared;

    /// <summary>The icon over the colored tile, shown only until a real preview exists.</summary>
    public Visibility TileIconVisibility => Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DurationVisibility => string.IsNullOrEmpty(DurationText) ? Visibility.Collapsed : Visibility.Visible;

    public string StatusGlyph => State switch
    {
        ItemState.Waiting => Glyphs.Schedule,
        ItemState.Active => Glyphs.Sync,
        ItemState.Done => Glyphs.CheckCircle,
        ItemState.Failed => Glyphs.Error,
        ItemState.Cancelled => Glyphs.Cancelled,
        _ => Group switch
        {
            MediaGroup.Video => Glyphs.Video,
            MediaGroup.Audio => Glyphs.Audio,
            _ => Glyphs.Photo,
        },
    };

    public Brush StatusGlyphBrush => State switch
    {
        ItemState.Active => Tokens.Brush("McAccentText"),
        ItemState.Done => Tokens.Brush("McOk"),
        ItemState.Failed => Tokens.Brush("McErr"),
        _ => Tokens.Brush("McText2"),
    };

    public string StatusText => State switch
    {
        ItemState.Waiting => "Waiting",
        ItemState.Active => string.Create(CultureInfo.InvariantCulture, $"Converting · {Progress:0}%"),
        ItemState.Done => "Done",
        ItemState.Failed => NotSmaller ? "Not made smaller" : "Failed",
        ItemState.Cancelled => "Cancelled",
        _ => PlannedText,
    };

    public Brush StatusBrush => State switch
    {
        ItemState.Active or ItemState.Done => Tokens.Brush("McText"),
        ItemState.Failed => Tokens.Brush("McErr"),
        _ => Tokens.Brush("McText2"),
    };

    public Windows.UI.Text.FontWeight StatusWeight => State is ItemState.Active or ItemState.Done or ItemState.Failed
        ? FontWeights.SemiBold
        : FontWeights.Normal;

    public string? Detail => State switch
    {
        ItemState.Done => DoneDetail,
        ItemState.Failed => Message,
        ItemState.Cancelled => "Stopped before it finished.",
        _ => null,
    };

    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RetryVisibility => State is ItemState.Failed or ItemState.Cancelled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BarVisibility => State is ItemState.Active or ItemState.Waiting ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True for the queued files that show the moving "waiting" bar.</summary>
    public bool BarIsMoving => State == ItemState.Waiting && AnimateWait;

    public string BarAutomationName => $"Progress for {Name}";

    public string AutomationText =>
        $"{Name}, {MediaGroups.One(Group)}, {StatusText}{(string.IsNullOrEmpty(Detail) ? string.Empty : ", " + Detail)}{(IsSelected ? ", selected" : string.Empty)}";

    public string MenuAutomationName => $"More options for {Name}";

    public Visibility SelectedVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    partial void OnStateChanged(ItemState value)
    {
        NotifyDisplay();
    }

    partial void OnProgressChanged(double value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(AutomationText));
    }

    partial void OnThumbnailChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(TileIconVisibility));
    }

    partial void OnDurationTextChanged(string? value) => OnPropertyChanged(nameof(DurationVisibility));

    partial void OnPlannedTextChanged(string value) => NotifyDisplay();

    partial void OnMessageChanged(string? value) => NotifyDisplay();

    partial void OnNotSmallerChanged(bool value)
    {
        NotifyDisplay();
        OnPropertyChanged(nameof(RetryLabel));
    }

    public string RetryLabel => NotSmaller ? "Save anyway" : "Retry";

    partial void OnDoneDetailChanged(string? value) => NotifyDisplay();

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedVisibility));
        OnPropertyChanged(nameof(AutomationText));
    }

    partial void OnAnimateWaitChanged(bool value) => OnPropertyChanged(nameof(BarIsMoving));

    private void NotifyDisplay()
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusGlyphBrush));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusWeight));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(DetailVisibility));
        OnPropertyChanged(nameof(RetryVisibility));
        OnPropertyChanged(nameof(BarVisibility));
        OnPropertyChanged(nameof(BarIsMoving));
        OnPropertyChanged(nameof(AutomationText));
    }

    /// <summary>Puts a finished or failed file back to "ready" so it can be converted again.</summary>
    public void ResetForRetry()
    {
        State = ItemState.Ready;
        Progress = 0;
        Message = null;
        Technical = null;
        DoneDetail = null;
        PartialSeconds = null;
        OutputPath = null;
        OutputBytes = 0;
        NotSmaller = false;
    }
}

/// <summary>A filter pill above the file list: "All 12", "Done 8".</summary>
public sealed partial class PillItem(string key, string label, int count, string? glyph, string glyphBrushKey, bool isSelected)
    : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public int Count { get; } = count;

    public string? Glyph { get; } = glyph;

    public Brush GlyphBrush { get; } = Tokens.Brush(glyphBrushKey);

    public bool IsSelected { get; } = isSelected;

    public Visibility GlyphVisibility => Glyph is null ? Visibility.Collapsed : Visibility.Visible;

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A preset chip in the inspector.</summary>
public sealed class ChipItem(Preset preset, bool isSelected)
{
    public Preset Preset { get; } = preset;

    public string Label => Preset.ChipLabel;

    public string FullName => Preset.Name;

    public bool IsSelected { get; } = isSelected;
}

/// <summary>One "Convert to" row in the inspector: a kind of file and its chosen format.</summary>
public sealed class KindRow(MediaGroup group, int count, FormatChoice choice)
{
    public MediaGroup Group { get; } = group;

    public string Glyph => Group switch
    {
        MediaGroup.Video => Glyphs.Video,
        MediaGroup.Audio => Glyphs.Audio,
        _ => Glyphs.Photo,
    };

    public string Label { get; } = $"{MediaGroups.Label(group)} · {count}";

    public string FormatName { get; } = choice.Name;

    public string FormatDescription { get; } = choice.Description;

    public string AutomationName { get; } = $"Convert {MediaGroups.Many(group)} to, {choice.Name}";
}

/// <summary>One line in the format picker.</summary>
public sealed class FormatRow(FormatChoice choice, bool isSelected)
{
    public FormatChoice Choice { get; } = choice;

    public string Name => Choice.Name;

    public string Description => Choice.Description;

    public string? Tag => Choice.Tag;

    public Visibility TagVisibility => Choice.Tag is null ? Visibility.Collapsed : Visibility.Visible;

    public bool IsSelected { get; } = isSelected;

    public Visibility SelectedVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Brush Background => IsSelected ? Tokens.Brush("McAccentSoft") : Tokens.Brush("McTransparent");

    public string Glyph => Choice.Group switch
    {
        MediaGroup.Audio => Glyphs.Audio,
        MediaGroup.Video => Glyphs.Video,
        MediaGroup.Gif => Choice.Format is TargetFormat.Mp4 or TargetFormat.WebM ? Glyphs.Video : Glyphs.Photo,
        _ => Choice.Format is TargetFormat.Mp4 or TargetFormat.WebM ? Glyphs.Video : Glyphs.Photo,
    };

    public Brush GlyphBrush => Choice.IsRecommended ? Tokens.Brush("McAccentText") : Tokens.Brush("McText2");

    public string AutomationName => $"{Choice.Name}, {Choice.Description}{(Choice.Tag is null ? string.Empty : ", " + Choice.Tag)}";
}

/// <summary>
/// How big the file cards are. "No previews" gives a compact card with just an icon. The other three show the
/// picture at three sizes. One shared instance is used by every card.
/// </summary>
public sealed partial class ViewOptions : ObservableObject
{
    public static ViewOptions Shared { get; } = new();

    /// <summary>Room the text under the picture needs: name, status, detail, retry button, progress bar.</summary>
    private const double TextArea = 120;

    public bool ShowThumbnails { get; private set; } = true;

    public double TileHeight { get; private set; } = 116;

    public GridLength TileRowHeight => new(TileHeight);

    public double CardMinWidth { get; private set; } = 176;

    public double CardMinHeight => TileHeight + TextArea;

    public double TileIconSize { get; private set; } = 28;

    /// <summary>The longest side of the preview picture to make. Larger than the card, so it stays sharp on sharp screens.</summary>
    public int ThumbnailPixels { get; private set; } = 320;

    public Visibility ImageVisibility => ShowThumbnails ? Visibility.Visible : Visibility.Collapsed;

    public void Apply(ThumbnailSize size)
    {
        (ShowThumbnails, TileHeight, CardMinWidth, TileIconSize, ThumbnailPixels) = size switch
        {
            ThumbnailSize.Small => (true, 76d, 140d, 22d, 200),
            ThumbnailSize.Large => (true, 176d, 264d, 40d, 560),
            ThumbnailSize.Off => (false, 44d, 200d, 20d, 0),
            _ => (true, 116d, 176d, 28d, 320),
        };

        OnPropertyChanged(string.Empty);
    }
}

/// <summary>A choice in a small list such as Speed or Channels.</summary>
public sealed record ListChoice<T>(string Label, T Value);
