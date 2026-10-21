using Microsoft.UI.Xaml;

namespace Mercury.Converter.App;

/// <summary>Small functions used from XAML bindings.</summary>
public static class UiHelpers
{
    public static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The length badge is hidden when previews are off, because the picture strip is too short to hold it.</summary>
    public static Visibility BadgeVisible(string? duration, bool previewsOn) => previewsOn && !string.IsNullOrEmpty(duration) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The kind icon shows on a card until its picture is ready, and always when previews are off.</summary>
    public static Visibility IconVisible(bool hasPicture, bool previewsOn) => hasPicture && previewsOn ? Visibility.Collapsed : Visibility.Visible;
}
