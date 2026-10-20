using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Mercury.Converter.App;

/// <summary>Small functions used from XAML bindings and window setup.</summary>
public static class UiHelpers
{
    public static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The length badge is hidden when previews are off, because the picture strip is too short to hold it.</summary>
    public static Visibility BadgeVisible(string? duration, bool previewsOn) => previewsOn && !string.IsNullOrEmpty(duration) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The kind icon shows on a card until its picture is ready, and always when previews are off.</summary>
    public static Visibility IconVisible(bool hasPicture, bool previewsOn) => hasPicture && previewsOn ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Draws the minimize, maximize and close buttons in dark on light. Without this they follow the Windows theme,
    /// so with Windows in dark mode they are white on the app's light title bar and cannot be seen.
    /// </summary>
    public static void StyleCaptionButtons(AppWindow window)
    {
        var bar = window.TitleBar;
        var ink = Color.FromArgb(255, 0x1A, 0x1B, 0x20);
        var hover = Color.FromArgb(20, 0, 0, 0);
        var pressed = Color.FromArgb(40, 0, 0, 0);

        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = ink;
        bar.ButtonInactiveForegroundColor = Color.FromArgb(255, 0x8A, 0x8D, 0x98);
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = ink;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = ink;
    }
}
