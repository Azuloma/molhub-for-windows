using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MolHub.Windows;

/// <summary>
/// Creates and caches the native pages and tells them when data changed. MainWindow assigns the content that
/// <see cref="CreatePlaceholder"/> returns to <c>ContentFrame.Content</c> and keeps navigation, history and sign-out.
/// </summary>
internal sealed class ShellPageHost
{
    private readonly Func<string, string> L;
    private readonly Func<AuthenticatedUser> _getUser;
    private readonly Func<NativePage> _currentPage;
    private readonly WebBridgeClient _bridge;
    private readonly WriteGate _writeGate;
    private readonly Action _signOutWhenWritesFinish;
    private readonly Action _updateBackButton;
    private readonly Action<NativePage> _navigateTo;
    private readonly Action<AuthenticatedUser> _onApproved;
    private readonly Action<AuthenticatedUser> _updateUser;
    private readonly Func<IntPtr> _getWindowHandle;
    private readonly Action<byte[]?> _onAvatarChanged;
    private readonly SettingsView _settings;
    private readonly VersionInfoView _versionInfo;
    private DashboardView? _dashboard;
    private ProjectsView? _projects;
    private ProjectsView? _commitHistory;
    private ApprovalView? _approval;
    private MaintenanceView? _maintenance;
    private ManagementView? _management;
    private ProfileView? _profile;
    private AdministrationView? _administration;

    public ShellPageHost(
        Func<string, string> localize,
        Func<AuthenticatedUser> getUser,
        Func<NativePage> currentPage,
        WebBridgeClient bridge,
        WriteGate writeGate,
        Action signOutWhenWritesFinish,
        Action updateBackButton,
        Action<NativePage> navigateTo,
        Action<AuthenticatedUser> onApproved,
        Action<AuthenticatedUser> updateUser,
        Func<IntPtr> getWindowHandle,
        Action<byte[]?> onAvatarChanged,
        Action applySavedTheme)
    {
        L = localize;
        _getUser = getUser;
        _currentPage = currentPage;
        _bridge = bridge;
        _writeGate = writeGate;
        _signOutWhenWritesFinish = signOutWhenWritesFinish;
        _updateBackButton = updateBackButton;
        _navigateTo = navigateTo;
        _onApproved = onApproved;
        _updateUser = updateUser;
        _getWindowHandle = getWindowHandle;
        _onAvatarChanged = onAvatarChanged;
        _settings = new SettingsView(localize, applySavedTheme);
        _versionInfo = new VersionInfoView(localize);
    }

    public void SetBridgeStatus(string key, string? detail = null) => _versionInfo.SetBridgeStatus(key, detail);

    /// <summary>The page's own screen stack (Projects, Commit history, Server maintenance or Administration), which Back walks first.</summary>
    public IScreenStack? CurrentStackPage => _currentPage() switch
    {
        NativePage.Projects => _projects,
        NativePage.CommitHistory => _commitHistory,
        NativePage.ServerMaintenance => _maintenance,
        NativePage.Administration => _administration,
        _ => null
    };

    /// <summary>The approval screen is recreated after the account was found approved.</summary>
    public void ResetApproval() => _approval = null;

    public FrameworkElement CreatePlaceholder(NativePage page)
    {
        if (page == NativePage.Dashboard) return CreateDashboardPage();
        if (page == NativePage.Projects) return CreateProjectsPage();
        if (page == NativePage.CommitHistory) return CreateCommitHistoryPage();
        if (page == NativePage.AwaitingApproval) return CreateApprovalPage();
        if (page == NativePage.ServerMaintenance) return CreateMaintenancePage();
        if (page == NativePage.ProjectManagement) return CreateManagementPage();
        if (page == NativePage.ProfileSettings) return CreateProfilePage();
        if (page == NativePage.Administration) return CreateAdministrationPage();
        if (page == NativePage.AppSettings) return _settings.CreateSettingsPage();
        if (page == NativePage.VersionInfo) return _versionInfo.CreateVersionInfoPage();

        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = L("Page_" + NativePageCatalog.SearchKey(page)), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = L("NotConnected"), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private FrameworkElement CreateDashboardPage()
    {
        _dashboard ??= new DashboardView(L, _getUser(), SettingsView.ReadLanguage(),
            () => _bridge.RequestAsync("dashboard"),
            page => _navigateTo(page),
            OpenProject,
            OpenPublish,
            _signOutWhenWritesFinish);
        _ = _dashboard.EnsureLoadedAsync();
        return _dashboard;
    }

    private FrameworkElement CreateProjectsPage()
    {
        _projects ??= new ProjectsView(L, _getUser, SettingsView.ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            _signOutWhenWritesFinish,
            _updateBackButton,
            ProjectsRoot.List,
            OnWorkChanged,
            _writeGate);
        _ = _projects.EnsureLoadedAsync();
        return _projects;
    }

    private FrameworkElement CreateCommitHistoryPage()
    {
        _commitHistory ??= new ProjectsView(L, _getUser, SettingsView.ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            _signOutWhenWritesFinish,
            _updateBackButton,
            ProjectsRoot.History,
            OnWorkChanged,
            _writeGate);
        _ = _commitHistory.EnsureLoadedAsync();
        return _commitHistory;
    }

    private FrameworkElement CreateApprovalPage()
    {
        _approval ??= new ApprovalView(L, _getUser(),
            () => _bridge.RequestAsync("session"),
            _onApproved,
            _updateUser,
            // The web's "Announcements" button; the announcements live on the Server maintenance page.
            () => _navigateTo(NativePage.ServerMaintenance),
            _signOutWhenWritesFinish);
        return _approval;
    }

    private FrameworkElement CreateManagementPage()
    {
        _management ??= new ManagementView(L, SettingsView.ReadLanguage(), _getUser(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            _signOutWhenWritesFinish,
            _writeGate,
            OnWorkChanged);
        _ = _management.EnsureLoadedAsync();
        return _management;
    }

    private FrameworkElement CreateProfilePage()
    {
        _profile ??= new ProfileView(L,
            (command, payload) => _bridge.RequestAsync(command, payload),
            _getUser,
            _writeGate,
            _getWindowHandle,
            _onAvatarChanged,
            _signOutWhenWritesFinish,
            _signOutWhenWritesFinish);
        _ = _profile.EnsureLoadedAsync();
        return _profile;
    }

    private FrameworkElement CreateMaintenancePage()
    {
        _maintenance ??= new MaintenanceView(L, SettingsView.ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            _getUser,
            _signOutWhenWritesFinish,
            _updateBackButton);
        _ = _maintenance.EnsureLoadedAsync();
        return _maintenance;
    }

    private FrameworkElement CreateAdministrationPage()
    {
        _administration ??= new AdministrationView(L, SettingsView.ReadLanguage(),
            (command, payload) => _bridge.RequestAsync(command, payload),
            _signOutWhenWritesFinish,
            _updateBackButton,
            _getUser,
            _writeGate,
            OnWorkChanged);
        _ = _administration.EnsureLoadedAsync();
        return _administration;
    }

    /// <summary>Opens a project's publish form from the Dashboard (list and project underneath for Back).</summary>
    public void OpenPublish(string projectId, string name)
    {
        _navigateTo(NativePage.Projects);
        _projects?.OpenPublish(projectId, name);
    }

    /// <summary>A write changed reservations or commits: other pages reload when shown again.</summary>
    public void OnWorkChanged()
    {
        _dashboard?.MarkStale();
        _projects?.MarkStale();
        _commitHistory?.MarkStale();
        _management?.MarkStale();
        _administration?.MarkStale();
    }

    /// <summary>The account's avatar changed: pages that show it reload, and every data page is marked stale.</summary>
    public void OnUserAvatarChanged()
    {
        _projects?.UserChanged();
        _commitHistory?.UserChanged();
        _administration?.UserChanged();
        OnWorkChanged();
    }

    /// <summary>Opens a project overview from another page (the list stays underneath for Back).</summary>
    public void OpenProject(string projectId, string name)
    {
        _navigateTo(NativePage.Projects);
        _projects?.OpenProject(projectId, name);
    }

    /// <summary>Opens a commit from another place (a notification): Projects with the project underneath for Back.</summary>
    public void OpenCommit(string projectId, string name, string commitId)
    {
        _navigateTo(NativePage.Projects);
        _projects?.OpenCommit(projectId, name, commitId);
    }

    /// <summary>A server reset changed the data under the open page: mark every data page stale and reload the visible one.</summary>
    public void ReloadCurrentPageAfterReset()
    {
        OnWorkChanged();
        _ = _currentPage() switch
        {
            NativePage.Dashboard => _dashboard?.EnsureLoadedAsync(),
            NativePage.Projects => _projects?.EnsureLoadedAsync(),
            NativePage.CommitHistory => _commitHistory?.EnsureLoadedAsync(),
            NativePage.ProjectManagement => _management?.EnsureLoadedAsync(),
            NativePage.Administration => _administration?.EnsureLoadedAsync(),
            _ => null
        };
    }
}
