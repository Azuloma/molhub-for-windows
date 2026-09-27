using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace FusionLedger.Windows;

/// <summary>
/// Read-only Project management (`manageProjects`, `manageProject`) for approved project owners and site admins,
/// following the web manage panel: a project picker, then the project's state and administrator, members, work
/// reservation, approval requests, Discord destinations and activity. Adding or removing members, releasing
/// reservations, requests and Discord changes are not rendered until those writes exist natively.
/// </summary>
internal sealed class ManagementView : UserControl
{
    private const string RefreshGlyph = "\uE72C";
    private const string ProjectGlyph = "\uE8B7";
    private const string PeopleGlyph = "\uE716";
    private const string ClockGlyph = "\uE823";
    private const string RequestGlyph = "\uE9D5";
    private const string LinkGlyph = "\uE71B";
    private const string HistoryGlyph = "\uE81C";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private readonly PageParts _p;
    private readonly string _language;
    private readonly Func<string, JsonObject?, Task<BridgeResult>> _request;
    private readonly Action _signInAgain;
    private readonly InfoBar _statusBar = new() { IsClosable = true, IsOpen = false };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl _detail = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _picker = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _limitNote;
    private readonly FrameworkElement _content;
    private readonly Button _refresh;
    private IReadOnlyList<ManagedProject> _projects = [];
    private string? _selected;
    private bool _updatingPicker;
    private bool _loaded;
    private DateTimeOffset _loadedAt;
    private int _generation;

    public ManagementView(Func<string, string> localize, string language, Func<string, JsonObject?, Task<BridgeResult>> request, Action signInAgain)
    {
        _p = new PageParts(localize);
        _language = language;
        _request = request;
        _signInAgain = signInAgain;

        _refresh = _p.SubtleButton(L("Dashboard_Refresh"), RefreshGlyph, () => _ = LoadAsync());
        var title = new Grid { ColumnSpacing = 12 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(PageParts.Heading(L("Page_Project management"), AutomationHeadingLevel.Level1, "TitleTextBlockStyle"));
        Grid.SetColumn(_refresh, 1);
        title.Children.Add(_refresh);

        _picker.Header = L("History_ProjectFilter");
        AutomationProperties.SetName(_picker, L("History_ProjectFilter"));
        _picker.SelectionChanged += (_, _) =>
        {
            if (_updatingPicker || _picker.SelectedItem is not ComboBoxItem { Tag: string id } || id == _selected) return;
            _selected = id;
            _ = LoadDetailAsync(++_generation);
        };
        _limitNote = PageParts.Caption(L("Manage_FirstProjectsOnly"));
        _limitNote.Visibility = Visibility.Collapsed;

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(_picker);
        content.Children.Add(_limitNote);
        content.Children.Add(_detail);
        _content = content;

        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32, 28, 32, 32), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(title);
        root.Children.Add(_p.Secondary(L("Manage_Intro")));
        root.Children.Add(PageParts.Caption(L("Manage_ReadOnlyNote")));
        root.Children.Add(_statusBar);
        root.Children.Add(_body);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
    }

    private string L(string key) => _p.L(key);

    /// <summary>Loads on first display and again when the data is older than a minute (the chosen project is kept).</summary>
    public Task EnsureLoadedAsync() =>
        !_loaded || DateTimeOffset.Now - _loadedAt > StaleAfter ? LoadAsync() : Task.CompletedTask;

    private async Task LoadAsync()
    {
        var generation = ++_generation;
        if (!_loaded) _body.Content = _p.LoadingIndicator("Manage_Loading");
        _refresh.IsEnabled = false;
        var result = await _request("manageProjects", ManagementModel.ListPayload());
        if (generation != _generation) return;

        var list = result.Ok && result.Data is { } data ? ManagementModel.ParseList(data, result.Meta) : null;
        if (list is null)
        {
            _refresh.IsEnabled = true;
            ShowError(result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result));
            if (!_loaded) _body.Content = null;
            return;
        }

        _projects = list.Items;
        _loaded = true;
        _loadedAt = DateTimeOffset.Now;
        _limitNote.Visibility = list.NextOffset is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_projects.Count == 0)
        {
            // Ownership can also come from deleted projects only on the server side; the list decides.
            _refresh.IsEnabled = true;
            _statusBar.IsOpen = false;
            _selected = null;
            _body.Content = _p.Secondary(L("Manage_None"));
            return;
        }

        _selected = ManagementModel.Select(_projects, _selected);
        FillPicker();
        _body.Content = _content;
        await LoadDetailAsync(generation);
    }

    private void FillPicker()
    {
        _updatingPicker = true;
        try
        {
            _picker.Items.Clear();
            foreach (var project in _projects)
            {
                var text = project.Deleted ? $"{project.Name} · {L("Manage_Deleted")}" : project.Name;
                var item = new ComboBoxItem { Content = text, Tag = project.Id };
                AutomationProperties.SetName(item, text);
                _picker.Items.Add(item);
                if (project.Id == _selected) _picker.SelectedItem = item;
            }
        }
        finally
        {
            _updatingPicker = false;
        }
    }

    private async Task LoadDetailAsync(int generation)
    {
        if (_selected is not { } projectId || ManagementModel.DetailPayload(projectId) is not { } payload) return;
        // The picker stays enabled (keyboard focus stays on it); the generation counter drops superseded replies.
        _refresh.IsEnabled = false;
        _detail.Content = _p.LoadingIndicator("Manage_Loading");
        var result = await _request("manageProject", payload);
        if (generation != _generation) return;
        _refresh.IsEnabled = true;

        var detail = result.Ok && result.Data is { } data ? ManagementModel.ParseDetail(data) : null;
        if (detail is null)
        {
            ShowError(result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result));
            _detail.Content = null;
            return;
        }
        _statusBar.IsOpen = false;
        _detail.Content = BuildDetail(detail);
    }

    private void ShowError(ProjectsError error)
    {
        var (severity, titleKey, messageKey) = error switch
        {
            ProjectsError.Connection => (InfoBarSeverity.Error, "Dashboard_ErrorConnectionTitle", "Dashboard_ErrorConnection"),
            ProjectsError.SessionEnded => (InfoBarSeverity.Warning, "Dashboard_ErrorSessionTitle", "Manage_ErrorSession"),
            ProjectsError.PendingApproval => (InfoBarSeverity.Informational, "Dashboard_ErrorPendingTitle", "Dashboard_ErrorPending"),
            ProjectsError.Maintenance => (InfoBarSeverity.Warning, "Dashboard_ErrorMaintenanceTitle", "Dashboard_ErrorMaintenance"),
            ProjectsError.RateLimited => (InfoBarSeverity.Warning, "Dashboard_ErrorRateLimitTitle", "Dashboard_ErrorRateLimit"),
            ProjectsError.NoAccess => (InfoBarSeverity.Warning, "Projects_ErrorNoAccessTitle", "Manage_ErrorNoAccess"),
            _ => (InfoBarSeverity.Error, "Manage_ErrorUnexpectedTitle", "Dashboard_ErrorUnexpected")
        };
        _statusBar.Severity = severity;
        _statusBar.Title = L(titleKey);
        _statusBar.Message = L(messageKey);
        var action = new Button();
        if (error == ProjectsError.SessionEnded)
        {
            action.Content = L("Dashboard_SignInAgain");
            action.Click += (_, _) => _signInAgain();
        }
        else
        {
            // No access usually means the project list changed; reloading the list picks a project that is still managed.
            action.Content = L(error == ProjectsError.NoAccess ? "Dashboard_Refresh" : "Retry");
            action.Click += (_, _) => _ = LoadAsync();
        }
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
    }

    private FrameworkElement BuildDetail(ManagedProjectDetail detail)
    {
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(GeneralCard(detail));
        panel.Children.Add(MembersCard(detail));
        panel.Children.Add(ReservationCard(detail));
        panel.Children.Add(RequestsCard(detail));
        panel.Children.Add(DiscordCard(detail));
        panel.Children.Add(ActivityCard(detail));
        return panel;
    }

    private static StackPanel CardBody() => new() { Spacing = 10, Padding = new Thickness(16, 12, 16, 16) };

    private TextBlock Empty() => _p.Secondary(L("Manage_Empty"));

    private string Label(string? key, string raw) => key is null ? raw : L(key);

    private string Date(DateTimeOffset? value) => DashboardModel.FormatDate(value, _language);

    /// <summary>"a · b · c" leaving out empty parts.</summary>
    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(part => part.Length > 0));

    private Border GeneralCard(ManagedProjectDetail detail)
    {
        var body = CardBody();
        var heading = PageParts.Heading(detail.Project.Name, AutomationHeadingLevel.Level3, "SubtitleTextBlockStyle");
        body.Children.Add(heading);
        var (kind, name) = ManagementModel.Owner(detail);
        var owner = kind switch
        {
            ManagedOwnerKind.Account => name,
            ManagedOwnerKind.SiteManaged => L("Manage_SiteManaged"),
            _ => L("Manage_OwnerUnknown")
        };
        body.Children.Add(_p.Secondary(Join(L(detail.Project.Deleted ? "Manage_Deleted" : "Manage_Active"),
            string.Format(L("Manage_OwnerFormat"), owner))));
        return _p.Card("History_ProjectFilter", ProjectGlyph, body);
    }

    private Border MembersCard(ManagedProjectDetail detail)
    {
        var body = CardBody();
        if (detail.Members.Count == 0) body.Children.Add(Empty());
        foreach (var member in detail.Members)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var avatar = PageParts.Avatar(member.Username, member.Avatar, 28);
            avatar.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(avatar);
            var text = new StackPanel { Spacing = 0 };
            text.Children.Add(new TextBlock { Text = member.Username, Style = PageParts.Res("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis });
            var role = member.Id == detail.Project.OwnerId ? L("Manage_Owner") : Label(ManagementModel.StatusKey(member.Status), member.Status);
            text.Children.Add(PageParts.Caption(role));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            AutomationProperties.SetName(row, Join(member.Username, role));
            body.Children.Add(row);
        }
        return _p.Card("Manage_Members", PeopleGlyph, body);
    }

    private Border ReservationCard(ManagedProjectDetail detail)
    {
        var body = CardBody();
        if (detail.Reservation is not { } reservation)
        {
            body.Children.Add(_p.Secondary(L("Manage_NoReservation")));
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(PageParts.Avatar(reservation.Username, reservation.Avatar, 28));
            row.Children.Add(new TextBlock { Text = Join(reservation.Username, Date(reservation.StartedAt)), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(row);
        }
        return _p.Card("Manage_Reservation", ClockGlyph, body);
    }

    private Border RequestsCard(ManagedProjectDetail detail)
    {
        var body = CardBody();
        if (detail.Requests.Count == 0) body.Children.Add(Empty());
        foreach (var item in detail.Requests)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(new TextBlock { Text = Label(ManagementModel.KindKey(item.Kind), item.Kind), Style = PageParts.Res("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
            if (item.Reason.Length > 0) row.Children.Add(new TextBlock { Text = item.Reason, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            row.Children.Add(PageParts.Caption(Join(item.Requester, Date(item.CreatedAt), Label(ManagementModel.StatusKey(item.Status), item.Status))));
            body.Children.Add(row);
        }
        return _p.Card("Manage_Requests", RequestGlyph, body);
    }

    private Border DiscordCard(ManagedProjectDetail detail)
    {
        var body = CardBody();
        // The web's disclosure: posted content is visible to the channel's viewers, even without site membership.
        body.Children.Add(_p.Secondary(L("Manage_DiscordHint")));
        if (!detail.DiscordConfigured) body.Children.Add(_p.Secondary(L("Manage_DiscordUnconfigured")));
        if (detail.Links.Count == 0) body.Children.Add(Empty());
        foreach (var link in detail.Links)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(new TextBlock { Text = link.ChannelName.Length > 0 ? "#" + link.ChannelName : link.ChannelId, Style = PageParts.Res("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(PageParts.Caption(Join($"{link.GuildId} / {link.ChannelId}", L(link.Enabled ? "Manage_Active" : "Manage_Status_cancelled"))));
            body.Children.Add(row);
        }
        return _p.Card("Manage_Discord", LinkGlyph, body);
    }

    private Border ActivityCard(ManagedProjectDetail detail)
    {
        var body = CardBody();
        if (detail.Events.Count == 0) body.Children.Add(Empty());
        foreach (var item in detail.Events)
        {
            var row = new StackPanel { Spacing = 2 };
            var label = Label(ManagementModel.EventKey(item.Kind), item.Kind);
            var target = ManagementModel.TargetName(item, detail);
            row.Children.Add(new TextBlock { Text = target.Length > 0 ? $"{label}: {target}" : label, Style = PageParts.Res("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(PageParts.Caption(Join(item.Actor, Date(item.CreatedAt))));
            body.Children.Add(row);
        }
        return _p.Card("Manage_Activity", HistoryGlyph, body);
    }
}
