using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MolHub.Windows;

/// <summary>
/// Read-only Server maintenance page for every signed-in account: the current maintenance state (`maintenance`)
/// and the announcements published when a maintenance ended (`announcements`), with "Load more" and one
/// announcement's details on the page's own stack. Starting or completing maintenance is not rendered until the
/// admin writes exist natively.
/// </summary>
internal sealed class MaintenanceView : UserControl, IScreenStack
{
    private const string RefreshGlyph = "\uE72C";
    private const string BackGlyph = "\uE72B";
    private const string RepairGlyph = "\uE90F";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private readonly PageParts _p;
    private readonly string _language;
    private readonly Func<string, JsonObject?, Task<BridgeResult>> _request;
    private readonly Func<AuthenticatedUser> _user;
    private readonly Action _signInAgain;
    private readonly Action _navigationChanged;
    private readonly ScrollViewer _scroll;
    private readonly InfoBar _statusBar = new() { IsClosable = true, IsOpen = false };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl _state = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly List<Announcement> _items = [];
    private readonly StackPanel _rows = new() { Spacing = 12 };
    private readonly FrameworkElement _list;
    private readonly Button _refresh;
    private readonly Button _loadMore;
    private readonly TextBlock _section;
    private Announcement? _detail;
    // A failed state read keeps its message until a later state read succeeds (the card stays hidden meanwhile).
    private MaintenanceError? _stateError;
    private double _listOffset;
    private int? _next;
    private DateTimeOffset _loadedAt;
    private bool _loaded;
    private int _generation;

    public MaintenanceView(Func<string, string> localize, string language, Func<string, JsonObject?, Task<BridgeResult>> request,
        Func<AuthenticatedUser> user, Action signInAgain, Action navigationChanged)
    {
        _p = new PageParts(localize);
        _language = language;
        _request = request;
        _user = user;
        _signInAgain = signInAgain;
        _navigationChanged = navigationChanged;

        _refresh = _p.SubtleButton(L("Dashboard_Refresh"), RefreshGlyph, () => _ = LoadAsync(append: false));
        _loadMore = new Button { Content = L("Projects_LoadMore"), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
        _loadMore.Click += (_, _) => _ = LoadAsync(append: true);
        var list = new StackPanel { Spacing = 16 };
        list.Children.Add(TitleRow(L("Page_Server maintenance"), _refresh));
        list.Children.Add(_state);
        _section = PageParts.Heading(L("Page_Announcements"), AutomationHeadingLevel.Level2, "SubtitleTextBlockStyle");
        _section.Margin = new Thickness(0, 8, 0, 0);
        list.Children.Add(_section);
        list.Children.Add(_rows);
        list.Children.Add(_loadMore);
        _list = list;

        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32, 28, 32, 32) };
        root.Children.Add(_statusBar);
        root.Children.Add(_body);
        _scroll = PageParts.CenteredPage(root, 960);
        Content = _scroll;
    }

    public bool CanGoBack => _detail is not null;

    private string L(string key) => _p.L(key);

    /// <summary>Loads on first display and again when the page is older than a minute.</summary>
    public Task EnsureLoadedAsync()
    {
        if (_detail is null) _body.Content = _loaded ? _list : _p.LoadingIndicator("Maintenance_Loading");
        return !_loaded || DateTimeOffset.Now - _loadedAt > StaleAfter ? LoadAsync(append: false) : Task.CompletedTask;
    }

    /// <summary>Returns from an announcement's details to the list (title-bar Back, Alt+Left or the Back button).</summary>
    public bool TryGoBack()
    {
        if (_detail is null) return false;
        _detail = null;
        _body.Content = _loaded ? _list : _p.LoadingIndicator("Maintenance_Loading");
        var offset = _listOffset;
        _body.UpdateLayout();
        _scroll.ChangeView(null, offset, null, true);
        _navigationChanged();
        if (_stateError is { } stateError) ShowError(stateError);
        if (!_loaded) _ = LoadAsync(append: false);
        return true;
    }

    private void OpenDetail(Announcement announcement)
    {
        _listOffset = _scroll.VerticalOffset;
        _detail = announcement;
        _statusBar.IsOpen = false;
        _body.Content = BuildDetail(announcement);
        _scroll.ChangeView(null, 0, null, true);
        _navigationChanged();
    }

    /// <summary>A full load reads the state and the first announcement page together; "Load more" reads the next page only.</summary>
    private async Task LoadAsync(bool append)
    {
        var generation = ++_generation;
        _refresh.IsEnabled = false;
        _loadMore.IsEnabled = false;
        var stateTask = append ? null : _request("maintenance", null);
        var listResult = await _request("announcements", MaintenanceModel.ListPayload(append ? _next ?? 0 : 0));
        var stateResult = stateTask is null ? null : await stateTask;
        if (generation != _generation) return;
        _refresh.IsEnabled = true;
        _loadMore.IsEnabled = true;

        var page = listResult.Ok && listResult.Data is { } data ? MaintenanceModel.ParseList(data, listResult.Meta) : null;
        MaintenanceState? state = null;
        if (stateResult is not null)
        {
            state = stateResult.Ok && stateResult.Data is { } stateData ? MaintenanceModel.ParseState(stateData) : null;
            // A failed state read hides the state card rather than showing an old one.
            _state.Content = state is null ? null : StateCard(state);
            _stateError = state is not null ? null : stateResult.Ok ? MaintenanceError.Unexpected : MaintenanceModel.ErrorFor(stateResult);
        }

        if (page is null)
        {
            var error = listResult.Ok ? MaintenanceError.Unexpected : MaintenanceModel.ErrorFor(listResult);
            if (_detail is null)
            {
                ShowError(error);
                if (!_loaded)
                {
                    // Keep whatever state did load. Rejected and suspended accounts may read the state but not the
                    // announcements, so that section is hidden for them instead of offering a retry that cannot work.
                    _rows.Children.Clear();
                    _loadMore.Visibility = Visibility.Collapsed;
                    _section.Visibility = error == MaintenanceError.NotApproved ? Visibility.Collapsed : Visibility.Visible;
                    _body.Content = _list;
                }
            }
            return;
        }

        _section.Visibility = Visibility.Visible;
        if (_stateError is { } stateError)
        {
            if (_detail is null) ShowError(stateError);
        }
        else
        {
            _statusBar.IsOpen = false;
        }
        if (!append) _items.Clear();
        foreach (var item in page.Items)
        {
            if (!_items.Any(existing => existing.Id == item.Id)) _items.Add(item);
        }
        _next = page.NextOffset;
        _loaded = true;
        _loadedAt = DateTimeOffset.Now;
        RenderRows();
        if (_detail is null) _body.Content = _list;
    }

    private void RenderRows()
    {
        _rows.Children.Clear();
        if (_items.Count == 0) _rows.Children.Add(_p.Secondary(L("Announcements_None")));
        foreach (var item in _items) _rows.Children.Add(AnnouncementCard.Row(item, L, _language, () => OpenDetail(item)));
        _loadMore.Visibility = _next is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(MaintenanceError error)
    {
        var (severity, titleKey, messageKey) = error switch
        {
            MaintenanceError.Connection => (InfoBarSeverity.Error, "Dashboard_ErrorConnectionTitle", "Dashboard_ErrorConnection"),
            MaintenanceError.SessionEnded => (InfoBarSeverity.Warning, "Dashboard_ErrorSessionTitle", "Maintenance_ErrorSession"),
            MaintenanceError.NotApproved => (InfoBarSeverity.Warning, "Maintenance_ErrorUnexpectedTitle", "Announcements_ErrorNotApproved"),
            MaintenanceError.RateLimited => (InfoBarSeverity.Warning, "Dashboard_ErrorRateLimitTitle", "Dashboard_ErrorRateLimit"),
            _ => (InfoBarSeverity.Error, "Maintenance_ErrorUnexpectedTitle", "Dashboard_ErrorUnexpected")
        };
        _statusBar.Severity = severity;
        _statusBar.Title = L(titleKey);
        _statusBar.Message = L(messageKey);
        Button? action = null;
        if (error == MaintenanceError.SessionEnded)
        {
            action = new Button { Content = L("Dashboard_SignInAgain") };
            action.Click += (_, _) => _signInAgain();
        }
        else if (error != MaintenanceError.NotApproved)
        {
            // Retrying cannot help an account the server refuses (rejected or suspended).
            action = new Button { Content = L("Retry") };
            action.Click += (_, _) => _ = LoadAsync(append: false);
        }
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
    }

    /// <summary>The web wording: "Maintenance active" with the workspace message (and, for members, the lock note).</summary>
    private Border StateCard(MaintenanceState state)
    {
        var body = new StackPanel { Spacing = 8, Padding = new Thickness(16, 12, 16, 16) };
        if (!state.Available)
        {
            body.Children.Add(_p.Secondary(L("Maintenance_Unavailable")));
            return _p.Card("Maintenance_Status", RepairGlyph, body);
        }

        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Style = PageParts.Res(state.Active ? "ProjectStatusWorkingDotStyle" : "ProjectStatusAvailableDotStyle")
        });
        label.Children.Add(new TextBlock
        {
            Text = L(state.Active ? "Maintenance_Active" : "Maintenance_Inactive"),
            Style = PageParts.Res(state.Active ? "ProjectStatusWorkingTextStyle" : "ProjectStatusAvailableTextStyle")
        });
        body.Children.Add(label);
        if (state.Active)
        {
            body.Children.Add(new TextBlock { Text = L("Maintenance_ActiveMessage"), TextWrapping = TextWrapping.Wrap });
            if (MaintenanceModel.ShowsLockNote(_user())) body.Children.Add(_p.Secondary(L("Maintenance_Locked")));
            var started = DashboardModel.FormatDate(state.StartedAt, _language);
            if (started.Length > 0) body.Children.Add(PageParts.Caption(string.Format(L("Maintenance_StartedFormat"), started)));
        }
        return _p.Card("Maintenance_Status", RepairGlyph, body);
    }

    private static Grid TitleRow(string text, FrameworkElement action)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(PageParts.Heading(text, AutomationHeadingLevel.Level1, "TitleTextBlockStyle"));
        Grid.SetColumn(action, 1);
        row.Children.Add(action);
        return row;
    }

    private FrameworkElement BuildDetail(Announcement item)
    {
        var back = _p.SubtleButton(L("Announcements_Back"), BackGlyph, () => TryGoBack());
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(TitleRow(L("Announcements_DetailTitle"), back));

        panel.Children.Add(AnnouncementCard.Detail(item, L, _language));
        return panel;
    }
}
