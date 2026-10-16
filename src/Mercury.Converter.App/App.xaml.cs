using Microsoft.UI.Xaml;
using Mercury.Converter.Core;

namespace Mercury.Converter.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>What the right-click menu asked for, or <see cref="ConvertCommand.None"/> for a normal launch.</summary>
    public static ConvertCommand Startup { get; private set; } = ConvertCommand.None;

    public Window? Window { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();

        // The uninstaller runs "MercuryConverter.exe --remove-menu" to clean up the right-click menu without opening a window.
        if (arguments.Any(argument => string.Equals(argument, "--remove-menu", StringComparison.OrdinalIgnoreCase)))
        {
            Services.ShellIntegration.Remove();
            Exit();
            return;
        }

        // "--install-menu" does the same as turning on "Show in right-click menu", without opening a window.
        if (arguments.Any(argument => string.Equals(argument, "--install-menu", StringComparison.OrdinalIgnoreCase)))
        {
            Services.ShellIntegration.Install(Environment.ProcessPath ?? string.Empty);
            Exit();
            return;
        }

        Startup = ConvertCommand.Parse(arguments);

        // Files given without a format (for example dropped on the program's icon) are just added to the list.
        if (Startup == ConvertCommand.None)
        {
            var files = arguments.Skip(1)
                .Where(argument => !argument.StartsWith("--", StringComparison.Ordinal) && File.Exists(argument))
                .Select(Path.GetFullPath)
                .ToList();
            if (files.Count > 0)
            {
                Startup = new ConvertCommand(null, null, files);
            }
        }

        // One file from the right-click menu opens the small window, if the user has not chosen the full one.
        // "--quick file" opens the small window with its format list, so a format can be picked there.
        var quickIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--quick", StringComparison.OrdinalIgnoreCase));
        if (quickIndex >= 0 && quickIndex + 1 < arguments.Length && File.Exists(arguments[quickIndex + 1])
            && MediaGroups.Of(arguments[quickIndex + 1]) is not null)
        {
            Window = new QuickWindow(Path.GetFullPath(arguments[quickIndex + 1]), null, null);
            Window.Activate();
            return;
        }

        if (Startup.Target is not null && Startup.Files.Count == 1 && File.Exists(Startup.Files[0])
            && MediaGroups.Of(Startup.Files[0]) is not null && AppSettingsStore.Default.Load().UseQuickWindow)
        {
            Window = new QuickWindow(Startup.Files[0], Startup.Target, Startup.Quality);
            Window.Activate();
            return;
        }

        Window = new MainWindow();
        Window.Activate();
    }

    /// <summary>Opens the full window with these files added. Used by "Open in Mercury" in the small window.</summary>
    public void OpenMainWindow(IEnumerable<string> files)
    {
        Startup = new ConvertCommand(null, null, files.ToList());
        Window = new MainWindow();
        Window.Activate();
    }
}
