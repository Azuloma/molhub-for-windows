namespace MolHub.Windows;

/// <summary>
/// Chooses the MolHub icon variant for the surface it is drawn on. The source artwork is a
/// white line drawing, so light surfaces use the dark-line variant generated from it.
/// </summary>
public static class AppIconAssets
{
    public const string OnDarkImageUri = "ms-appx:///Assets/AppIconOnDark.png";
    public const string OnLightImageUri = "ms-appx:///Assets/AppIconOnLight.png";
    public const string OnDarkIconFile = "Assets\\AppIconOnDark.ico";
    public const string OnLightIconFile = "Assets\\AppIconOnLight.ico";

    public static string TitleBarImageUri(bool surfaceIsLight) => surfaceIsLight ? OnLightImageUri : OnDarkImageUri;

    public static string WindowIconFile(bool surfaceIsLight) => surfaceIsLight ? OnLightIconFile : OnDarkIconFile;
}
