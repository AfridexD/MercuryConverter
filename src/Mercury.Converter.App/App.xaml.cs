using Mercury.Converter.App.Services;
using Mercury.Converter.Core;
using Microsoft.UI.Xaml;

namespace Mercury.Converter.App;

public partial class App : Application
{
    private StartupCoordinator? coordinator;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>What the right-click menu asked for, or <see cref="ConvertCommand.None"/> for a normal launch.</summary>
    public static ConvertCommand Startup { get; private set; } = ConvertCommand.None;

    public Window? Window { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
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

        ThemeService.Initialize(AppSettingsStore.Default.Load().Theme);

        RepairRightClickMenu();

        // Explorer starts the program once per selected file. The first process collects the others' files.
        if (Startup.Files.Count > 0)
        {
            coordinator = StartupCoordinator.TryBecomeOwner(Startup);
            if (coordinator is null)
            {
                Exit();
                return;
            }

            if (Startup.Target is not null)
            {
                Startup = await coordinator.GatherAsync(Startup, TimeSpan.FromMilliseconds(700));
            }
        }

        // "--quick file" opens the small window with its format list, so a format can be picked there.
        var quickIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--quick", StringComparison.OrdinalIgnoreCase));
        if (quickIndex >= 0 && quickIndex + 1 < arguments.Length && File.Exists(arguments[quickIndex + 1])
            && MediaGroups.Of(arguments[quickIndex + 1]) is not null)
        {
            Window = new QuickWindow(Path.GetFullPath(arguments[quickIndex + 1]), null, null);
        }
        else if (Startup.Target is not null && Startup.Files.Count == 1 && File.Exists(Startup.Files[0])
            && MediaGroups.Of(Startup.Files[0]) is not null && AppSettingsStore.Default.Load().UseQuickWindow)
        {
            // One file from the right-click menu opens the small window, if the user has not chosen the full one.
            Window = new QuickWindow(Startup.Files[0], Startup.Target, Startup.Quality);
        }
        else
        {
            Window = new MainWindow();
        }

        Window.Activate();

        // Files that arrive later (a second right-click while this window is open) are added here.
        var queue = Window.DispatcherQueue;
        coordinator?.Attach(command => queue.TryEnqueue(() => Receive(command)));
    }

    /// <summary>Opens the full window with these files added. Used by "Open in Mercury" in the small window.</summary>
    public void OpenMainWindow(IEnumerable<string> files)
    {
        Startup = new ConvertCommand(null, null, files.ToList());
        Window = new MainWindow();
        Window.Activate();
    }

    private void Receive(ConvertCommand command)
    {
        if (Window is MainWindow main)
        {
            main.Receive(command);
        }
        else
        {
            Startup = command;
            Window = new MainWindow();
            Window.Activate();
        }
    }

    /// <summary>
    /// If the right-click menu is on, writes its entries again. This keeps them pointing at the program where it is now,
    /// and replaces entries written by builds whose command Explorer could not fill in.
    /// </summary>
    private static void RepairRightClickMenu()
    {
        try
        {
            if (Services.ShellIntegration.NeedsRepair(Environment.ProcessPath ?? string.Empty))
            {
                Services.ShellIntegration.Install(Environment.ProcessPath ?? string.Empty);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // The menu keeps working as it was. Nothing to tell the user.
        }
    }
}
