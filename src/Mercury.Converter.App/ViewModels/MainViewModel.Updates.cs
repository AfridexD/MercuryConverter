using CommunityToolkit.Mvvm.Input;
using Mercury.Converter.Core;

namespace Mercury.Converter.App.ViewModels;

/// <summary>Telling the user a newer version exists. Off until the user says yes, and it only ever shows a link.</summary>
public sealed partial class MainViewModel
{
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 1_000_000 };

    private UpdateInfo? update;
    private string? skippedVersion;
    private DateTime? lastUpdateCheck;

    /// <summary>The version of this program.</summary>
    public static Version CurrentVersion { get; } = typeof(MainViewModel).Assembly.GetName().Version is { } v
        ? new Version(v.Major, v.Minor, Math.Max(0, v.Build))
        : new Version(1, 0, 0);

    /// <summary>The first-run question: "Tell me when a new version is out?"</summary>
    public bool ShowUpdateQuestion { get; private set; }

    public bool ShowUpdateAvailable => update is not null;

    public string UpdateText => update is null ? string.Empty : $"Version {update.Text} is available.";

    private void LoadUpdateState(AppSettings settings)
    {
        skippedVersion = settings.SkippedVersion;
        lastUpdateCheck = settings.LastUpdateCheckUtc;
        UpdateQuestionAnswered = settings.UpdatePromptAnswered;
        ShowUpdateQuestion = !settings.UpdatePromptAnswered;
    }

    private bool UpdateQuestionAnswered { get; set; }

    /// <summary>Asks GitHub about a newer version, at most once a day, and only if the user has said yes.</summary>
    public async Task CheckForUpdateAsync()
    {
        if (!CheckForUpdates || lastUpdateCheck is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20))
        {
            return;
        }

        var found = await UpdateChecker.CheckAsync(CurrentVersion, UpdateHttp);
        lastUpdateCheck = DateTime.UtcNow;
        SaveSettings();

        if (found is not null && found.Text != skippedVersion)
        {
            update = found;
            OnPropertyChanged(string.Empty);
        }
    }

    [RelayCommand]
    private async Task AnswerUpdateQuestion(string answer)
    {
        UpdateQuestionAnswered = true;
        ShowUpdateQuestion = false;
        CheckForUpdates = answer == "yes";
        SaveSettings();
        OnPropertyChanged(string.Empty);
        if (CheckForUpdates)
        {
            await CheckForUpdateAsync();
        }
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (update is not null)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(update.PageUrl));
        }
    }

    [RelayCommand]
    private void SkipUpdate()
    {
        skippedVersion = update?.Text;
        update = null;
        SaveSettings();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        update = null;
        OnPropertyChanged(string.Empty);
    }
}
