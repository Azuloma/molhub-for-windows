using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace MolHub.Windows;

/// <summary>Sets the caption, Alt+Tab and taskbar window icon for the current Windows app theme.</summary>
internal static class WindowIcon
{
    public static void Apply(Window window)
    {
        try
        {
            var background = new UISettings().GetColorValue(UIColorType.Background);
            var surfaceIsLight = ThemePolicy.IsLightColor(background.R, background.G, background.B);
            var hwnd = WindowNative.GetWindowHandle(window);
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, AppIconAssets.WindowIconFile(surfaceIsLight)));
        }
        catch
        {
            // The package logo remains the fallback window icon.
        }
    }
}
