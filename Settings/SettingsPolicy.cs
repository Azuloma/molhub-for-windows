namespace MolHub.Windows;

/// <summary>Validation rules for the small, intentionally fixed UI settings schema.</summary>
public static class SettingsPolicy
{
    public const string LanguageKey = "ui.language";
    public const string ThemeKey = "ui.theme";
    public const string DefaultLanguage = "en-US";
    public const string DefaultTheme = "System";

    public static string NormalizeLanguage(string? value) =>
        value is "en-US" or "ja-JP" ? value : DefaultLanguage;

    public static string NormalizeTheme(string? value) =>
        value is "System" or "Light" or "Dark" ? value : DefaultTheme;
}

/// <summary>Pure theme decisions; OS color acquisition is kept in ThemeService.</summary>
public static class ThemePolicy
{
    public static bool IsLightColor(byte red, byte green, byte blue) =>
        (0.2126 * red + 0.7152 * green + 0.0722 * blue) >= 128;

    public static bool ResolveIsLight(string? preference, bool systemIsLight) =>
        SettingsPolicy.NormalizeTheme(preference) switch
        {
            "Light" => true,
            "Dark" => false,
            _ => systemIsLight
        };
}
