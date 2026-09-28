using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace MolHub.Windows;

/// <summary>
/// Thin desktop acrylic for title-bar flyouts, so the screen behind shows through like the Windows account menu.
/// A windowed flyout popup never becomes the active window, so the default configuration would always report it
/// as inactive and draw the solid fallback; the flyout is only visible while in use, so it is treated as active.
/// </summary>
public sealed class ThinAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    /// <summary>The main window's actual theme, kept in sync by MainWindow.</summary>
    public ElementTheme Theme { get; set; } = ElementTheme.Default;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!DesktopAcrylicController.IsSupported()) return;
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        _controller.SetSystemBackdropConfiguration(new SystemBackdropConfiguration
        {
            IsInputActive = true,
            IsHighContrast = new AccessibilitySettings().HighContrast,
            Theme = Theme switch
            {
                ElementTheme.Light => SystemBackdropTheme.Light,
                ElementTheme.Dark => SystemBackdropTheme.Dark,
                _ => SystemBackdropTheme.Default
            }
        });
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controller is null) return;
        _controller.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller.Dispose();
        _controller = null;
    }
}
