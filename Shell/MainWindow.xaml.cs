using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace FusionLedger.Windows;

public sealed partial class MainWindow : Window
{
    // Replaced (status, role, project manager) when a pending account is found approved.
    private AuthenticatedUser _user;
    private readonly ResourceLoader _strings = ResourceLoader.GetForViewIndependentUse();
    private readonly ObservableCollection<string> _searchSuggestions = [];
    private readonly Stack<NativePage> _history = new();
    private readonly ThemeService _themeService = new();
    private readonly WebBridgeClient _bridge = new();
    private DashboardView? _dashboard;
    private ProjectsView? _projects;
    private ProjectsView? _commitHistory;
    private ApprovalView? _approval;
    private MaintenanceView? _maintenance;
    private ManagementView? _management;
    private ProfileView? _profile;
    private AdministrationView? _administration;
    // One write at a time for the whole app (Projects, Commit history, Project management and Profile settings).
    private readonly WriteGate _writeGate = new();
    private string _bridgeStatusKey = "BridgeConnecting";
    private TextBlock? _bridgeStatusText;
    private bool _signingOut;
    // A password change ended the session while another write was still being sent: sign out once the gate is free.
    private bool _signOutWhenGateFree;
    private NativePage _currentPage = NativePage.Dashboard;
    private RadioButtons? _languageOptions;
    private RadioButtons? _themeOptions;
    private InfoBar? _settingsInfoBar;
    private bool _updatingSettings;
    private bool _syncingNavigationSelection;
    private bool _initialNavigationCompleted;

    public MainWindow(AuthenticatedUser user)
    {
        _user = user;
        InitializeComponent();
        Closed += MainWindow_Closed;
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureNativeTitleBar();
        WindowIcon.Apply(this);

        PageSearchBox.ItemsSource = _searchSuggestions;
        ConfigureProfile();
        ConfigureAccelerators();
        ApplyLocalizedStrings();
        RootGrid.ActualThemeChanged += (_, _) => { UpdateTitleBarIcon(); UpdateFlyoutBackdropTheme(); };
        ApplySavedTheme();
        CaptionButtonTheme.Attach(this, RootGrid);
        UpdateTitleBarIcon();
        UpdateFlyoutBackdropTheme();
    }

    private void UpdateFlyoutBackdropTheme()
    {
        foreach (var flyout in new[] { AccountFlyout, NotificationsFlyout })
        {
            if (flyout.SystemBackdrop is ThinAcrylicBackdrop backdrop) backdrop.Theme = RootGrid.ActualTheme;
        }
    }

    private void UpdateTitleBarIcon()
    {
        AppTitleBar.IconSource = new ImageIconSource
        {
            ImageSource = new BitmapImage(new Uri(AppIconAssets.TitleBarImageUri(RootGrid.ActualTheme == ElementTheme.Light)))
        };
    }

    private string L(string key) => _strings.GetString(key);

    private void ConfigureNativeTitleBar()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new global::Windows.Graphics.SizeInt32(1464, 934));
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            }
        }
        catch
        {
            // Unpackaged or older host shells can omit AppWindow customization.
            // The standard WinUI TitleBar remains usable with its safe 48px layout.
        }
    }

    private void ApplySavedTheme()
    {
        _themeService.Apply(RootGrid, ReadTheme());
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _themeService.Dispose();
        _bridge.Dispose();
    }

    /// <summary>Connects the hidden data bridge and confirms the server session behind it.</summary>
    private async Task ConnectBridgeAsync()
    {
        if (!await _bridge.StartAsync())
        {
            SetBridgeStatus("BridgeUnavailable", _bridge.LastError);
            return;
        }

        var session = await _bridge.RequestAsync("session");
        if (!session.Ok || session.Data is not { } data)
        {
            SetBridgeStatus("BridgeUnavailable", $"session {session.Status} {session.ErrorCode}");
            return;
        }
        var signedIn = data.TryGetProperty("user", out var user) && user.ValueKind == System.Text.Json.JsonValueKind.Object;
        SetBridgeStatus(signedIn ? "BridgeConnected" : "BridgeSessionEnded");
    }

    private string? _bridgeStatusDetail;

    private void SetBridgeStatus(string key, string? detail = null)
    {
        _bridgeStatusKey = key;
        _bridgeStatusDetail = detail;
        if (_bridgeStatusText is not null) _bridgeStatusText.Text = BridgeStatusText();
    }

    private string BridgeStatusText() =>
        string.IsNullOrEmpty(_bridgeStatusDetail) ? L(_bridgeStatusKey) : $"{L(_bridgeStatusKey)} ({_bridgeStatusDetail})";

    private void ApplyLocalizedStrings()
    {
        AppTitleBar.Title = L("ProductName");
        AppTitleBar.Subtitle = L("Beta");
        Title = L("MainWindowTitle");
        PageSearchBox.PlaceholderText = L("SearchPages");
        AutomationProperties.SetName(AppTitleBar, L("TitleBarName"));
        AutomationProperties.SetName(PageSearchBox, L("SearchPages"));
        AutomationProperties.SetName(Navigation, L("NavigationName"));
        ToolTipService.SetToolTip(NotificationsButton, L("Notifications"));
        var accountMenuName = string.Format(L("AccountMenuForFormat"), _user.Username);
        ToolTipService.SetToolTip(ProfileButton, accountMenuName);
        AutomationProperties.SetName(NotificationsButton, L("Notifications"));
        AutomationProperties.SetName(ProfileButton, accountMenuName);
        NotificationsHeader.Text = L("Notifications");
        NotificationsEmpty.Text = L("NotConnected");
        AccountName.Text = _user.Username;
        UpdateAccountStatusText();
        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>().Concat(Navigation.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            if (TryParsePage(item.Tag as string, out var page)) item.Content = L("Page_" + NativePageCatalog.SearchKey(page));
        }
        AdministrationLabel.Text = L("Administration");
        ProfileSettingsLabel.Text = L("ProfileSettings");
        SignOutLabel.Text = L("SignOut");
        AutomationProperties.SetName(ProfileSettingsButton, L("ProfileSettings"));
        AutomationProperties.SetName(AdministrationButton, L("Administration"));
        AutomationProperties.SetName(SignOutButton, L("SignOut"));
    }

    private void UpdateAccountStatusText()
    {
        AccountStatus.Text = string.Format(L("AccountStatusFormat"), LocalizedValue("Status", _user.Status));
        AccountRole.Text = string.Format(L("AccountRoleFormat"), LocalizedValue("Role", _user.Role));
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySearchWidth(RootGrid.ActualWidth);
        if (_initialNavigationCompleted) return;
        // Let NavigationView finish its initial selection/layout before showing the first page.
        _initialNavigationCompleted = true;
        NavigateTo(NativePageCatalog.StartPage(_user), false);
        _ = ConnectBridgeAsync();
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplySearchWidth(e.NewSize.Width);
    }

    private void ApplySearchWidth(double clientWidth)
    {
        PageSearchBox.Width = TitleBarLayoutPolicy.SearchWidthForClient(clientWidth);
        PageSearchBox.Visibility = Visibility.Visible;
    }

    private void ConfigureProfile()
    {
        ApplyPageAccess();
        ProfilePicture.DisplayName = _user.Username;
        AccountPicture.DisplayName = _user.Username;
        ApplyHeaderAvatars();
    }

    /// <summary>Shows the current icon in the title bar and the account flyout, or the initials when there is none.</summary>
    private void ApplyHeaderAvatars()
    {
        if (_user.AvatarPng is { Length: > 0 } avatar)
        {
            // The account flyout picture is out of the tree while closed, so both pictures re-decode on load.
            AvatarImage.Attach(ProfilePicture, avatar, 64);
            AvatarImage.Attach(AccountPicture, avatar, 96);
        }
        else
        {
            AvatarImage.Clear(ProfilePicture);
            AvatarImage.Clear(AccountPicture);
        }
    }

    /// <summary>
    /// Profile settings saved (or re-read) the icon: the title bar, the account flyout and the Projects profile column show
    /// it at once, and the other pages reload when shown again so server-sent avatars are current too.
    /// </summary>
    private void OnAvatarChanged(byte[]? avatar)
    {
        if (_signingOut) return;
        UpdateUser(_user with { AvatarPng = avatar });
        ApplyHeaderAvatars();
        _projects?.UserChanged();
        _commitHistory?.UserChanged();
        _administration?.UserChanged();
        OnWorkChanged();
    }

    /// <summary>
    /// Shows the pane items and account rows this account may open (pending accounts: approval screen and
    /// Server maintenance; approved: the workspace; admin: administration).
    /// </summary>
    private void ApplyPageAccess()
    {
        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>().Concat(Navigation.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            if (TryParsePage(item.Tag as string, out var page))
                item.Visibility = NativePageCatalog.IsAvailable(page, _user) ? Visibility.Visible : Visibility.Collapsed;
        }
        AdministrationButton.Visibility = NativePageCatalog.IsAvailable(NativePage.Administration, _user)
            ? Visibility.Visible : Visibility.Collapsed;
        ProfileSettingsButton.Visibility = NativePageCatalog.IsAvailable(NativePage.ProfileSettings, _user)
            ? Visibility.Visible : Visibility.Collapsed;
        AccountSeparator.Visibility = ProfileSettingsButton.Visibility == Visibility.Visible || AdministrationButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A newer session read of the same account (for example pending → rejected) updates the menu and pane.</summary>
    private void UpdateUser(AuthenticatedUser user)
    {
        _user = user;
        UpdateAccountStatusText();
        ApplyPageAccess();
    }

    /// <summary>The approval screen found the same account approved: open the workspace without a new sign-in.</summary>
    private void OnApproved(AuthenticatedUser user)
    {
        // A reply that arrives after Sign out must not open the workspace in the closing window.
        if (_signingOut) return;
        UpdateUser(user);
        _approval = null;
        _history.Clear();
        NavigateTo(NativePage.Dashboard, false);
    }

    private void ConfigureAccelerators()
    {
        // Windows.System.VirtualKey does not name OEM comma; 0xBC is VK_OEM_COMMA (Ctrl+,).
        AddAccelerator((global::Windows.System.VirtualKey)0xBC, global::Windows.System.VirtualKeyModifiers.Control, (_, e) =>
        {
            NavigateTo(NativePage.AppSettings);
            e.Handled = true;
        });
        AddAccelerator(global::Windows.System.VirtualKey.N, global::Windows.System.VirtualKeyModifiers.Menu, (_, e) =>
        {
            ShowHeaderFlyout(NotificationsFlyout, NotificationsButton);
            e.Handled = true;
        });
        AddAccelerator(global::Windows.System.VirtualKey.Left, global::Windows.System.VirtualKeyModifiers.Menu, (_, e) =>
        {
            GoBack();
            e.Handled = true;
        });
    }

    private void PageSearchKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        PageSearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void AddAccelerator(global::Windows.System.VirtualKey key, global::Windows.System.VirtualKeyModifiers modifiers,
        global::Windows.Foundation.TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += handler;
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args) => GoBack();

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_syncingNavigationSelection
            && args.SelectedItem is NavigationViewItem item
            && TryParsePage(item.Tag as string, out var page))
        {
            NavigateTo(page);
        }
    }

    private void NavigateTo(NativePage page, bool remember = true)
    {
        if (!NativePageCatalog.IsAvailable(page, _user)) return;
        if (remember && page != _currentPage) _history.Push(_currentPage);
        _currentPage = page;
        ContentFrame.Content = CreatePlaceholder(page);
        UpdateBackButton();
        SyncNavigationSelection(page);
    }

    /// <summary>The page's own screen stack (Projects, Commit history, Server maintenance or Administration), which Back walks first.</summary>
    private IScreenStack? CurrentStackPage => _currentPage switch
    {
        NativePage.Projects => _projects,
        NativePage.CommitHistory => _commitHistory,
        NativePage.ServerMaintenance => _maintenance,
        NativePage.Administration => _administration,
        _ => null
    };

    /// <summary>Back is shown for nested pages and for an item opened inside a page's own screen stack.</summary>
    private void UpdateBackButton()
    {
        var stackNested = CurrentStackPage?.CanGoBack == true;
        AppTitleBar.IsBackButtonVisible = NativePageCatalog.IsNested(_currentPage) || stackNested;
        AppTitleBar.IsBackButtonEnabled = _history.Count > 0 || stackNested;
    }

    private void GoBack()
    {
        if (CurrentStackPage?.TryGoBack() == true) return;
        if (_history.Count == 0) return;
        var page = _history.Pop();
        NavigateTo(page, false);
    }

    private FrameworkElement CreatePlaceholder(NativePage page)
    {
        if (page == NativePage.Dashboard) return CreateDashboardPage();
        if (page == NativePage.Projects) return CreateProjectsPage();
        if (page == NativePage.CommitHistory) return CreateCommitHistoryPage();
        if (page == NativePage.AwaitingApproval) return CreateApprovalPage();
        if (page == NativePage.ServerMaintenance) return CreateMaintenancePage();
        if (page == NativePage.ProjectManagement) return CreateManagementPage();
        if (page == NativePage.ProfileSettings) return CreateProfilePage();
        if (page == NativePage.Administration) return CreateAdministrationPage();
        if (page == NativePage.AppSettings) return CreateSettingsPage();
        if (page == NativePage.VersionInfo) return CreateVersionInfoPage();

        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = L("Page_" + NativePageCatalog.SearchKey(page)), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = L("NotConnected"), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private FrameworkElement CreateDashboardPage()
    {
        _dashboard ??= new DashboardView(L, _user, ReadLanguage(),
            () => _bridge.RequestAsync("dashboard"),
            page => NavigateTo(page),
            OpenProject,
            OpenPublish,
            SignOutWhenWritesFinish);
        _ = _dashboard.EnsureLoadedAsync();
        return _dashboard;
    }

    private FrameworkElement CreateProjectsPage()
    {
        _projects ??= new ProjectsView(L, () => _user, ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            SignOutWhenWritesFinish,
            UpdateBackButton,
            ProjectsRoot.List,
            OnWorkChanged,
            _writeGate);
        _ = _projects.EnsureLoadedAsync();
        return _projects;
    }

    private FrameworkElement CreateCommitHistoryPage()
    {
        _commitHistory ??= new ProjectsView(L, () => _user, ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            SignOutWhenWritesFinish,
            UpdateBackButton,
            ProjectsRoot.History,
            OnWorkChanged,
            _writeGate);
        _ = _commitHistory.EnsureLoadedAsync();
        return _commitHistory;
    }

    private FrameworkElement CreateApprovalPage()
    {
        _approval ??= new ApprovalView(L, _user,
            () => _bridge.RequestAsync("session"),
            OnApproved,
            UpdateUser,
            // The web's "Announcements" button; the announcements live on the Server maintenance page.
            () => NavigateTo(NativePage.ServerMaintenance),
            SignOutWhenWritesFinish);
        return _approval;
    }

    private FrameworkElement CreateManagementPage()
    {
        _management ??= new ManagementView(L, ReadLanguage(), _user,
            (command, payload) => _bridge.RequestAsync(command, payload),
            SignOutWhenWritesFinish,
            _writeGate,
            OnWorkChanged);
        _ = _management.EnsureLoadedAsync();
        return _management;
    }

    private FrameworkElement CreateProfilePage()
    {
        _profile ??= new ProfileView(L,
            (command, payload) => _bridge.RequestAsync(command, payload),
            () => _user,
            _writeGate,
            () => WindowNative.GetWindowHandle(this),
            OnAvatarChanged,
            SignOutWhenWritesFinish,
            SignOutWhenWritesFinish);
        _ = _profile.EnsureLoadedAsync();
        return _profile;
    }

    private FrameworkElement CreateMaintenancePage()
    {
        _maintenance ??= new MaintenanceView(L, ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            () => _user,
            SignOutWhenWritesFinish,
            UpdateBackButton);
        _ = _maintenance.EnsureLoadedAsync();
        return _maintenance;
    }

    private FrameworkElement CreateAdministrationPage()
    {
        _administration ??= new AdministrationView(L, ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            SignOutWhenWritesFinish,
            UpdateBackButton,
            () => _user,
            _writeGate,
            OnWorkChanged);
        _ = _administration.EnsureLoadedAsync();
        return _administration;
    }

    /// <summary>Opens a project's publish form from the Dashboard (list and project underneath for Back).</summary>
    private void OpenPublish(string projectId, string name)
    {
        NavigateTo(NativePage.Projects);
        _projects?.OpenPublish(projectId, name);
    }

    /// <summary>A write changed reservations or commits: other pages reload when shown again.</summary>
    private void OnWorkChanged()
    {
        _dashboard?.MarkStale();
        _projects?.MarkStale();
        _commitHistory?.MarkStale();
        _management?.MarkStale();
        _administration?.MarkStale();
    }

    /// <summary>Opens a project overview from another page (the list stays underneath for Back).</summary>
    private void OpenProject(string projectId, string name)
    {
        NavigateTo(NativePage.Projects);
        _projects?.OpenProject(projectId, name);
    }

    private FrameworkElement CreateSettingsPage()
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

    private FrameworkElement CreateVersionInfoPage()
    {
        var info = RuntimeVersionInfo.GetSnapshot();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = L("Page_Version info"), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = L("VersionInfoDescription"), TextWrapping = TextWrapping.Wrap });
        AddVersionRow(panel, "VersionLabel", info.DisplayVersion);
        AddVersionRow(panel, "BetaLabel", L("Beta"));
        AddVersionRow(panel, "FrameworkLabel", info.Framework);
        AddVersionRow(panel, "WindowsAppSdkLabel", info.WindowsAppSdkVersion);
        AddVersionRow(panel, "WebViewLabel", info.WebViewDescription);
        AddVersionRow(panel, "ArchitectureLabel", info.Architecture);
        AddVersionRow(panel, "PackageIdentityLabel", info.PackageIdentity);
        _bridgeStatusText = AddVersionRow(panel, "BridgeLabel", BridgeStatusText());
        scroll.Content = panel;
        return scroll;
    }

    private TextBlock AddVersionRow(StackPanel panel, string labelKey, string value)
    {
        var row = new StackPanel { Spacing = 2 };
        var valueText = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        row.Children.Add(new TextBlock { Text = L(labelKey), Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        row.Children.Add(valueText);
        panel.Children.Add(row);
        return valueText;
    }

    private static RadioButton? FindRadioButton(RadioButtons buttons, string tag) =>
        buttons.Items.OfType<RadioButton>().FirstOrDefault(button => string.Equals(button.Tag as string, tag, StringComparison.Ordinal));

    private string ReadLanguage()
    {
        try { return SettingsPolicy.NormalizeLanguage(ApplicationData.Current.LocalSettings.Values[SettingsPolicy.LanguageKey] as string); }
        catch { return SettingsPolicy.DefaultLanguage; }
    }

    private string ReadTheme()
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
        if (PersistSetting(SettingsPolicy.ThemeKey, theme, restartRequired: false)) ApplySavedTheme();
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

    /// <summary>
    /// Selects the page's pane item; a page without a visible item (account-menu and title-bar pages) clears the
    /// selection, so clicking the previous page in the pane still navigates back to it.
    /// </summary>
    private void SyncNavigationSelection(NativePage page)
    {
        var match = Navigation.MenuItems.OfType<NavigationViewItem>().Concat(Navigation.FooterMenuItems.OfType<NavigationViewItem>())
            .FirstOrDefault(item => item.Visibility == Visibility.Visible && TryParsePage(item.Tag as string, out var itemPage) && itemPage == page);
        _syncingNavigationSelection = true;
        try
        {
            Navigation.SelectedItem = match;
        }
        finally
        {
            _syncingNavigationSelection = false;
        }
    }

    private void PageSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var query = sender.Text.Trim();
        _searchSuggestions.Clear();
        if (query.Length == 0) return;
        foreach (var page in Enum.GetValues<NativePage>())
        {
            if (!IsPageAvailable(page)) continue;
            var name = NativePageCatalog.SearchKey(page);
            if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || LocalizedValue("Page", name).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                _searchSuggestions.Add(LocalizedValue("Page", name));
            }
        }
    }

    private void PageSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.ChosenSuggestion as string ?? sender.Text.Trim();
        // Nullable, so "no available match" is not mistaken for the first enum value (Dashboard).
        var page = Enum.GetValues<NativePage>().Cast<NativePage?>().FirstOrDefault(candidate =>
        {
            if (candidate is not { } value || !IsPageAvailable(value)) return false;
            var key = NativePageCatalog.SearchKey(value);
            return key.Equals(query, StringComparison.CurrentCultureIgnoreCase)
                || LocalizedValue("Page", key).Equals(query, StringComparison.CurrentCultureIgnoreCase);
        });
        if (page is { } match)
        {
            NavigateTo(match);
            sender.Text = string.Empty;
        }
    }

    private void NotificationsButton_Click(object sender, RoutedEventArgs e) => ShowHeaderFlyout(NotificationsFlyout, NotificationsButton);
    private void ProfileButton_Click(object sender, RoutedEventArgs e) => ShowHeaderFlyout(AccountFlyout, ProfileButton);

    private void ShowHeaderFlyout(Flyout flyout, FrameworkElement target)
    {
        // Set before showing: the Button's own flyout opening reads the same placement.
        flyout.Placement = HeaderFlyoutOpensAbove(flyout, target)
            ? Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight
            : Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight;
        flyout.ShowAt(target);
    }

    private bool HeaderFlyoutOpensAbove(Flyout flyout, FrameworkElement target)
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
            var hwnd = WindowNative.GetWindowHandle(this);
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
    private void ProfileSettings_Click(object sender, RoutedEventArgs e) { AccountFlyout.Hide(); NavigateTo(NativePage.ProfileSettings); }
    private void Administration_Click(object sender, RoutedEventArgs e) { AccountFlyout.Hide(); if (NativePageCatalog.CanAdminister(_user)) NavigateTo(NativePage.Administration); }
    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        await SignOutAsync();
    }

    private async Task SignOutAsync()
    {
        if (_signingOut) return;
        // Signing out now would drop the answer of a write that is still being sent; sign out after it finishes.
        if (_writeGate.InFlight)
        {
            await ShowSignOutBlockedAsync();
            return;
        }
        _signingOut = true;
        // Revoke the server session first when the bridge is up; local sign-in data is cleared either way.
        if (_bridge.State == BridgeConnectionState.Connected) await _bridge.RequestAsync("logout");
        App.RequestSignOut();
        _signingOut = false;
    }

    /// <summary>
    /// The session already ended on the server (a page's "Sign in again", or a password change that was applied or
    /// unconfirmed), so this sign-out is never refused: when another write is still being sent, it runs as soon as the
    /// write gate is released.
    /// </summary>
    private void SignOutWhenWritesFinish()
    {
        if (_writeGate.InFlight)
        {
            if (_signOutWhenGateFree) return;
            _signOutWhenGateFree = true;
            _writeGate.Released += SignOutWhenGateFree;
            return;
        }
        _ = SignOutAsync();
    }

    private void SignOutWhenGateFree()
    {
        if (_writeGate.InFlight) return;
        _writeGate.Released -= SignOutWhenGateFree;
        _signOutWhenGateFree = false;
        // Released is raised inside the finishing write's finally; sign out after it has returned, deferring again if
        // another write took the gate in between.
        if (!DispatcherQueue.TryEnqueue(SignOutWhenWritesFinish)) SignOutWhenWritesFinish();
    }

    private async Task ShowSignOutBlockedAsync()
    {
        if (RootGrid.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title = L("Work_BusyTitle"),
            Content = new TextBlock { Text = L("Shell_SignOutBlocked"), TextWrapping = TextWrapping.Wrap },
            CloseButtonText = L("Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Style = PageParts.Res("DefaultContentDialogStyle")
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch
        {
            // Another dialog is already open; sign-out stays refused either way.
        }
    }

    private string LocalizedValue(string prefix, string value)
    {
        var localized = L(prefix + "_" + value);
        return string.IsNullOrEmpty(localized) ? value : localized;
    }

    private bool IsPageAvailable(NativePage page) => NativePageCatalog.IsAvailable(page, _user);

    private static bool TryParsePage(string? tag, out NativePage page) =>
        Enum.TryParse(tag, ignoreCase: false, out page);
}
