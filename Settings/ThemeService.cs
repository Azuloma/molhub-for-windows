using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace MolHub.Windows;

/// <summary>Applies a persisted theme without overriding Windows High Contrast.</summary>
public sealed class ThemeService : IDisposable
{
    private readonly UISettings _settings = new();
    private FrameworkElement? _root;
    private string _preference = SettingsPolicy.DefaultTheme;

    public static string ReadSavedPreference()
    {
        try
        {
            return SettingsPolicy.NormalizeTheme(ApplicationData.Current.LocalSettings.Values[SettingsPolicy.ThemeKey] as string);
        }
        catch
        {
            return SettingsPolicy.DefaultTheme;
        }
    }

    public void Apply(FrameworkElement root, string? preference)
    {
        _settings.ColorValuesChanged -= Settings_ColorValuesChanged;
        _root = root;
        _preference = SettingsPolicy.NormalizeTheme(preference);
        ApplyCurrentTheme();
        if (_preference == SettingsPolicy.DefaultTheme)
        {
            _settings.ColorValuesChanged += Settings_ColorValuesChanged;
        }
    }

    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        if (_root is null) return;
        _root.DispatcherQueue.TryEnqueue(ApplyCurrentTheme);
    }

    private void ApplyCurrentTheme()
    {
        if (_root is null) return;
        // Default leaves WinUI's High Contrast resources in charge of accessibility colors.
        if (new AccessibilitySettings().HighContrast)
        {
            _root.RequestedTheme = ElementTheme.Default;
            return;
        }

        var background = _settings.GetColorValue(UIColorType.Background);
        var systemIsLight = ThemePolicy.IsLightColor(background.R, background.G, background.B);
        _root.RequestedTheme = ThemePolicy.ResolveIsLight(_preference, systemIsLight)
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    public void Dispose()
    {
        _settings.ColorValuesChanged -= Settings_ColorValuesChanged;
        _root = null;
    }
}
