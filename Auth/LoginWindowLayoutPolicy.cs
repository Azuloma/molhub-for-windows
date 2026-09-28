namespace MolHub.Windows;

/// <summary>
/// Sizes the sign-in window like a compact Windows account dialog: a 481 x 683 DIP client area
/// (32 DIP title bar plus the web sign-in surface), scaled for the monitor and kept inside its work area.
/// </summary>
public static class LoginWindowLayoutPolicy
{
    public const int ClientWidthDip = 481;
    public const int ClientHeightDip = 683;
    public const int TitleBarHeightDip = 32;
    public const int WorkAreaMarginDip = 16;

    public readonly record struct PixelSize(int Width, int Height);

    public readonly record struct PixelPoint(int X, int Y);

    /// <summary>Client size in physical pixels for the given scale, clamped to the work area minus a margin.</summary>
    public static PixelSize ClientSize(double scale, int workWidth, int workHeight)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var margin = (int)Math.Round(WorkAreaMarginDip * scale, MidpointRounding.AwayFromZero);
        var maxWidth = Math.Max(1, workWidth - 2 * margin);
        var maxHeight = Math.Max(1, workHeight - 2 * margin);
        return new PixelSize(
            Math.Min((int)Math.Round(ClientWidthDip * scale, MidpointRounding.AwayFromZero), maxWidth),
            Math.Min((int)Math.Round(ClientHeightDip * scale, MidpointRounding.AwayFromZero), maxHeight));
    }

    /// <summary>Top-left position that centers an outer window size in the work area, never above or left of it.</summary>
    public static PixelPoint CenteredPosition(int outerWidth, int outerHeight, int workX, int workY, int workWidth, int workHeight) =>
        new(workX + Math.Max(0, (workWidth - outerWidth) / 2), workY + Math.Max(0, (workHeight - outerHeight) / 2));
}
