using System.Runtime.InteropServices;
using Mercury.Converter.App.ViewModels;
using Mercury.Converter.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace Mercury.Converter.App;

/// <summary>
/// The small window the right-click menu opens: 360 px wide, next to the mouse, on top until the job is done.
/// It converts one file and shows the result, so nobody has to open the full window for a single file.
/// </summary>
public sealed partial class QuickWindow : Window
{
    private enum Phase
    {
        Pick,
        Running,
        Done,
        Problem,
    }

    private readonly string path;
    private readonly MediaGroup group;
    private readonly long inputBytes;
    private readonly AppSettings settings = AppSettingsStore.Default.Load();
    private readonly ConversionRunner runner = new(ToolLocator.Default);
    private readonly int? qualityFromMenu;
    private TargetFormat target;
    private CancellationTokenSource? cancellation;
    private Phase phase;
    private string? savedPath;
    private double? partialSeconds;

    public QuickWindow(string filePath, TargetFormat? chosen, int? quality)
    {
        InitializeComponent();
        Title = "Quick Convert";
        path = filePath;
        group = MediaGroups.Of(filePath) ?? MediaGroup.Photo;
        inputBytes = new FileInfo(filePath).Length;
        qualityFromMenu = quality;
        target = chosen is { } format && FormatCatalog.IsOffered(group, format) ? format : settings.TargetFor(group);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        UiHelpers.StyleCaptionButtons(AppWindow);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        KindIcon.Glyph = group switch { MediaGroup.Video => Glyphs.Video, MediaGroup.Audio => Glyphs.Audio, _ => Glyphs.Photo };
        FileName.Text = System.IO.Path.GetFileName(filePath);
        FileMeta.Text = $"{System.IO.Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant()} {MediaGroups.One(group)} · {SizeText.Of(inputBytes)}";

        var accelerator = new KeyboardAccelerator { Key = VirtualKey.Escape };
        accelerator.Invoked += (_, args) =>
        {
            Close();
            args.Handled = true;
        };
        Root.KeyboardAccelerators.Add(accelerator);

        var enter = new KeyboardAccelerator { Key = VirtualKey.Enter };
        enter.Invoked += (_, args) =>
        {
            if (phase == Phase.Pick)
            {
                Start(null);
            }

            args.Handled = true;
        };
        Root.KeyboardAccelerators.Add(enter);

        Closed += (_, _) => cancellation?.Cancel();
        Root.Loaded += (_, _) =>
        {
            PlaceNearCursor();
            if (chosen is not null)
            {
                Start(null);
            }
            else
            {
                ShowPick();
            }
        };
    }

    // ----- Position and size ------------------------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Windows.Graphics.PointInt32 point);

    private void PlaceNearCursor()
    {
        FitHeight();
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var area = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Nearest).WorkArea;
        var size = AppWindow.Size;
        var x = Math.Clamp(cursor.X + 12, area.X, Math.Max(area.X, area.X + area.Width - size.Width));
        var y = Math.Clamp(cursor.Y + 12, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height));
        AppWindow.Move(new PointInt32(x, y));
    }

    /// <summary>Makes the window exactly as tall as its content, so no empty space or scroll bar appears.</summary>
    private void FitHeight()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        Root.Measure(new Windows.Foundation.Size(360, double.PositiveInfinity));
        var height = Root.DesiredSize.Height;
        AppWindow.Resize(new SizeInt32((int)Math.Ceiling(360 * scale) + 2, (int)Math.Ceiling(height * scale) + 2));
    }

    private void Show(Phase next)
    {
        phase = next;
        PickPanel.Visibility = next == Phase.Pick ? Visibility.Visible : Visibility.Collapsed;
        RunPanel.Visibility = next == Phase.Running ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = next is Phase.Done or Phase.Problem ? Visibility.Visible : Visibility.Collapsed;
        FitHeight();
    }

    // ----- 1. Pick a format -------------------------------------------------------------------------------------

    private void ShowPick()
    {
        while (FormatList.Children.Count > 1)
        {
            FormatList.Children.RemoveAt(1);
        }

        var quality = settings.LastQuality;
        foreach (var choice in FormatCatalog.For(group))
        {
            var selected = choice.Format == target;
            var row = new Grid { Height = 36, Padding = new Thickness(10, 0, 10, 0), ColumnSpacing = 10 };
            row.Background = selected ? Tokens.Brush("McSubtleHover") : Tokens.Brush("McTransparent");
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = choice.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = Tokens.Brush("McText") });
            if (choice.IsRecommended)
            {
                var tag = new Border
                {
                    Background = Tokens.Brush("McAccentSoft"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Recommended", FontSize = 12, Foreground = Tokens.Brush("McAccentText") },
                };
                Grid.SetColumn(tag, 1);
                row.Children.Add(tag);
            }

            var estimate = new TextBlock
            {
                Text = "≈ " + SizeText.Of(SizeEstimator.Estimate(inputBytes, choice, quality)),
                FontSize = 12,
                Foreground = Tokens.Brush("McText2"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(estimate, 2);
            row.Children.Add(estimate);

            if (selected)
            {
                row.Children.Add(new Border
                {
                    Width = 3,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(-10, 10, 0, 10),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = Tokens.Brush("McAccent"),
                });
            }

            var format = choice.Format;
            row.Tapped += (_, _) =>
            {
                target = format;
                ShowPick();
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"{choice.Name}, about {SizeText.Of(SizeEstimator.Estimate(inputBytes, choice, quality))}");
            FormatList.Children.Add(row);
        }

        Show(Phase.Pick);
    }

    private void Convert_Click(object sender, RoutedEventArgs e) => Start(null);

    // ----- 2. Converting ----------------------------------------------------------------------------------------

    private async void Start(string? extraArguments)
    {
        var info = Targets.Get(target);
        var choice = FormatCatalog.Get(group, target);
        RunLabel.Text = $"Converting to {choice.Name}";
        RunPercent.Text = "0%";
        RunBar.Value = 0;
        Show(Phase.Running);

        var quality = qualityFromMenu ?? settings.LastQuality;
        var extras = ConversionOptions.FfmpegOptions(target, settings.VideoSpeed, settings.AudioChannels, false);
        if (extraArguments is not null)
        {
            extras = $"{extras} {extraArguments}".Trim();
        }

        var changes = group switch
        {
            MediaGroup.Audio => Transform.None,
            _ => Transform.None,
        };

        var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
        var sameExtension = string.Equals(System.IO.Path.GetExtension(path).TrimStart('.'), info.Extension, StringComparison.OrdinalIgnoreCase);
        var suffix = extraArguments is not null ? "-part" : sameExtension ? "-converted" : string.Empty;
        var folder = settings.OutputFolder ?? System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        var output = System.IO.Path.Combine(folder, $"{baseName}{suffix}.{info.Extension}");

        double? duration = null;
        if (group is MediaGroup.Video or MediaGroup.Audio)
        {
            duration = await MediaProbe.GetDurationSecondsAsync(path, ToolLocator.Default);
        }

        var request = new ConversionRequest(path, target, quality, Changes: changes, ExtraFfmpegArguments: extras);
        var progress = new Progress<double>(value =>
        {
            RunBar.Value = value * 100;
            RunPercent.Text = $"{value * 100:0}%";
        });

        cancellation = new CancellationTokenSource();
        var result = await runner.RunAsync(request, output, duration, progress, settings.KeepSmallerThanOriginal, cancellation.Token);

        if (result.Succeeded && result.OutputPath is { } saved)
        {
            savedPath = saved;
            var change = inputBytes == 0 ? 0 : (int)Math.Round((result.OutputBytes - inputBytes) * 100.0 / inputBytes);
            var sign = change > 0 ? "+" : change < 0 ? "−" : string.Empty;
            ShowResult(
                done: true,
                $"Saved as {System.IO.Path.GetFileName(saved)} · {SizeText.Of(result.OutputBytes)} ({sign}{Math.Abs(change)}%)",
                "Show in folder",
                "Close");
        }
        else if (result.WasCancelled)
        {
            Close();
        }
        else
        {
            partialSeconds = result.PartialSeconds;
            var text = partialSeconds is { } seconds
                ? $"This file is damaged after {SizeText.Duration(seconds)}. Mercury can save the part before that."
                : "This file could not be converted. Open it in Mercury to see why.";
            ShowResult(
                done: false,
                text,
                partialSeconds is { } part ? $"Save first {SizeText.Duration(part)}" : "Open in Mercury",
                partialSeconds is null ? "Close" : "Open in Mercury");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    // ----- 3 and 4. The result ----------------------------------------------------------------------------------

    private void ShowResult(bool done, string text, string primary, string secondary)
    {
        ResultBox.Background = Tokens.Brush(done ? "McOkSoft" : "McErrSoft");
        ResultIcon.Glyph = done ? Glyphs.CheckCircle : Glyphs.Error;
        ResultIcon.Foreground = Tokens.Brush(done ? "McOk" : "McErr");
        ResultText.Text = text;
        ResultPrimary.Content = primary;
        ResultSecondary.Content = secondary;
        Show(done ? Phase.Done : Phase.Problem);
        Activate();
    }

    private void ResultPrimary_Click(object sender, RoutedEventArgs e)
    {
        if (phase == Phase.Done && savedPath is not null)
        {
            MainViewModel.ShowInFolder(savedPath);
            Close();
        }
        else if (partialSeconds is { } seconds)
        {
            // The text is written as a plain number of seconds, which the FFmpeg allowlist accepts.
            Start($"-t {Math.Floor(seconds).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        else
        {
            OpenInMercury_Click(sender, e);
        }
    }

    private void ResultSecondary_Click(object sender, RoutedEventArgs e)
    {
        if (phase == Phase.Problem && partialSeconds is not null)
        {
            OpenInMercury_Click(sender, e);
        }
        else
        {
            Close();
        }
    }

    private void OpenInMercury_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).OpenMainWindow([path]);
        Close();
    }
}
