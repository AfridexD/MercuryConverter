using Mercury.Converter.App.Services;
using Mercury.Converter.App.ViewModels;
using Mercury.Converter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mercury.Converter.App;

public sealed partial class MainWindow : Window
{
    private bool suppressShellToggle;

    /// <summary>Ignores preset selection events that happen while the window is still being built.</summary>
    private bool ready;

    private TaskbarProgress? taskbar;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Mercury Converter";

        // The look of the Photos and Clipchamp apps: Mica material, with the app drawing its own title bar.
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1360, 860));
        TrySetIcon();

        SelectThumbnailMenu(ViewModel.ThumbnailSetting);

        suppressShellToggle = true;
        ShellToggle.IsOn = ShellIntegration.IsInstalled();
        suppressShellToggle = false;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => OnClosed();

        var startup = App.Startup;
        if (startup.Files.Count > 0)
        {
            ViewModel.ApplyCommand(startup);

            // Started from the right-click menu: convert straight away.
            _ = ConvertWhenReadyAsync();
        }

        ready = true;
    }

    public MainViewModel ViewModel { get; } = new();

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

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && PresetBox.SelectedItem is Preset preset)
        {
            ViewModel.ApplyPreset(preset);
        }
    }

    private void SelectThumbnailMenu(ThumbnailSize size)
    {
        ThumbOff.IsChecked = size == ThumbnailSize.Off;
        ThumbSmall.IsChecked = size == ThumbnailSize.Small;
        ThumbMedium.IsChecked = size == ThumbnailSize.Medium;
        ThumbLarge.IsChecked = size == ThumbnailSize.Large;
    }

    private void Thumbnails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem item && Enum.TryParse<ThumbnailSize>(item.Tag as string, out var size))
        {
            ViewModel.SetThumbnailSize(size);
        }
    }

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
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.AddFolder(folder.Path);
        }
    }

    private async void ImportPresets_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.ImportPresets(file.Path);
        }
    }

    private async void ExportPresets_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            SuggestedFileName = "mercury-converter-presets",
        };
        picker.FileTypeChoices.Add("Mercury presets", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            ViewModel.ExportPresets(file.Path);
        }
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.OutputFolder = folder.Path;
        }
    }

    private void UseOriginalFolder_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OutputFolder = null;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to converter";
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
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

    private void ShellToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (suppressShellToggle)
        {
            return;
        }

        try
        {
            if (ShellToggle.IsOn)
            {
                ShellIntegration.Install(Environment.ProcessPath ?? string.Empty);
            }
            else
            {
                ShellIntegration.Remove();
            }

            ViewModel.Summary = ShellToggle.IsOn
                ? "Right-click menu added. Right-click a file, then choose Show more options if you use Windows 11."
                : "Right-click menu removed.";
        }
        catch (UnauthorizedAccessException)
        {
            suppressShellToggle = true;
            ShellToggle.IsOn = !ShellToggle.IsOn;
            suppressShellToggle = false;
            ViewModel.Summary = "Windows blocked the change. Try again, or check your permissions.";
        }
    }
}
