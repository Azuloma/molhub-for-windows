using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace MolHub.Windows;

/// <summary>
/// Keeps the system-drawn caption buttons in the app theme. They follow the Windows app mode by default, so a
/// Light app theme on a dark system (or the reverse) would draw them in the wrong colors.
/// </summary>
internal static class CaptionButtonTheme
{
    public static void Attach(Window window, FrameworkElement root)
    {
        Apply(window, root);
        root.ActualThemeChanged += (_, _) => Apply(window, root);
    }

    private static void Apply(Window window, FrameworkElement root)
    {
        try
        {
            // High Contrast keeps the system colors.
            window.AppWindow.TitleBar.PreferredTheme = new AccessibilitySettings().HighContrast
                ? TitleBarTheme.UseDefaultAppMode
                : root.ActualTheme == ElementTheme.Light ? TitleBarTheme.Light : TitleBarTheme.Dark;
        }
        catch
        {
            // Hosts without AppWindow title bar support keep the default caption colors.
        }
    }
}
