using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace MolHub.Windows;

/// <summary>
/// The App settings page (language and theme), built in code. Saves ui.language / ui.theme through SettingsPolicy
/// keys; a language change needs a restart, a theme change is applied at once through the callback from MainWindow.
/// </summary>
internal sealed class SettingsView
{
    private readonly Func<string, string> _localize;
    private readonly Action _applySavedTheme;
    private RadioButtons? _languageOptions;
    private RadioButtons? _themeOptions;
    private InfoBar? _settingsInfoBar;
    private bool _updatingSettings;

    public SettingsView(Func<string, string> localize, Action applySavedTheme)
    {
        _localize = localize;
        _applySavedTheme = applySavedTheme;
    }

    private string L(string key) => _localize(key);

    public FrameworkElement CreateSettingsPage()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(32) };
        panel.Children.Add(new TextBlock
        {
            Text = L("Page_App settings"),
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"]
        });
        panel.Children.Add(new TextBlock { Text = L("SettingsDescription"), TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(new TextBlock { Text = L("LanguageHeader"), Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        _updatingSettings = true;
        _languageOptions = new RadioButtons();
        AutomationProperties.SetName(_languageOptions, L("LanguageHeader"));
        _languageOptions.Items.Add(new RadioButton { Content = L("LanguageEnglish"), Tag = "en-US" });
        _languageOptions.Items.Add(new RadioButton { Content = L("LanguageJapanese"), Tag = "ja-JP" });
        _languageOptions.SelectedItem = FindRadioButton(_languageOptions, ReadLanguage());
        _languageOptions.SelectionChanged += SettingsLanguage_SelectionChanged;
        panel.Children.Add(_languageOptions);

        panel.Children.Add(new TextBlock { Text = L("ThemeHeader"), Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        _themeOptions = new RadioButtons();
        AutomationProperties.SetName(_themeOptions, L("ThemeHeader"));
        _themeOptions.Items.Add(new RadioButton { Content = L("ThemeSystem"), Tag = "System" });
        _themeOptions.Items.Add(new RadioButton { Content = L("ThemeLight"), Tag = "Light" });
        _themeOptions.Items.Add(new RadioButton { Content = L("ThemeDark"), Tag = "Dark" });
        _themeOptions.SelectedItem = FindRadioButton(_themeOptions, ReadTheme());
        _themeOptions.SelectionChanged += SettingsTheme_SelectionChanged;
        panel.Children.Add(_themeOptions);
        _updatingSettings = false;

        _settingsInfoBar = new InfoBar { IsOpen = false, IsClosable = false };
        panel.Children.Add(_settingsInfoBar);
        scroll.Content = panel;
        return scroll;
    }

    private static RadioButton? FindRadioButton(RadioButtons buttons, string tag) =>
        buttons.Items.OfType<RadioButton>().FirstOrDefault(button => string.Equals(button.Tag as string, tag, StringComparison.Ordinal));

    internal static string ReadLanguage()
    {
        try { return SettingsPolicy.NormalizeLanguage(ApplicationData.Current.LocalSettings.Values[SettingsPolicy.LanguageKey] as string); }
        catch { return SettingsPolicy.DefaultLanguage; }
    }

    internal static string ReadTheme()
    {
        return ThemeService.ReadSavedPreference();
    }

    private void SettingsLanguage_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingSettings || _languageOptions?.SelectedItem is not RadioButton item) return;
        var language = SettingsPolicy.NormalizeLanguage(item.Tag as string);
        // RadioButtons can report the initial selection after the page is built; only a real change is saved.
        if (language == ReadLanguage()) return;
        PersistSetting(SettingsPolicy.LanguageKey, language, restartRequired: true);
    }

    private void SettingsTheme_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingSettings || _themeOptions?.SelectedItem is not RadioButton item) return;
        var theme = SettingsPolicy.NormalizeTheme(item.Tag as string);
        if (theme == ReadTheme()) return;
        if (PersistSetting(SettingsPolicy.ThemeKey, theme, restartRequired: false)) _applySavedTheme();
    }

    private bool PersistSetting(string key, string value, bool restartRequired)
    {
        var previous = key == SettingsPolicy.LanguageKey ? ReadLanguage() : ReadTheme();
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
            if (restartRequired)
            {
                ShowSettingsInfo(L("SettingsRestartTitle"), L("SettingsRestartMessage"), InfoBarSeverity.Informational);
            }
            else
            {
                ShowSettingsInfo(L("SettingsThemeSavedTitle"), L("SettingsThemeSavedMessage"), InfoBarSeverity.Informational);
            }
            return true;
        }
        catch
        {
            RollbackSetting(key, previous);
            ShowSettingsInfo(L("SettingsSaveErrorTitle"), L("SettingsSaveErrorMessage"), InfoBarSeverity.Error);
            return false;
        }
    }

    private void RollbackSetting(string key, string value)
    {
        _updatingSettings = true;
        try
        {
            if (key == SettingsPolicy.LanguageKey && _languageOptions is not null)
                _languageOptions.SelectedItem = FindRadioButton(_languageOptions, value);
            else if (key == SettingsPolicy.ThemeKey && _themeOptions is not null)
                _themeOptions.SelectedItem = FindRadioButton(_themeOptions, value);
        }
        finally
        {
            _updatingSettings = false;
        }
    }

    private void ShowSettingsInfo(string title, string message, InfoBarSeverity severity)
    {
        if (_settingsInfoBar is null) return;
        _settingsInfoBar.Title = title;
        _settingsInfoBar.Message = message;
        _settingsInfoBar.Severity = severity;
        _settingsInfoBar.IsOpen = true;
    }
}
