using Mercury.Converter.App.Services;
using Mercury.Converter.App.ViewModels;
using Mercury.Converter.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Mercury.Converter.App;

public sealed partial class MainWindow : Window
{
    private const double NarrowBelow = 900;
    private const double WideFrom = 1700;

    private TaskbarProgress? taskbar;
    private bool isActive = true;
    private bool wasBusy;
    private bool narrow;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Mercury Converter";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ThemeService.Attach(this, Root);
        AppWindow.Resize(new SizeInt32(1280, 800));
        TrySetIcon();
        AddKeyboardShortcuts();

        Activated += (_, args) => isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        Notifier.Register();
        ViewModel.ResolveOutputFolder = ResolveOutputFolderAsync;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _ = ViewModel.CheckForUpdateAsync();

        // The View menu changes how big the cards are.
        ThemeService.Changed += UpdateThemeIcon;
        UpdateThemeIcon();
        ViewModel.View.PropertyChanged += (_, _) => ApplyCardSize();
        ApplyCardSize();

#if DEBUG
        // For making screenshots only: MERCURY_TEST_FRAME=2000x1250 opens the window at that many pixels, near the top-left.
        if (Environment.GetEnvironmentVariable("MERCURY_TEST_FRAME") is { Length: > 0 } frame)
        {
            var size = frame.Split('x').Select(int.Parse).ToArray();
            AppWindow.Resize(new SizeInt32(size[0], size[1]));
            AppWindow.Move(new PointInt32(40, 0));
        }

        // For testing only: MERCURY_TEST_THEME=Dark,Light cycles the theme every few seconds, like using the theme menu.
        if (Environment.GetEnvironmentVariable("MERCURY_TEST_THEME") is { Length: > 0 } themes)
        {
            var themeQueue = new Queue<AppTheme>(themes.Split(',').Select(Enum.Parse<AppTheme>));
            var themeTimer = DispatcherQueue.CreateTimer();
            themeTimer.Interval = TimeSpan.FromSeconds(4);
            themeTimer.Tick += (t, _) =>
            {
                if (themeQueue.Count > 0)
                {
                    ViewModel.Theme = themeQueue.Dequeue();
                }
                else
                {
                    t.Stop();
                }
            };
            themeTimer.Start();
        }

        // For testing only: MERCURY_TEST_VIEW=Large,Small cycles the View setting every few seconds, like clicking the menu.
        if (Environment.GetEnvironmentVariable("MERCURY_TEST_VIEW") is { Length: > 0 } sizes)
        {
            var queue = new Queue<ThumbnailSize>(sizes.Split(',').Select(Enum.Parse<ThumbnailSize>));
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(4);
            timer.Tick += (t, _) =>
            {
                if (queue.Count > 0)
                {
                    ViewModel.ThumbnailSetting = queue.Dequeue();
                }
                else
                {
                    t.Stop();
                }
            };
            timer.Start();
        }
#endif
        Closed += (_, _) => OnClosed();

        var startup = App.Startup;
        if (startup.Files.Count > 0)
        {
            ViewModel.ApplyStartup(startup);

            // Started from a right-click menu entry that names a format: convert straight away.
            if (startup.Target is not null)
            {
                _ = ConvertWhenReadyAsync();
            }
        }
    }

    public MainViewModel ViewModel { get; } = new();

    /// <summary>Adds files sent by another right-click while this window is open, and converts them if a format was named.</summary>
    public void Receive(ConvertCommand command)
    {
        ViewModel.ApplyStartup(command);
        if (command.Target is not null)
        {
            _ = ConvertWhenReadyAsync();
        }

        Activate();
    }

    private void TrySetIcon()
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }
    }

    private async Task ConvertWhenReadyAsync()
    {
        // Wait briefly so the window and its list finish loading before work starts.
        await Task.Delay(150);

        // Files sent while another run is going wait their turn.
        while (ViewModel.IsBusy)
        {
            await Task.Delay(500);
        }

        if (ViewModel.ConvertCommand.CanExecute(null))
        {
            ViewModel.ConvertCommand.Execute(null);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.OverallProgress) && ViewModel.ShowTaskbarProgress && ViewModel.IsBusy)
        {
            EnsureTaskbar().SetFraction(ViewModel.OverallProgress);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsBusy) && !ViewModel.IsBusy)
        {
            taskbar?.Clear();

            // A run that ended while the user was in another app gets a Windows notification.
            if (wasBusy && ViewModel.NotifyWhenDone && !isActive)
            {
                Notifier.Show("Conversions finished", ViewModel.SummaryText, WindowNative.GetWindowHandle(this));
            }

            wasBusy = false;
        }

        if (e.PropertyName == nameof(MainViewModel.IsBusy) && ViewModel.IsBusy)
        {
            wasBusy = true;
        }

        if (e.PropertyName is nameof(MainViewModel.IsPaneOpen))
        {
            ApplyLayout();
        }

        if (e.PropertyName is nameof(MainViewModel.Sort) or "")
        {
            SortLabel.Text = $"Sort: {ViewModel.SortChoices.First(c => c.Value == ViewModel.Sort).Label}";
        }

        if (e.PropertyName == string.Empty && ViewModel.HasFiles)
        {
            OfferRightClickTip();
        }

        if (e.PropertyName is nameof(MainViewModel.IsSettingsOpen))
        {
            SettingsPage.Visibility = ViewModel.IsSettingsOpen ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private TaskbarProgress EnsureTaskbar() =>
        taskbar ??= new TaskbarProgress(WindowNative.GetWindowHandle(this));

    private void OnClosed()
    {
        ViewModel.SaveSettings();
        taskbar?.Clear();
        taskbar?.Dispose();
    }

    // ----- Layout: two columns, or a pane that opens over the files when the window is narrow -------------------

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        narrow = e.NewSize.Width < NarrowBelow;
        InspectorColumn.Width = narrow ? new GridLength(0) : new GridLength(e.NewSize.Width >= WideFrom ? 400 : 360);
        if (!narrow)
        {
            ViewModel.IsPaneOpen = false;
        }

        OptionsButton.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (narrow)
        {
            Grid.SetColumn(Inspector, 0);
            Grid.SetColumnSpan(Inspector, 2);
            Inspector.HorizontalAlignment = HorizontalAlignment.Right;
            Inspector.Width = 340;
            Inspector.Visibility = ViewModel.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            PaneScrim.Visibility = ViewModel.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            Grid.SetColumn(Inspector, 1);
            Grid.SetColumnSpan(Inspector, 1);
            Inspector.HorizontalAlignment = HorizontalAlignment.Stretch;
            Inspector.Width = double.NaN;
            Inspector.Visibility = Visibility.Visible;
            PaneScrim.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyCardSize()
    {
        if (Cards.Layout is UniformGridLayout layout)
        {
            layout.MinItemWidth = ViewModel.View.CardMinWidth;
            layout.MinItemHeight = ViewModel.View.CardMinHeight;
        }
    }

    private void Options_Click(object sender, RoutedEventArgs e) => ViewModel.IsPaneOpen = !ViewModel.IsPaneOpen;

    private void Scrim_Tapped(object sender, TappedRoutedEventArgs e) => ViewModel.IsPaneOpen = false;

    private void Settings_Click(object sender, RoutedEventArgs e) => ViewModel.IsSettingsOpen = true;

    // ----- Keyboard ---------------------------------------------------------------------------------------------

    private void AddKeyboardShortcuts()
    {
        AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => AddFiles_Click(this, new RoutedEventArgs()));
        AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => AddFolder_Click(this, new RoutedEventArgs()));
        AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, () =>
        {
            if (!IsTyping())
            {
                ViewModel.SelectAll();
            }
        });
        AddShortcut(VirtualKey.Delete, VirtualKeyModifiers.None, () =>
        {
            if (!IsTyping() && !ViewModel.IsSettingsOpen)
            {
                ViewModel.RemoveItems(ViewModel.SelectedItems);
            }
        });
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, () => _ = ViewModel.RetryAsync(ViewModel.SelectedItems));
        AddShortcut(VirtualKey.Enter, VirtualKeyModifiers.None, () =>
        {
            if (FocusManager.GetFocusedElement(Root.XamlRoot) is not (Button or ToggleButton or ComboBox or TextBox or CheckBox)
                && !ViewModel.IsSettingsOpen && ViewModel.ConvertCommand.CanExecute(null))
            {
                ViewModel.ConvertCommand.Execute(null);
            }
        });
        AddShortcut(VirtualKey.Escape, VirtualKeyModifiers.None, () =>
        {
            if (ViewModel.IsSettingsOpen)
            {
                ViewModel.IsSettingsOpen = false;
            }
            else if (ViewModel.IsPaneOpen)
            {
                ViewModel.IsPaneOpen = false;
            }
            else if (ViewModel.IsBusy)
            {
                ViewModel.CancelCommand.Execute(null);
            }
        });
        AddShortcut(VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () =>
        {
            if (ViewModel.SelectedItems.FirstOrDefault() is { } item)
            {
                MainViewModel.CopyText(item.OutputPath ?? item.Path);
            }
        });
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers, ScopeOwner = Root };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private bool IsTyping() => FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or ComboBox;

    // ----- Adding files -----------------------------------------------------------------------------------------

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0)
        {
            ViewModel.AddFiles(files.Select(file => file.Path));
        }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
        {
            ViewModel.AddFolder(folder);
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        if (SafeOutput.CheckWritable(folder) is { } problem)
        {
            var dialog = NewDialog("Mercury can't save there");
            dialog.CloseButtonText = "OK";
            dialog.DefaultButton = ContentDialogButton.Close;
            dialog.Content = new TextBlock { Text = problem + "\n\nPlease choose a different folder.", TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            await dialog.ShowAsync();
            return;
        }

        ViewModel.OutputFolder = folder;
    }

    private void UseOriginalFolder_Click(object sender, RoutedEventArgs e) => ViewModel.OutputFolder = null;

    private void Files_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to Mercury Converter";
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void Files_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            ViewModel.AddFiles(items.OfType<StorageFile>().Select(file => file.Path));
            foreach (var folder in items.OfType<StorageFolder>())
            {
                ViewModel.AddFolder(folder.Path);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ----- Command bar menus ------------------------------------------------------------------------------------

    private void ViewMenu_Opening(object? sender, object e)
    {
        ViewMenu.Items.Clear();
        foreach (var choice in ViewModel.PreviewChoices)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = choice.Value == ThumbnailSize.Off ? "No previews" : $"{choice.Label} previews",
                GroupName = "Previews",
                IsChecked = ViewModel.ThumbnailSetting == choice.Value,
            };
            var size = choice.Value;
            item.Click += (_, _) => ViewModel.ThumbnailSetting = size;
            ViewMenu.Items.Add(item);
        }
    }

    private void ThemeMenu_Opening(object? sender, object e)
    {
        ThemeMenu.Items.Clear();
        foreach (var choice in ViewModel.ThemeChoices)
        {
            var item = new RadioMenuFlyoutItem { Text = choice.Label, GroupName = "Theme", IsChecked = ViewModel.Theme == choice.Value };
            var theme = choice.Value;
            item.Click += (_, _) => ViewModel.Theme = theme;
            ThemeMenu.Items.Add(item);
        }
    }

    /// <summary>A moon while the app is light (press for dark), and a sun while it is dark.</summary>
    private void UpdateThemeIcon() => ThemeIcon.Glyph = ThemeService.IsDark ? "" : "";

    private void SortMenu_Opening(object? sender, object e)
    {
        SortMenu.Items.Clear();
        foreach (var choice in ViewModel.SortChoices)
        {
            var item = new RadioMenuFlyoutItem { Text = choice.Label, GroupName = "Sort", IsChecked = ViewModel.Sort == choice.Value };
            var mode = choice.Value;
            item.Click += (_, _) => ViewModel.Sort = mode;
            SortMenu.Items.Add(item);
        }
    }

    // ----- Filters, banner, presets, rotation --------------------------------------------------------------------

    private void Pill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            ViewModel.SetFilter(key);
        }
    }

    private void BannerShow_Click(object sender, RoutedEventArgs e)
    {
        var done = ViewModel.Files.FirstOrDefault(f => f.State == ItemState.Done && f.OutputPath is not null);
        if (done?.OutputPath is { } path)
        {
            MainViewModel.ShowInFolder(path);
        }
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChipItem chip })
        {
            ViewModel.ApplyPreset(chip.Preset);
        }
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => ViewModel.RotateBy(-90);

    private void RotateRight_Click(object sender, RoutedEventArgs e) => ViewModel.RotateBy(90);

    // ----- The format picker ------------------------------------------------------------------------------------

    private void KindRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: KindRow row } anchor)
        {
            return;
        }

        var current = ViewModel.CurrentTarget(row.Group);
        var choices = FormatCatalog.For(row.Group);
        var panel = new StackPanel { Spacing = 2, Width = 320 };

        void AddSection(string title, IEnumerable<FormatChoice> list)
        {
            var items = list.ToList();
            if (items.Count == 0)
            {
                return;
            }

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["McSectionLabel"],
                Margin = new Thickness(8, 8, 0, 4),
            });
            foreach (var choice in items)
            {
                panel.Children.Add(BuildFormatRow(choice, choice.Format == current, row.Group));
            }
        }

        AddSection("Recommended", choices.Where(c => c.IsRecommended));
        AddSection("Other formats", choices.Where(c => !c.IsRecommended));

        var flyout = new Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
            Content = new ScrollViewer { MaxHeight = 420, Content = panel },
        };
        pickerFlyout = flyout;
        flyout.ShowAt(anchor);
    }

    private Flyout? pickerFlyout;

    private Button BuildFormatRow(FormatChoice choice, bool selected, MediaGroup group)
    {
        var row = new FormatRow(choice, selected);
        var text = new StackPanel { Spacing = 0 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = row.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Tokens.Brush("McText") });
        if (row.Tag is { } tag)
        {
            title.Children.Add(new TextBlock { Text = tag, FontSize = 12, Foreground = Tokens.Brush("McAccentText"), VerticalAlignment = VerticalAlignment.Center });
        }

        text.Children.Add(title);
        text.Children.Add(new TextBlock
        {
            Text = row.Description,
            Style = (Style)Application.Current.Resources["McCaptionText"],
        });

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = row.Glyph, FontSize = 16, Foreground = row.GlyphBrush, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (selected)
        {
            var check = new FontIcon { Glyph = Glyphs.Check, FontSize = 14, Foreground = Tokens.Brush("McAccentText"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }

        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6, 8, 6),
            Background = row.Background,
            BorderThickness = new Thickness(0),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, row.AutomationName);
        button.Click += (_, _) =>
        {
            ViewModel.SetTarget(group, choice.Format);
            pickerFlyout?.Hide();
        };
        return button;
    }

    // ----- Cards ------------------------------------------------------------------------------------------------

    private static FileItem? ItemOf(object sender) =>
        sender is FrameworkElement { DataContext: FileItem item } ? item : (sender as FrameworkElement)?.Tag as FileItem;

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject) || ItemOf(sender) is not { } item)
        {
            return;
        }

        ViewModel.ToggleSelection(item);
    }

    private void Card_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space && ItemOf(sender) is { } item)
        {
            ViewModel.ToggleSelection(item);
            e.Handled = true;
        }
    }

    private void Card_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && sender is FrameworkElement element)
        {
            BuildCardMenu(item).ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }

    private void CardMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && sender is FrameworkElement element)
        {
            BuildCardMenu(item).ShowAt(element);
        }
    }

    private void CardRetry_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            _ = ViewModel.RetryAsync([item]);
        }
    }

    private MenuFlyout BuildCardMenu(FileItem item)
    {
        var menu = new MenuFlyout();

        MenuFlyoutItem Add(string text, string glyph, Action action, bool enabled = true, VirtualKey? accelerator = null)
        {
            var entry = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            if (accelerator is { } key)
            {
                entry.KeyboardAcceleratorTextOverride = key == VirtualKey.F5 ? "F5" : key.ToString();
            }

            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
            return entry;
        }

        if (item.State == ItemState.Done && item.OutputPath is { } output)
        {
            Add("Open", Glyphs.OpenNew, () => MainViewModel.OpenFile(output));
            Add("Show in folder", Glyphs.FolderOpen, () => MainViewModel.ShowInFolder(output));
            Add("Copy saved file path", Glyphs.Copy, () => MainViewModel.CopyText(output));
            Add("Convert again", Glyphs.Refresh, () => _ = ViewModel.RetryAsync([item]), !ViewModel.IsBusy);
        }
        else
        {
            Add("Show original in folder", Glyphs.FolderOpen, () => MainViewModel.ShowInFolder(item.Path));
            Add("Copy file path", Glyphs.Copy, () => MainViewModel.CopyText(item.Path));
            if (item.State is ItemState.Failed or ItemState.Cancelled)
            {
                Add("Retry", Glyphs.Refresh, () => _ = ViewModel.RetryAsync([item]), !ViewModel.IsBusy, VirtualKey.F5);
                Add("Details", Glyphs.Info, () => _ = ShowProblemAsync(item));
            }
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        Add(item.State == ItemState.Done ? "Remove from list" : "Remove", Glyphs.Delete, () => ViewModel.RemoveItems([item]), item.State != ItemState.Active);
        return menu;
    }
}
