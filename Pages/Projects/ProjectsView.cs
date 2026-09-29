using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MolHub.Windows;

/// <summary>The first screen of a <see cref="ProjectsView"/>: the Projects page or the Commit history page.</summary>
internal enum ProjectsRoot
{
    List,
    History
}

/// <summary>
/// Native Projects: the project list (`projects`), a project overview with its commits (`project`,
/// `projectCommits`) and commit details (`commit`), navigated inside this page with a breadcrumb. It follows the web
/// project pages. The only writes are Start work / Cancel work / Publish version (`ProjectsView.Work.cs`); management actions are not rendered until they exist natively.
/// The Commit history page is a second instance whose first screen is the personal history (`history`).
/// </summary>
internal sealed partial class ProjectsView : UserControl, IScreenStack
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private readonly ProjectsRoot _root;
    private readonly PageParts _p;
    // The signed-in account as MainWindow knows it now (Profile settings can change the icon).
    private readonly Func<AuthenticatedUser> _currentUser;
    private readonly string _language;
    private readonly Func<string, JsonObject?, Task<BridgeResult>> _request;
    private readonly Action _signInAgain;
    private readonly Action _navigationChanged;
    private readonly Action _workChanged;
    private readonly WriteGate _gate;
    private readonly ScrollViewer _scroll;
    private readonly BreadcrumbBar _breadcrumb = new() { Visibility = Visibility.Collapsed };
    private readonly InfoBar _statusBar = new() { IsClosable = true, IsOpen = false };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly List<Screen> _stack = [];

    // Project list state.
    private readonly List<ProjectSummary> _projects = [];
    private string _query = string.Empty;
    private int _projectTotal;
    private int? _accessibleTotal;
    // Ticket, list generation, normalized query and next offset of the project list (see LoadState.cs).
    private readonly PagedLoadState<string> _list = new();
    private StackPanel? _projectRows;
    private Button? _loadMoreProjects;
    private PersonPicture? _selfAvatar;

    public ProjectsView(Func<string, string> localize, Func<AuthenticatedUser> user, string language,
        Func<string, JsonObject?, Task<BridgeResult>> request, Action signInAgain, Action navigationChanged,
        ProjectsRoot firstScreen = ProjectsRoot.List, Action? workChanged = null, WriteGate? writeGate = null)
    {
        _root = firstScreen;
        _p = new PageParts(localize);
        _currentUser = user;
        _language = language;
        _request = request;
        _signInAgain = signInAgain;
        _navigationChanged = navigationChanged;
        _workChanged = workChanged ?? (() => { });
        _gate = writeGate ?? new WriteGate();
        // A write warning or error stays until the user closes it (loading another screen does not hide it).
        _statusBar.Closed += (_, _) => _stickyNotice = false;

        AutomationProperties.SetName(_breadcrumb, L("Projects_Breadcrumb"));
        _breadcrumb.ItemClicked += (_, e) => PopTo(e.Index);
        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32, 20, 32, 32) };
        root.Children.Add(_breadcrumb);
        root.Children.Add(_statusBar);
        root.Children.Add(_body);
        _scroll = PageParts.CenteredPage(root, 1280);
        Content = _scroll;
        SizeChanged += (_, e) => Current?.Layout?.Apply(e.NewSize.Width);
    }

    private enum ScreenKind
    {
        List,
        History,
        Project,
        Commit,
        Publish
    }

    private sealed class Screen(ScreenKind kind, string id, string title)
    {
        public ScreenKind Kind { get; } = kind;
        public string Id { get; } = id;
        public string Title { get; set; } = title;
        public FrameworkElement? Content { get; set; }
        public TwoColumnLayout? Layout { get; set; }
        public double ScrollOffset { get; set; }
        public Func<Task>? Load { get; set; }
        /// <summary>Reloads data into the already built content (the history keeps its filters).</summary>
        public Func<Task>? Refresh { get; set; }
        /// <summary>
        /// Ticket, phase and freshness of this screen's data. A write elsewhere (Project management, another page) invalidates
        /// it, so a shown project reloads when shown again; an older reply that arrives later is dropped.
        /// </summary>
        public LoadState State { get; set; } = new();
        /// <summary>The commit timeline built on this screen (the Commits tab or the history), invalidated with the screen.</summary>
        public PagedLoadState<CommitFilter>? Timeline { get; set; }
    }

    private Screen? Current => _stack.Count > 0 ? _stack[^1] : null;

    private AuthenticatedUser _user => _currentUser();

    /// <summary>The signed-in account's icon changed (Profile settings): the profile column shows the new one at once.</summary>
    public void UserChanged()
    {
        if (_selfAvatar is null) return;
        if (_user.AvatarPng is { Length: > 0 } avatar) AvatarImage.Attach(_selfAvatar, avatar, 192);
        else AvatarImage.Clear(_selfAvatar);
    }

    public bool CanGoBack => _stack.Count > 1;

    private string L(string key) => _p.L(key);

    /// <summary>Shows the first screen on first display and refreshes it when it is older than a minute.</summary>
    public Task EnsureLoadedAsync()
    {
        if (_stack.Count == 0) _stack.Add(RootScreen());
        ShowCurrent();
        if (_stack.Count == 1 && Current is { Content: null, Load: { } load }) return load();
        return RefreshRootIfStale();
    }

    private Task RefreshRootIfStale()
    {
        var now = DateTimeOffset.Now;
        // An open project whose reservation may have changed on another page is reloaded when shown again (an invalidated
        // project has no loaded time; age alone never reloads it).
        if (Current is { Kind: ScreenKind.Project, Content: not null } project && project.State.NeedsLoad(now, TimeSpan.MaxValue)) return ReloadProjectAsync(project);
        if (_stack.Count != 1 || Current is not { Content: not null } root || !root.State.NeedsLoad(now, StaleAfter)) return Task.CompletedTask;
        if (root.Kind == ScreenKind.List) return LoadListAsync(append: false);
        return root.Refresh?.Invoke() ?? Task.CompletedTask;
    }

    public bool TryGoBack()
    {
        if (!CanGoBack) return false;
        PopTo(_stack.Count - 2);
        return true;
    }

    /// <summary>Opens a project directly (for example from the Dashboard), with the list underneath for Back.</summary>
    public void OpenProject(string projectId, string name)
    {
        if (!ProjectsModel.IsValidId(projectId)) return;
        if (_stack.Count == 0) _stack.Add(RootScreen());
        _stack.RemoveRange(1, _stack.Count - 1);
        PushProject(projectId, name);
    }

    private Screen RootScreen() => _root == ProjectsRoot.History ? HistoryScreen() : ListScreen();

    private Screen ListScreen() => new(ScreenKind.List, string.Empty, L("Page_Projects")) { Load = () => LoadListAsync(append: false), State = _list };

    private void Push(Screen screen)
    {
        if (Current is { } current) current.ScrollOffset = _scroll.VerticalOffset;
        _stack.Add(screen);
        ShowCurrent(scrollToTop: true);
        _ = screen.Load?.Invoke();
    }

    private void PopTo(int index)
    {
        if (index < 0 || index >= _stack.Count - 1) return;
        _stack.RemoveRange(index + 1, _stack.Count - index - 1);
        ShowCurrent();
        if (Current is { Content: null, Load: { } load }) _ = load();
        else _ = RefreshRootIfStale();
    }

    private void ShowCurrent(bool scrollToTop = false)
    {
        if (Current is not { } screen) return;
        CloseStatusBar();
        UpdateBreadcrumb();
        _body.Content = screen.Content ?? _p.LoadingIndicator("Projects_Loading");
        screen.Layout?.Apply(ActualWidth);
        var offset = scrollToTop ? 0 : screen.ScrollOffset;
        DispatcherQueue.TryEnqueue(() => _scroll.ChangeView(null, offset, null, true));
        _navigationChanged();
        ShowPendingWriteNotice();
    }

    private void UpdateBreadcrumb()
    {
        _breadcrumb.ItemsSource = _stack.Select(screen => screen.Title).ToList();
        _breadcrumb.Visibility = _stack.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts freshly built content on a screen and shows it only if that screen is still the current one.</summary>
    private void SetContent(Screen screen, FrameworkElement content, TwoColumnLayout? layout)
    {
        screen.Content = content;
        screen.Layout = layout;
        if (Current != screen) return;
        CloseStatusBar();
        _body.Content = content;
        layout?.Apply(ActualWidth);
    }

    /// <summary>Hides the InfoBar unless it shows a write warning or error the user has not dismissed yet.</summary>
    private void CloseStatusBar()
    {
        if (!_stickyNotice) _statusBar.IsOpen = false;
    }

    private void ShowError(Screen screen, ProjectsError error, Func<Task> retry)
    {
        if (Current != screen) return;
        _stickyNotice = false;
        var history = _root == ProjectsRoot.History;
        var (severity, titleKey, messageKey) = error switch
        {
            ProjectsError.Connection => (InfoBarSeverity.Error, "Dashboard_ErrorConnectionTitle", "Dashboard_ErrorConnection"),
            ProjectsError.SessionEnded => (InfoBarSeverity.Warning, "Dashboard_ErrorSessionTitle", history ? "History_ErrorSession" : "Projects_ErrorSession"),
            ProjectsError.PendingApproval => (InfoBarSeverity.Informational, "Dashboard_ErrorPendingTitle", "Dashboard_ErrorPending"),
            ProjectsError.Maintenance => (InfoBarSeverity.Warning, "Dashboard_ErrorMaintenanceTitle", "Dashboard_ErrorMaintenance"),
            ProjectsError.RateLimited => (InfoBarSeverity.Warning, "Dashboard_ErrorRateLimitTitle", "Dashboard_ErrorRateLimit"),
            ProjectsError.NoAccess => (InfoBarSeverity.Warning, "Projects_ErrorNoAccessTitle", history ? "History_ErrorNoAccess" : "Projects_ErrorNoAccess"),
            _ => (InfoBarSeverity.Error, history ? "History_ErrorUnexpectedTitle" : "Projects_ErrorUnexpectedTitle", "Dashboard_ErrorUnexpected")
        };
        var action = new Button();
        if (error == ProjectsError.SessionEnded)
        {
            action.Content = L("Dashboard_SignInAgain");
            action.Click += (_, _) => _signInAgain();
        }
        else if (error == ProjectsError.NoAccess && screen != _stack[0])
        {
            // Membership may have changed: go back to the refreshed first screen instead of retrying.
            action.Content = L(history ? "History_BackToHistory" : "Projects_BackToProjects");
            action.Click += (_, _) =>
            {
                PopTo(0);
                if (_stack[0].Kind == ScreenKind.List) _ = LoadListAsync(append: false);
                else if (_stack[0].Refresh is { } refresh) _ = refresh();
            };
        }
        else
        {
            action.Content = L("Retry");
            action.Click += async (_, _) => await retry();
        }
        _statusBar.Severity = severity;
        _statusBar.Title = L(titleKey);
        _statusBar.Message = L(messageKey);
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
        if (screen.Content is null) _body.Content = null;
    }

    private void ShowNotice(string titleKey, string messageKey)
    {
        _statusBar.Severity = InfoBarSeverity.Warning;
        _statusBar.Title = L(titleKey);
        _statusBar.Message = L(messageKey);
        _statusBar.ActionButton = null;
        _statusBar.IsOpen = true;
    }

    // ----- Project list -----

    private async Task LoadListAsync(bool append)
    {
        var screen = _stack.FirstOrDefault(s => s.Kind == ScreenKind.List);
        if (screen is null) return;
        PagedTicket<string> ticket;
        if (append)
        {
            // Not accepted while any load of the list runs or there is no next page: the list and the button stay as they are.
            if (_list.BeginMore() is not { } more) return;
            ticket = more;
        }
        else
        {
            ticket = _list.BeginFirst(_query);
        }
        UpdateLoadMore();

        var result = await _request("projects", ProjectsModel.ListPayload(ticket.Condition, ticket.Offset));
        // A newer refresh, search or Load more (or a changed list) owns the list and its button now.
        if (!(append ? _list.AcceptMore(ticket) : _list.AcceptFirst(ticket))) return;

        var page = result.Ok && result.Data is { } data ? ProjectsModel.ParseProjectList(data, result.Meta) : null;
        if (page is null)
        {
            _list.FailPage(ticket, DateTimeOffset.Now);
            UpdateLoadMore();
            ShowError(screen, result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result), () => LoadListAsync(append));
            return;
        }

        if (!append) _projects.Clear();
        var added = page.Projects.Where(project => _projects.All(existing => existing.Id != project.Id)).ToList();
        _projects.AddRange(added);
        _projectTotal = page.Total;
        _list.CompletePage(ticket, page.NextOffset, DateTimeOffset.Now);
        if (ticket.Condition.Length == 0) _accessibleTotal = page.Total;

        if (append && _projectRows is not null && screen.Content is not null)
        {
            foreach (var project in added) AddProjectRow(_projectRows, project);
            UpdateLoadMore();
            if (Current == screen) CloseStatusBar();
            return;
        }
        var (content, layout) = BuildListContent();
        SetContent(screen, content, layout);
    }

    private (FrameworkElement, TwoColumnLayout) BuildListContent()
    {
        var side = new StackPanel { Spacing = 12 };
        _selfAvatar = PageParts.Avatar(_user.Username, _user.AvatarPng, 96);
        side.Children.Add(_selfAvatar);
        var name = PageParts.Heading(_user.Username, AutomationHeadingLevel.None, "SubtitleTextBlockStyle");
        side.Children.Add(name);
        side.Children.Add(_p.Secondary(L(UserRoleKey())));
        var stats = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stats.Children.Add(PageParts.Divider());
        if (_accessibleTotal is { } accessible)
        {
            var row = new Grid { Padding = new Thickness(0, 10, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(_p.Secondary(L("Projects_AccessibleProjects")));
            var number = new TextBlock { Text = accessible.ToString(System.Globalization.CultureInfo.CurrentCulture), Style = PageParts.Res("BodyStrongTextBlockStyle") };
            Grid.SetColumn(number, 1);
            row.Children.Add(number);
            AutomationProperties.SetName(row, $"{L("Projects_AccessibleProjects")}: {accessible}");
            stats.Children.Add(row);
            stats.Children.Add(PageParts.Divider());
        }
        side.Children.Add(stats);

        var main = new StackPanel { Spacing = 12 };
        var search = new AutoSuggestBox
        {
            PlaceholderText = L("Projects_SearchPlaceholder"),
            QueryIcon = new SymbolIcon(Symbol.Find),
            Text = _query,
            MaxWidth = 10_000
        };
        AutomationProperties.SetName(search, L("Projects_SearchPlaceholder"));
        search.QuerySubmitted += (_, e) => ApplyQuery(e.QueryText);
        var toolbar = new Grid { ColumnSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(search);
        var refresh = _p.SubtleButton(L("Projects_Refresh"), "\uE72C", () => _ = LoadListAsync(append: false));
        refresh.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(refresh, 1);
        toolbar.Children.Add(refresh);
        main.Children.Add(toolbar);

        if (_query.Length > 0)
        {
            var results = new Grid { ColumnSpacing = 8 };
            results.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            results.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            results.Children.Add(_p.Secondary(string.Format(L("Projects_ResultsFormat"), _projectTotal, _query)));
            var clear = _p.LinkButton(L("Projects_ClearSearch"), () => ApplyQuery(string.Empty), new Thickness(0), glyph: null);
            Grid.SetColumn(clear, 1);
            results.Children.Add(clear);
            main.Children.Add(results);
        }

        _projectRows = new StackPanel();
        if (_projects.Count == 0)
        {
            _projectRows.Children.Add(_p.Secondary(L(_query.Length > 0 ? "Projects_NoResults" : "Projects_NoProjects")));
        }
        foreach (var project in _projects) AddProjectRow(_projectRows, project);
        main.Children.Add(_projectRows);

        _loadMoreProjects = new Button { Content = L("Projects_LoadMore"), HorizontalAlignment = HorizontalAlignment.Center };
        _loadMoreProjects.Click += async (_, _) => await LoadListAsync(append: true);
        main.Children.Add(_loadMoreProjects);
        UpdateLoadMore();

        var layout = new TwoColumnLayout(main, side, 240, sideOnLeft: true, sideFirstWhenStacked: true);
        return (layout.Element, layout);
    }

    private void UpdateLoadMore()
    {
        if (_loadMoreProjects is null) return;
        _loadMoreProjects.Visibility = _list.NextOffset is null ? Visibility.Collapsed : Visibility.Visible;
        _loadMoreProjects.IsEnabled = _list.CanLoadMore;
    }

    private void ApplyQuery(string text)
    {
        var query = ProjectsModel.NormalizeQuery(text);
        if (query == _query && Current?.Content is not null) return;
        _query = query;
        _ = LoadListAsync(append: false);
    }

    private string UserRoleKey() =>
        string.Equals(_user.Role, "admin", StringComparison.OrdinalIgnoreCase) ? "Projects_RoleSiteAdmin"
        : _user.ProjectManager ? "Projects_RoleOwner"
        : "Projects_RoleMember";

    private void AddProjectRow(StackPanel rows, ProjectSummary project)
    {
        if (rows.Children.Count > 0) rows.Children.Add(PageParts.Divider());
        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 16, 0, 16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var identity = new StackPanel { Spacing = 8 };
        var titleRow = new InlineWrapPanel();
        titleRow.Children.Add(ProjectLink(project.Id, project.Name, 16));
        titleRow.Children.Add(_p.PrivateBadge());
        identity.Children.Add(titleRow);
        identity.Children.Add(project.Description.Length > 0 ? _p.Secondary(project.Description) : _p.Secondary(L("Projects_NoDescription")));
        // "Last updated" moves to the next line when the row is narrow; the label and its version badge stay together.
        var meta = new InlineWrapPanel { HorizontalSpacing = 8 };
        if (project.Latest is { } latest)
        {
            var version = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            version.Children.Add(Centered(PageParts.Caption(L("Projects_LatestVersion"))));
            version.Children.Add(PageParts.VersionBadge(ProjectsModel.VersionLabel(latest.Version, latest.Id)));
            meta.Children.Add(version);
        }
        else
        {
            meta.Children.Add(Centered(PageParts.Caption(L("Projects_NoCommits"))));
        }
        var updated = DashboardModel.FormatDate(project.UpdatedAt, _language);
        if (updated.Length > 0) meta.Children.Add(Centered(PageParts.Caption($"{L("Projects_LastUpdated")} {updated}")));
        identity.Children.Add(meta);
        row.Children.Add(identity);

        var work = WorkStatus(project.Reservation, compact: true);
        work.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(work, 1);
        row.Children.Add(work);

        var open = new Button { VerticalAlignment = VerticalAlignment.Center };
        var openContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        openContent.Children.Add(new FontIcon { Glyph = "\uE72A", FontSize = 14, FontFamily = PageParts.SymbolFont });
        openContent.Children.Add(new TextBlock { Text = L("Projects_OpenProject") });
        open.Content = openContent;
        AutomationProperties.SetName(open, $"{L("Projects_OpenProject")}: {project.Name}");
        open.Click += (_, _) => PushProject(project.Id, project.Name);
        Grid.SetColumn(open, 2);
        row.Children.Add(open);
        rows.Children.Add(row);
    }

    /// <summary>Accent project name that opens the project overview.</summary>
    private HyperlinkButton ProjectLink(string projectId, string name, double fontSize)
    {
        var text = new TextBlock { Text = name, Style = PageParts.Res("DashboardAccentTextStyle"), FontSize = fontSize, TextWrapping = TextWrapping.Wrap };
        var link = new HyperlinkButton { Content = text, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(link, name);
        link.Click += (_, _) => PushProject(projectId, name);
        return link;
    }

    /// <summary>"Available for work" / "Working" with the web hint (or the holder) underneath.</summary>
    private StackPanel WorkStatus(ProjectReservation? reservation, bool compact)
    {
        var state = ProjectsModel.StateFor(reservation, _user.Username);
        var panel = new StackPanel { Spacing = 4 };
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var available = state == ReservationState.Available;
        status.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources[available ? "ProjectStatusAvailableDotStyle" : "ProjectStatusWorkingDotStyle"]
        });
        var statusText = L(available ? "Projects_Available" : "Projects_Working");
        status.Children.Add(new TextBlock { Text = statusText, Style = PageParts.Res(available ? "ProjectStatusAvailableTextStyle" : "ProjectStatusWorkingTextStyle") });
        panel.Children.Add(status);

        string hint;
        if (available)
        {
            hint = L("Projects_FreeHint");
        }
        else
        {
            var holder = state == ReservationState.Yours ? _user.Username : reservation?.Username ?? string.Empty;
            var since = DashboardModel.FormatDate(reservation?.StartedAt, _language);
            hint = since.Length > 0 ? string.Format(L("Projects_ReservedByFormat"), holder, since) : holder;
            if (!compact) hint += Environment.NewLine + L(state == ReservationState.Yours ? "Projects_OwnerHint" : "Projects_OtherHint");
        }
        panel.Children.Add(PageParts.Caption(hint));
        AutomationProperties.SetName(panel, $"{statusText}. {hint}");
        return panel;
    }

    private static FrameworkElement Centered(FrameworkElement element)
    {
        element.VerticalAlignment = VerticalAlignment.Center;
        return element;
    }
}
