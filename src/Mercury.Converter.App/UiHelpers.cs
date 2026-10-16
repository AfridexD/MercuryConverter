using Microsoft.UI.Xaml;

namespace Mercury.Converter.App;

/// <summary>Small functions used from XAML bindings.</summary>
public static class UiHelpers
{
    public static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;
}
