using Mercury.Converter.App.Services;
using Mercury.Converter.App.ViewModels;
using Mercury.Converter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mercury.Converter.App;

/// <summary>The dialogs and the one-time tip: managing presets, a file's problem, and the right-click menu offer.</summary>
public sealed partial class MainWindow
{
    private TeachingTip? rightClickTip;
    private bool tipOffered;

    private static Style Res(string key) => (Style)Application.Current.Resources[key];

    private ContentDialog NewDialog(string title) => new()
    {
        XamlRoot = Root.XamlRoot,
        Title = title,
        Background = Tokens.Brush("McFlyout"),
        DefaultButton = ContentDialogButton.Primary,
    };

    // ----- Manage presets ---------------------------------------------------------------------------------------

    private async void ManagePresets_Click(object sender, RoutedEventArgs e) => await ShowPresetsAsync();

    private async Task ShowPresetsAsync()
    {
        var dialog = NewDialog("Presets");
        dialog.CloseButtonText = "Done";
        dialog.DefaultButton = ContentDialogButton.Close;

        var status = new TextBlock { Style = Res("McCaptionText"), Visibility = Visibility.Collapsed };
        var list = new StackPanel { Spacing = 4 };
        var nameBox = new TextBox { PlaceholderText = "Name this preset", MaxLength = 80 };

        void Rebuild()
        {
            list.Children.Clear();
            AddSection("Yours", ViewModel.Presets.Where(p => !p.IsBuiltIn).ToList(), editable: true);
            AddSection("Built in", ViewModel.Presets.Where(p => p.IsBuiltIn).ToList(), editable: false);
        }

        void AddSection(string title, List<Preset> presets, bool editable)
        {
            list.Children.Add(new TextBlock { Text = title, Style = Res("McSectionLabel"), Margin = new Thickness(0, 8, 0, 2) });
            if (presets.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "Nothing here yet. Save your current settings below.", Style = Res("McCaptionText") });
                return;
            }

            foreach (var preset in presets)
            {
                var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = preset.Name, Foreground = Tokens.Brush("McText"), TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = preset.Summary, Style = Res("McCaptionText") });
                row.Children.Add(text);

                if (editable)
                {
                    var edit = new Button { Style = Res("McSubtleButton"), Content = new FontIcon { Glyph = Glyphs.Edit, FontSize = 14 }, Width = 32, Height = 32, Padding = new Thickness(0) };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(edit, $"Rename {preset.Name}");
                    edit.Click += async (_, _) =>
                    {
                        var renamed = await AskNameAsync("Rename preset", preset.Name);
                        if (renamed is not null)
                        {
                            ViewModel.RenamePreset(preset, renamed);
                            Rebuild();
                        }
                    };
                    Grid.SetColumn(edit, 1);
                    row.Children.Add(edit);

                    var delete = new Button { Style = Res("McSubtleButton"), Content = new FontIcon { Glyph = Glyphs.Delete, FontSize = 14 }, Width = 32, Height = 32, Padding = new Thickness(0) };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(delete, $"Delete {preset.Name}");
                    delete.Click += (_, _) =>
                    {
                        ViewModel.RemovePreset(preset);
                        Rebuild();
                    };
                    Grid.SetColumn(delete, 2);
                    row.Children.Add(delete);
                }

                list.Children.Add(row);
            }
        }

        var save = new Button { Content = "Save current settings as a preset", Style = Res("McButton") };
        save.Click += (_, _) =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                ShowStatus("Type a name first.");
                return;
            }

            if (ViewModel.SaveCurrentAsPreset(name) is null)
            {
                ShowStatus("Add some files first, so there is something to save.");
                return;
            }

            nameBox.Text = string.Empty;
            ShowStatus($"Saved “{name}”.");
            Rebuild();
        };

        var import = new Button { Content = "Import…", Style = Res("McCompactButton") };
        import.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFileAsync() is { } file)
            {
                ShowStatus(ViewModel.ImportPresets(file.Path));
                Rebuild();
            }
        };

        var export = new Button { Content = "Export…", Style = Res("McCompactButton") };
        export.Click += async (_, _) =>
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Downloads, SuggestedFileName = "mercury-converter-presets" };
            picker.FileTypeChoices.Add("Mercury presets", [".json"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            if (await picker.PickSaveFileAsync() is { } file)
            {
                ViewModel.ExportPresets(file.Path);
                ShowStatus("Exported your presets.");
            }
        };

        void ShowStatus(string message)
        {
            status.Text = message;
            status.Visibility = Visibility.Visible;
        }

        // The name box takes the space the button leaves, so the button text is never cut off.
        var saveRow = new Grid { ColumnSpacing = 8 };
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        saveRow.Children.Add(nameBox);
        Grid.SetColumn(save, 1);
        saveRow.Children.Add(save);
        var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        moveRow.Children.Add(import);
        moveRow.Children.Add(export);

        Rebuild();
        dialog.Content = new ScrollViewer
        {
            MaxHeight = 460,
            Content = new StackPanel { Spacing = 12, MinWidth = 440, Children = { list, saveRow, moveRow, status } },
        };
        await dialog.ShowAsync();
    }

    private async Task<string?> AskNameAsync(string title, string current)
    {
        var box = new TextBox { Text = current, MaxLength = 80, SelectionStart = 0, SelectionLength = current.Length };
        var dialog = NewDialog(title);
        dialog.PrimaryButtonText = "Save";
        dialog.CloseButtonText = "Cancel";
        dialog.Content = box;
        return await dialog.ShowAsync() == ContentDialogResult.Primary && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }

    // ----- A file's problem -------------------------------------------------------------------------------------

    private async Task ShowProblemAsync(FileItem item)
    {
        var dialog = NewDialog(item.State == ItemState.Cancelled ? "This file was not converted" : "This file could not be converted");
        dialog.PrimaryButtonText = "Retry";
        dialog.SecondaryButtonText = "Copy details";
        dialog.CloseButtonText = "Close";

        var details = item.Technical ?? "No technical details were recorded.";
        var box = new TextBox
        {
            Text = details,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MaxHeight = 160,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
        };
        var expander = new Expander
        {
            Header = "Technical details",
            Content = box,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = item.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = item.Message ?? "Stopped before it finished.", Style = Res("McSecondaryText") });
        if (item.PartialSeconds is { } seconds)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"This file looks damaged after {SizeText.Duration(seconds)}. The part before that was converted.",
                Style = Res("McSecondaryText"),
            });
        }

        panel.Children.Add(expander);
        dialog.Content = panel;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.RetryAsync([item]);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            MainViewModel.CopyText(details);
        }
    }

    // ----- Offer to add the right-click menu --------------------------------------------------------------------

    /// <summary>Shown once, the first time files are added, if the right-click menu is off.</summary>
    private void OfferRightClickTip()
    {
        // Not when the window was opened by the right-click menu itself: that person already uses it.
        if (tipOffered || App.Startup.Files.Count > 0 || ViewModel.RightClickTipShown || ViewModel.RightClickOn || !ViewModel.HasFiles || Root.XamlRoot is null)
        {
            return;
        }

        tipOffered = true;
        var tip = new TeachingTip
        {
            Title = "Convert right from File Explorer",
            Subtitle = "Add Mercury to the right-click menu, so you can convert a file without opening this window. You can change this later in Settings.",
            ActionButtonContent = "Turn on",
            CloseButtonContent = "Not now",
            Target = AddFilesButton,
            PreferredPlacement = TeachingTipPlacementMode.Bottom,
            IsLightDismissEnabled = true,
        };

        tip.ActionButtonClick += (_, _) =>
        {
            try
            {
                ShellIntegration.Install(Environment.ProcessPath ?? string.Empty);
                ViewModel.RightClickOn = true;
                ViewModel.SetNotice("Right-click menu added. On Windows 11, choose Show more options first.");
            }
            catch (UnauthorizedAccessException)
            {
                ViewModel.SetNotice("Windows blocked the change. You can try again in Settings.");
            }

            tip.IsOpen = false;
        };
        tip.Closed += (_, _) =>
        {
            ViewModel.RightClickTipShown = true;
            ViewModel.SaveSettings();
            Root.Children.Remove(tip);
        };

        rightClickTip = tip;
        Root.Children.Add(tip);
        tip.IsOpen = true;
    }
}
