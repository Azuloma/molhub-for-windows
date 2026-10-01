using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Windowing;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace MolHub.Windows;

public sealed partial class MainWindow : Window
{
    // Replaced (status, role, project manager) when a pending account is found approved.
    private AuthenticatedUser _user;
    private readonly ResourceLoader _strings = ResourceLoader.GetForViewIndependentUse();
    private readonly ObservableCollection<string> _searchSuggestions = [];
    private readonly Stack<NativePage> _history = new();
    private readonly ThemeService _themeService = new();
    private readonly WebBridgeClient _bridge = new();
    // One write at a time for the whole app (Projects, Commit history, Project management and Profile settings).
    private readonly WriteGate _writeGate = new();
    // False while the window is deactivated or minimized: toasts are shown only then.
    private bool _windowActive = true;
    // Sign-out sequence and write waiting (see SignOutCoordinator).
    private readonly SignOutCoordinator _signOut;
    private NativePage _currentPage = NativePage.Dashboard;
    // Page creation, caching and data invalidation (see ShellPageHost).
    private readonly ShellPageHost _pageHost;
    private bool _syncingNavigationSelection;
    private bool _initialNavigationCompleted;
    // Notification center: polls only while the bridge is connected and an approved account with a known id is signed in.
    private readonly NotificationCenter _notificationCenter;

    public MainWindow(AuthenticatedUser user)
    {
        _user = user;
        _signOut = new SignOutCoordinator(
            _writeGate,
            _bridge,
            DispatcherQueue,
            L,
            () => RootGrid.XamlRoot,
            () => RootGrid.ActualTheme,
            SyncNotifications,
            () => _notificationCenter?.ClearToasts());
        _pageHost = new ShellPageHost(
            L,
            () => _user,
            () => _currentPage,
            _bridge,
            _writeGate,
            () => _signOut.SignOutWhenWritesFinish(),
            UpdateBackButton,
            page => NavigateTo(page),
            OnApproved,
            UpdateUser,
            () => WindowNative.GetWindowHandle(this),
            OnAvatarChanged,
            ApplySavedTheme);
        InitializeComponent();
        _notificationCenter = new NotificationCenter(
            DispatcherQueue,
            (command, payload) => _bridge.RequestAsync(command, payload),
            L,
            NotificationsBadge,
            NotificationsButton,
            NotificationsFlyout,
            NotificationsStatus,
            NotificationsList,
            () => _signOut.IsSigningOut,
            () => _signOut.IsSignOutRequested,
            () => _windowActive,
            () => _bridge.State == BridgeConnectionState.Connected,
            () => IsPageAvailable(NativePage.Projects),
            () => _user.Id,
            _pageHost.OpenProject,
            _pageHost.OpenCommit,
            () => ShowHeaderFlyout(NotificationsFlyout, NotificationsButton),
            RestoreAndActivateWindow,
            _pageHost.ReloadCurrentPageAfterReset);
        Activated += MainWindow_Activated;
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
        _themeService.Apply(RootGrid, SettingsView.ReadTheme());
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _themeService.Dispose();
        _notificationCenter.Shutdown();
        Activated -= MainWindow_Activated;
        _bridge.StateChanged -= Bridge_StateChanged;
        _bridge.Dispose();
    }

    /// <summary>
    /// Keeps the bridge status row in step with the connection: a failure shows its diagnostic, and every (re)connection
    /// confirms the server session behind it. A failed bridge reconnects on the next request (for example Retry).
    /// </summary>
    private void Bridge_StateChanged(object? sender, EventArgs e)
    {
        SyncNotifications();
        switch (_bridge.State)
        {
            case BridgeConnectionState.Connecting:
                SetBridgeStatus("BridgeConnecting");
                break;
            case BridgeConnectionState.Unavailable:
                SetBridgeStatus("BridgeUnavailable", _bridge.LastError);
                break;
            case BridgeConnectionState.Connected:
                _ = CheckBridgeSessionAsync();
                break;
        }
    }

    /// <summary>Confirms the server session behind a newly connected bridge.</summary>
    private async Task CheckBridgeSessionAsync()
    {
        var session = await _bridge.RequestAsync("session");
        if (_bridge.State != BridgeConnectionState.Connected) return;
        if (!session.Ok || session.Data is not { } data)
        {
            SetBridgeStatus("BridgeUnavailable", $"session {session.Status} {session.ErrorCode}");
            return;
        }
        var signedIn = data.TryGetProperty("user", out var user) && user.ValueKind == System.Text.Json.JsonValueKind.Object;
        SetBridgeStatus(signedIn ? "BridgeConnected" : "BridgeSessionEnded");
    }

    private void SetBridgeStatus(string key, string? detail = null) => _pageHost.SetBridgeStatus(key, detail);

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
        _notificationCenter.RefreshUi();
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
        _bridge.StateChanged += Bridge_StateChanged;
        _ = _bridge.StartAsync();
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
        if (_signOut.IsSigningOut) return;
        UpdateUser(_user with { AvatarPng = avatar });
        ApplyHeaderAvatars();
        _pageHost.OnUserAvatarChanged();
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
        // A session read that lacks the account id must not stop the notification poller.
        _user = user with { Id = user.Id ?? _user.Id };
        UpdateAccountStatusText();
        ApplyPageAccess();
        SyncNotifications();
    }

    /// <summary>The approval screen found the same account approved: open the workspace without a new sign-in.</summary>
    private void OnApproved(AuthenticatedUser user)
    {
        // A reply that arrives after Sign out must not open the workspace in the closing window.
        if (_signOut.IsSigningOut) return;
        UpdateUser(user);
        _pageHost.ResetApproval();
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
        ContentFrame.Content = _pageHost.CreatePlaceholder(page);
        UpdateBackButton();
        SyncNavigationSelection(page);
    }

    /// <summary>Back is shown for nested pages and for an item opened inside a page's own screen stack.</summary>
    private void UpdateBackButton()
    {
        var stackNested = _pageHost.CurrentStackPage?.CanGoBack == true;
        AppTitleBar.IsBackButtonVisible = NativePageCatalog.IsNested(_currentPage) || stackNested;
        AppTitleBar.IsBackButtonEnabled = _history.Count > 0 || stackNested;
    }

    private void GoBack()
    {
        if (_pageHost.CurrentStackPage?.TryGoBack() == true) return;
        if (_history.Count == 0) return;
        var page = _history.Pop();
        NavigateTo(page, false);
    }

    // ----- Notification center (see NotificationCenter) -----

    private void SyncNotifications() => _notificationCenter.SyncNotifications();

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args) =>
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;

    /// <summary>A toast was clicked while the app runs (already validated by <see cref="NotificationToastPolicy"/>): the center opens the item, or the list when it is no longer known.</summary>
    internal void HandleToastActivation(ToastTarget target) => _notificationCenter.HandleToastActivation(target);

    private void RestoreAndActivateWindow()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        }
        catch
        {
            // Activate below still brings the window forward.
        }
        Activate();
    }

    private void NotificationsFlyout_Opened(object? sender, object e) => _notificationCenter.FlyoutOpened();

    private void NotificationsFlyout_Closed(object? sender, object e) => _notificationCenter.FlyoutClosed();

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

    private void ShowHeaderFlyout(Flyout flyout, FrameworkElement target) => HeaderFlyoutPlacement.Show(this, flyout, target);

    private void ProfileSettings_Click(object sender, RoutedEventArgs e) { AccountFlyout.Hide(); NavigateTo(NativePage.ProfileSettings); }
    private void Administration_Click(object sender, RoutedEventArgs e) { AccountFlyout.Hide(); if (NativePageCatalog.CanAdminister(_user)) NavigateTo(NativePage.Administration); }
    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        await _signOut.SignOutAsync();
    }

    /// <summary>The sign-out was abandoned (its login window was closed) and this window stays open: writes may start again.</summary>
    internal void SignOutAbandoned() => _signOut.SignOutAbandoned();

    private string LocalizedValue(string prefix, string value)
    {
        var localized = L(prefix + "_" + value);
        return string.IsNullOrEmpty(localized) ? value : localized;
    }

    private bool IsPageAvailable(NativePage page) => NativePageCatalog.IsAvailable(page, _user);

    private static bool TryParsePage(string? tag, out NativePage page) =>
        Enum.TryParse(tag, ignoreCase: false, out page);
}
