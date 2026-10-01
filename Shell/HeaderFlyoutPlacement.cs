using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace MolHub.Windows;

/// <summary>Opens a header flyout above or below its button, whichever has room (pure decision: FlyoutPlacementPolicy).</summary>
internal static class HeaderFlyoutPlacement
{
    public static void Show(Window window, Flyout flyout, FrameworkElement target)
    {
        // Set before showing: the Button's own flyout opening reads the same placement.
        flyout.Placement = OpensAbove(window, flyout, target)
            ? Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight
            : Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight;
        flyout.ShowAt(target);
    }

    private static bool OpensAbove(Window window, Flyout flyout, FrameworkElement target)
    {
        try
        {
            if (flyout.Content is not FrameworkElement content || target.XamlRoot is null) return false;
            content.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            // Before the first open a templated root may not measure yet; fall back to its maximum height.
            var height = content.DesiredSize.Height > 0 ? content.DesiredSize.Height : content.MaxHeight;
            if (double.IsInfinity(height) || height <= 0) return false;

            var scale = target.XamlRoot.RasterizationScale;
            var bounds = target.TransformToVisual(null).TransformBounds(new global::Windows.Foundation.Rect(0, 0, target.ActualWidth, target.ActualHeight));
            var hwnd = WindowNative.GetWindowHandle(window);
            var origin = new NativePoint();
            if (!ClientToScreen(hwnd, ref origin)) return false;
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var spaceAbove = (origin.Y + bounds.Top * scale - work.Y) / scale;
            var spaceBelow = (work.Y + work.Height - (origin.Y + bounds.Bottom * scale)) / scale;
            return FlyoutPlacementPolicy.OpenAbove(height, spaceBelow, spaceAbove);
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
