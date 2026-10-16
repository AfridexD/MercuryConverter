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

        Window = new MainWindow();
        Window.Activate();
    }
}
