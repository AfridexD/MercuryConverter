using Mercury.Converter.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Mercury.Converter.App.Services;

/// <summary>
/// The light and dark colors, from the design (docs/design/HANDOFF.md). The brushes in Tokens.xaml are shared by every
/// window, so changing their colors here changes the whole app at once, with no need to rebuild any page.
/// </summary>
public static class ThemeService
{
    private static readonly Dictionary<string, (Color Light, Color Dark)> Palette = new()
    {
        ["McBg"] = (Hex("#F2F3F6"), Hex("#1C1D22")),
        ["McWinStroke"] = (Hex("#1F000000"), Hex("#1AFFFFFF")),
        ["McLayer"] = (Hex("#FAFAFB"), Hex("#24262C")),
        ["McCard"] = (Hex("#FFFFFF"), Hex("#2C2E35")),
        ["McCardHover"] = (Hex("#F6F7F9"), Hex("#32353D")),
        ["McStroke"] = (Hex("#14000000"), Hex("#12FFFFFF")),
        ["McStrokeStrong"] = (Hex("#29000000"), Hex("#24FFFFFF")),
        ["McText"] = (Hex("#1A1B20"), Hex("#FFFFFF")),
        ["McText2"] = (Hex("#464953"), Hex("#C8CAD2")),
        ["McText3"] = (Hex("#5E626D"), Hex("#A2A6B1")),
        ["McCtrl"] = (Hex("#FFFFFF"), Hex("#0FFFFFFF")),
        ["McCtrlHover"] = (Hex("#F6F7F9"), Hex("#1AFFFFFF")),
        ["McCtrlStroke"] = (Hex("#1F000000"), Hex("#1AFFFFFF")),
        ["McSubtleHover"] = (Hex("#0D000000"), Hex("#0FFFFFFF")),
        ["McInput"] = (Hex("#FFFFFF"), Hex("#0DFFFFFF")),
        ["McAccent"] = (Hex("#1F4FD6"), Hex("#7FA3FF")),
        ["McAccentHover"] = (Hex("#1A43B8"), Hex("#9DB8F2")),
        ["McOnAccent"] = (Hex("#FFFFFF"), Hex("#0D1530")),
        ["McAccentText"] = (Hex("#1F4FD6"), Hex("#9DB8F2")),
        ["McAccentSoft"] = (Hex("#141F4FD6"), Hex("#247FA3FF")),
        ["McDisabledFill"] = (Hex("#14000000"), Hex("#14FFFFFF")),
        ["McDisabledText"] = (Hex("#66000000"), Hex("#73FFFFFF")),
        ["McTrack"] = (Hex("#1F000000"), Hex("#29FFFFFF")),
        ["McOk"] = (Hex("#0F7B0F"), Hex("#6CCB5F")),
        ["McErr"] = (Hex("#C42B1C"), Hex("#FF99A4")),
        ["McOkSoft"] = (Hex("#DFF6DD"), Hex("#263526")),
        ["McErrSoft"] = (Hex("#FDE7E9"), Hex("#442726")),
        ["McFlyout"] = (Hex("#FFFFFF"), Hex("#2C2E35")),
        ["McMenuBtn"] = (Hex("#E6FFFFFF"), Hex("#D91C1D22")),
        ["McSmoke"] = (Hex("#4D000000"), Hex("#80000000")),
        ["McAudioTile"] = (Hex("#E6E8EE"), Hex("#363942")),
    };

    private static readonly List<(Window Window, FrameworkElement Root)> Open = [];

    /// <summary>True while the dark colors are in use.</summary>
    public static bool IsDark { get; private set; }

    public static AppTheme Preference { get; private set; } = AppTheme.System;

    /// <summary>Raised after the colors changed, so things drawn in code (such as file tiles) can be redone.</summary>
    public static event Action? Changed;

    /// <summary>Starts a window using the theme. It follows Windows when the preference is <see cref="AppTheme.System"/>.</summary>
    public static void Attach(Window window, FrameworkElement root)
    {
        Open.Add((window, root));
        root.ActualThemeChanged += (_, _) =>
        {
            // Windows changed between light and dark while the app was set to follow it.
            if (Preference == AppTheme.System)
            {
                Update(window, root);
            }
        };
        root.RequestedTheme = ToElementTheme(Preference);
        Update(window, root);
        window.Closed += (_, _) => Open.RemoveAll(entry => entry.Window == window);
    }

    /// <summary>Changes the theme for every open window.</summary>
    public static void SetPreference(AppTheme preference)
    {
        Preference = preference;
        foreach (var (window, root) in Open.ToList())
        {
            root.RequestedTheme = ToElementTheme(preference);
            Update(window, root);
        }
    }

    /// <summary>Sets the theme before any window exists, so the first window opens in the right colors.</summary>
    public static void Initialize(AppTheme preference) => Preference = preference;

    private static void Update(Window window, FrameworkElement root)
    {
        var dark = root.ActualTheme == ElementTheme.Dark;
        Apply(dark);
        StyleCaptionButtons(window.AppWindow, dark);
    }

    private static void Apply(bool dark)
    {
        var changed = dark != IsDark;
        IsDark = dark;

        foreach (var (key, colors) in Palette)
        {
            if (Application.Current.Resources[key] is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
            {
                brush.Color = dark ? colors.Dark : colors.Light;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Draws the minimize, maximize and close buttons to match the theme.</summary>
    public static void StyleCaptionButtons(AppWindow window, bool dark)
    {
        var bar = window.TitleBar;
        var ink = dark ? Hex("#FFFFFF") : Hex("#1A1B20");
        var inactive = dark ? Hex("#A2A6B1") : Hex("#8A8D98");
        var hover = dark ? Hex("#1AFFFFFF") : Hex("#14000000");
        var pressed = dark ? Hex("#33FFFFFF") : Hex("#28000000");

        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = ink;
        bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = ink;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = ink;
    }

    private static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private static Color Hex(string value)
    {
        var text = value.TrimStart('#');
        var number = Convert.ToUInt32(text, 16);
        return text.Length == 8
            ? Color.FromArgb((byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number)
            : Color.FromArgb(255, (byte)(number >> 16), (byte)(number >> 8), (byte)number);
    }
}
