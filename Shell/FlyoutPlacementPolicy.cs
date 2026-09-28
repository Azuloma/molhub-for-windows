namespace MolHub.Windows;

/// <summary>
/// Chooses whether a title-bar flyout opens above its button. WinUI positions windowed flyouts against the
/// monitor bounds, so a flyout that fits the screen can still open under the taskbar; this uses the work area.
/// </summary>
public static class FlyoutPlacementPolicy
{
    /// <summary>Gap, border and shadow allowance between the button and the flyout panel, in DIP.</summary>
    public const double AllowanceDip = 16;

    public static bool OpenAbove(double flyoutHeight, double spaceBelow, double spaceAbove) =>
        flyoutHeight + AllowanceDip > spaceBelow && spaceAbove > spaceBelow;
}
