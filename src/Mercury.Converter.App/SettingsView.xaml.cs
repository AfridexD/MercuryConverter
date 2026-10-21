using Mercury.Converter.App.Services;
using Mercury.Converter.App.ViewModels;
using Mercury.Converter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mercury.Converter.App;

/// <summary>One third-party program listed on the Settings page.</summary>
public sealed record LicenseEntry(string Name, string Use, string License, string Url)
{
    public string Summary => $"{Use} · {License}";

    public string ViewName => $"View license for {Name}";
}

/// <summary>The Settings page. It opens over the main window and is closed with the back button or Esc.</summary>
public sealed partial class SettingsView : UserControl
{
    public const string RepositoryUrl = "https://github.com/AfridexD/MercuryConverter";

    private static readonly LicenseEntry[] Licenses =
    [
        new("FFmpeg", "Video and audio", "GNU GPL v3", "https://ffmpeg.org/legal.html"),
        new("ImageMagick 7", "Photos", "ImageMagick License", "https://imagemagick.org/script/license.php"),
        new("libjxl", "JPEG XL", "BSD 3-Clause", "https://github.com/libjxl/libjxl/blob/main/LICENSE"),
        new("Windows App SDK", "App framework", "Microsoft license", "https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE"),
        new("CommunityToolkit.Mvvm", "App framework", "MIT", "https://github.com/CommunityToolkit/dotnet/blob/main/License.md"),
    ];

    private MainViewModel? viewModel;
    private bool loading;

    public SettingsView()
    {
        InitializeComponent();
        LicenseList.ItemsSource = Licenses;
        AboutText.Text = $"Version {MainViewModel.CurrentVersion.ToString(3)} · Free and open source (MIT) · Collects no usage data";
        OnVisibleChanged();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => OnVisibleChanged());
    }

    public MainViewModel? ViewModel
    {
        get => viewModel;
        set
        {
            viewModel = value;
            Load();
        }
    }

    private void OnVisibleChanged()
    {
        if (Visibility == Visibility.Visible)
        {
            Load();
        }
    }

    /// <summary>Copies the current settings into the controls without triggering their change handlers.</summary>
    private void Load()
    {
        if (viewModel is null)
        {
            return;
        }

        loading = true;
        RightClickSwitch.IsOn = ShellIntegration.IsInstalled();
        QuickWindowBox.ItemsSource = viewModel.QuickWindowChoices;
        QuickWindowBox.SelectedItem = viewModel.QuickWindowChoices.First(c => c.Value == viewModel.UseQuickWindow);
        ThemeBox.ItemsSource = viewModel.ThemeChoices;
        ThemeBox.SelectedItem = viewModel.ThemeChoices.First(c => c.Value == viewModel.Theme);
        PreviewBox.ItemsSource = viewModel.PreviewChoices;
        PreviewBox.SelectedItem = viewModel.PreviewChoices.First(c => c.Value == viewModel.ThumbnailSetting);
        SpeedBox.ItemsSource = viewModel.ModeChoices;
        SpeedBox.SelectedItem = viewModel.SelectedMode;
        GpuSwitch.IsOn = viewModel.UseGraphicsCardEncoder;
        GpuSwitch.IsEnabled = viewModel.GraphicsEncoderAvailable;
        GpuText.Text = viewModel.GraphicsEncoderText;
        TaskbarSwitch.IsOn = viewModel.ShowTaskbarProgress;
        UpdateSwitch.IsOn = viewModel.CheckForUpdates;
        NotifySwitch.IsOn = viewModel.NotifyWhenDone;
        FolderText.Text = viewModel.OutputFolderText;
        UseOriginalButton.Visibility = viewModel.OutputFolderIsCustom ? Visibility.Visible : Visibility.Collapsed;
        loading = false;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.IsSettingsOpen = false;
        }
    }

    private void RightClick_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading || viewModel is null)
        {
            return;
        }

        try
        {
            if (RightClickSwitch.IsOn)
            {
                ShellIntegration.Install(Environment.ProcessPath ?? string.Empty);
            }
            else
            {
                ShellIntegration.Remove();
            }

            viewModel.RightClickOn = RightClickSwitch.IsOn;
        }
        catch (UnauthorizedAccessException)
        {
            loading = true;
            RightClickSwitch.IsOn = !RightClickSwitch.IsOn;
            loading = false;
            viewModel.SetNotice("Windows blocked the change. Try again, or check your permissions.");
        }
    }

    private void QuickWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && viewModel is not null && QuickWindowBox.SelectedItem is ListChoice<bool> choice)
        {
            viewModel.UseQuickWindow = choice.Value;
        }
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && viewModel is not null && ThemeBox.SelectedItem is ListChoice<AppTheme> choice)
        {
            viewModel.Theme = choice.Value;
        }
    }

    private void Preview_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && viewModel is not null && PreviewBox.SelectedItem is ListChoice<ThumbnailSize> choice)
        {
            viewModel.ThumbnailSetting = choice.Value;
        }
    }

    private void Speed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && viewModel is not null && SpeedBox.SelectedItem is ListChoice<PerformanceMode> choice)
        {
            viewModel.SelectedMode = choice;
        }
    }

    private void Gpu_Toggled(object sender, RoutedEventArgs e)
    {
        if (!loading && viewModel is not null)
        {
            viewModel.UseGraphicsCardEncoder = GpuSwitch.IsOn;
        }
    }

    private void Taskbar_Toggled(object sender, RoutedEventArgs e)
    {
        if (!loading && viewModel is not null)
        {
            viewModel.ShowTaskbarProgress = TaskbarSwitch.IsOn;
        }
    }

    private void Notify_Toggled(object sender, RoutedEventArgs e)
    {
        if (!loading && viewModel is not null)
        {
            viewModel.NotifyWhenDone = NotifySwitch.IsOn;
        }
    }

    private void Update_Toggled(object sender, RoutedEventArgs e)
    {
        if (!loading && viewModel is not null)
        {
            viewModel.CheckForUpdates = UpdateSwitch.IsOn;
        }
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel is null || Application.Current is not App { Window: { } window })
        {
            return;
        }

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            // Refuse a folder that cannot take new files, and say why, instead of failing later on every file.
            if (SafeOutput.CheckWritable(folder.Path) is { } problem)
            {
                FolderText.Text = problem;
                return;
            }

            viewModel.OutputFolder = folder.Path;
            Load();
        }
    }

    private void UseOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.OutputFolder = null;
            Load();
        }
    }

    private static void Open(string url) =>
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));

    private void AnimationSettings_Click(object sender, RoutedEventArgs e) => Open("ms-settings:easeofaccess-visualeffects");

    private void SourceCode_Click(object sender, RoutedEventArgs e) => Open(RepositoryUrl);

    private void ReportProblem_Click(object sender, RoutedEventArgs e) => Open(RepositoryUrl + "/issues/new");

    private void License_Click(object sender, RoutedEventArgs e) => Open(RepositoryUrl + "/blob/main/LICENSE");

    private void ViewLicense_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            Open(url);
        }
    }
}
