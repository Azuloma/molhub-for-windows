namespace MolHub.Windows;

/// <summary>Pure sizing policy for the title-bar page search control.</summary>
public static class TitleBarLayoutPolicy
{
    public static double SearchWidthForClient(double clientWidth)
    {
        if (double.IsNaN(clientWidth) || clientWidth < 0) return 96;
        return clientWidth switch
        {
            >= 1600 => 540,
            >= 1200 => 420,
            >= 900 => 320,
            >= 600 => 200,
            _ => 96
        };
    }
}
