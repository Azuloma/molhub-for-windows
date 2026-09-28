using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace FusionLedger.Windows;

/// <summary>Display-only site administration, loading only the selected section through the read bridge commands.</summary>
internal sealed partial class AdministrationView : UserControl, IScreenStack
{
    private const string RefreshGlyph = "\uE72C";
    private static readonly string[] SectionGlyphs = ["\uE8A5", "\uE8B7", "\uE716", "\uE823", "\uE71B", "\uE90F", "\uE81C"];
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    private readonly PageParts _p;
    private readonly string _language;
    private readonly Func<string, JsonObject?, Task<BridgeResult>> _request;
    private readonly Action _signInAgain;
    private readonly InfoBar _status = new() { IsClosable = true, IsOpen = false };
    private readonly ContentControl _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ScrollViewer _scroll;
    private readonly ListView _sections = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false, VerticalAlignment = VerticalAlignment.Top };
    private readonly Button _refresh;
    private readonly Button _more = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, DateTimeOffset> _loadedAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _generations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int?> _next = new(StringComparer.Ordinal);
    private int _activeRequestGeneration;
    private readonly Dictionary<string, AdminPage<AdminRequest>> _requests = new(StringComparer.Ordinal);
    private AdminPage<AdminProject>? _projects;
    private AdminPage<AdminMember>? _members;
    private AdminDiscordData? _discord;
    private AdminMaintenance? _maintenance;
    private bool _maintenanceUnavailable;
    private bool _forbidden;
    private bool _loadFailed;
    private Announcement? _announcementDetail;
    private double _maintenanceOffset;
    private readonly Action _navigationChanged;
    private AdminPage<AdminAudit>? _audit;
    private string _selected = AdministrationModel.DefaultSection;

    public AdministrationView(Func<string, string> localize, string language,
        Func<string, JsonObject?, Task<BridgeResult>> request, Action signInAgain, Action navigationChanged)
    {
        _p = new PageParts(localize); _language = language; _request = request; _signInAgain = signInAgain; _navigationChanged = navigationChanged;
        _refresh = _p.SubtleButton(L("Dashboard_Refresh"), RefreshGlyph, () => _ = LoadSectionAsync(_selected, false));
        var title = new Grid { ColumnSpacing = 12 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(PageParts.Heading(L("Page_Administration"), AutomationHeadingLevel.Level1, "TitleTextBlockStyle"));
        Grid.SetColumn(_refresh, 1); title.Children.Add(_refresh);
        for (var i = 0; i < AdministrationModel.Sections.Length; i++)
        {
            var section = AdministrationModel.Sections[i];
            var item = new ListViewItem { Content = SectionItem(section, SectionGlyphs[i]), Tag = section, Padding = new Thickness(10, 8, 10, 8) };
            AutomationProperties.SetName(item, L("Admin_Section_" + section));
            _sections.Items.Add(item);
        }
        _sections.SelectionChanged += (_, _) =>
        {
            if (_sections.SelectedItem is not ListViewItem { Tag: string section } || section == _selected) return;
            _activeRequestGeneration++;
            _refresh.IsEnabled = true; _more.IsEnabled = true;
            _status.IsOpen = false; _status.ActionButton = null;
            _selected = section; _loadFailed = false; RenderSelected();
            if (!_loadedAt.TryGetValue(section, out var loaded) || DateTimeOffset.Now - loaded > StaleAfter) _ = LoadSectionAsync(section, false);
        };
        _sections.SelectedIndex = 0;
        _more.Content = L("Projects_LoadMore");
        AutomationProperties.SetName(_more, L("Projects_LoadMore"));
        _more.Click += (_, _) => _ = LoadSectionAsync(_selected, true);

        var sectionPanel = new StackPanel { Spacing = 8 };
        sectionPanel.Children.Add(PageParts.Heading(L("Admin_Sections"), AutomationHeadingLevel.Level2));
        sectionPanel.Children.Add(_sections);
        var main = new StackPanel { Spacing = 16 };
        main.Children.Add(_content); main.Children.Add(_more);
        var layout = new TwoColumnLayout(main, sectionPanel, 240, true, true);
        layout.Apply(1280);
        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32, 28, 32, 32) };
        root.Children.Add(title); root.Children.Add(_p.Secondary(L("Admin_Intro"))); root.Children.Add(_status); root.Children.Add(layout.Element);
        root.SizeChanged += (_, e) => layout.Apply(e.NewSize.Width);
        _scroll = PageParts.CenteredPage(root, 1280);
        Content = _scroll;
        RenderSelected();
    }

    private string L(string key) => _p.L(key);
    public void MarkStale() => _loadedAt.Clear();
    public void UserChanged() => MarkStale();
    public bool CanGoBack => _selected == "Maintenance" && _announcementDetail is not null;
    public bool TryGoBack()
    {
        if (_announcementDetail is null || _selected != "Maintenance") return false;
        _announcementDetail = null; RenderSelected(); _content.UpdateLayout(); _scroll.UpdateLayout();
        _scroll.ChangeView(null, _maintenanceOffset, null, true); _navigationChanged(); return true;
    }
    public Task EnsureLoadedAsync() => !_loadedAt.TryGetValue(_selected, out var loaded) || DateTimeOffset.Now - loaded > StaleAfter
        ? LoadSectionAsync(_selected, false) : Task.CompletedTask;

    private FrameworkElement SectionItem(string label, string glyph)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(PageParts.Glyph(glyph, 16)); row.Children.Add(new TextBlock { Text = L("Admin_Section_" + label), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private async Task LoadSectionAsync(string section, bool append)
    {
        var generation = _generations.GetValueOrDefault(section) + 1; _generations[section] = generation;
        var activeGeneration = ++_activeRequestGeneration;
        _loadFailed = false;
        if (!HasData(section)) _content.Content = _p.LoadingIndicator("Admin_Loading");
        _refresh.IsEnabled = false; _more.IsEnabled = false;
        var command = AdministrationModel.Command(section);
        var payload = AdministrationModel.IsPaged(section) ? AdministrationModel.ListPayload(section, append ? _next.GetValueOrDefault(section) ?? 0 : 0) : null;
        var result = command is null ? null : await _request(command, payload);
        if (_generations.GetValueOrDefault(section) != generation || _activeRequestGeneration != activeGeneration || _selected != section) return;
        _refresh.IsEnabled = true; _more.IsEnabled = true;
        if (result is null) return;
        if (!result.Ok)
        {
            _loadFailed = true;
            var forbidden = result.Status == 403 || result.ErrorCode is "FORBIDDEN" or "ADMIN_REQUIRED";
            if (forbidden)
            {
                ClearAllData(); _forbidden = true; ShowError("Admin_ErrorForbiddenTitle", "Admin_ErrorForbidden");
            }
            else if (section == "Maintenance" && result.Status == 503 && result.ErrorCode == "MAINTENANCE_SETUP_REQUIRED")
            {
                _loadFailed = false;
                _maintenance = new AdminMaintenance(false, null, []); _maintenanceUnavailable = true; _loadedAt[section] = DateTimeOffset.Now; _status.IsOpen = false;
            }
            else ShowErrorFor(result);
            if (section == _selected) RenderSelected();
            return;
        }
        _forbidden = false;
        var valid = section switch
        {
            "Requests" => StorePaged(section, result.Data is { } requestData ? AdministrationModel.ParseRequests(requestData, result.Meta) : null, append),
            "Projects" or "Reservations" => StoreProjects(result.Data is { } projectData ? AdministrationModel.ParseProjects(projectData, result.Meta) : null, append),
            "Members" => StoreMembers(result.Data is { } memberData ? AdministrationModel.ParseMembers(memberData, result.Meta) : null, append),
            "Discord" => StoreDiscord(result.Data is { } discordData ? AdministrationModel.ParseDiscord(discordData) : null),
            "Maintenance" => StoreMaintenance(result.Data is { } maintenanceData ? AdministrationModel.ParseMaintenance(maintenanceData) : null),
            "Audit" => StoreAudit(result.Data is { } auditData ? AdministrationModel.ParseAudit(auditData, result.Meta) : null, append),
            _ => false
        };
        if (!valid)
        {
            _loadFailed = true;
            ShowErrorFor(null);
            if (section == _selected) RenderSelected();
            return;
        }
        _loadFailed = false;
        _loadedAt[section] = DateTimeOffset.Now;
        if (section is "Projects" or "Reservations") _loadedAt[section == "Projects" ? "Reservations" : "Projects"] = _loadedAt[section];
        _status.IsOpen = false;
        if (section == "Maintenance") _maintenanceUnavailable = false;
        if (section == _selected) RenderSelected();
    }

    private bool StorePaged(string section, AdminPage<AdminRequest>? page, bool append)
    {
        if (page is null) return false;
        IReadOnlyList<AdminRequest> old = append && _requests.TryGetValue(section, out var prior) ? prior.Items : Array.Empty<AdminRequest>();
        _requests[section] = page with { Items = old.Concat(page.Items).DistinctBy(x => x.Id).ToList() };
        _next[section] = page.NextOffset; return true;
    }
    private bool StoreProjects(AdminPage<AdminProject>? page, bool append)
    {
        if (page is null) return false;
        IReadOnlyList<AdminProject> old = append ? _projects?.Items ?? Array.Empty<AdminProject>() : Array.Empty<AdminProject>();
        _projects = page with { Items = old.Concat(page.Items).DistinctBy(x => x.Id).ToList() };
        _next["Projects"] = page.NextOffset; _next["Reservations"] = page.NextOffset; return true;
    }
    private bool StoreMembers(AdminPage<AdminMember>? page, bool append)
    {
        if (page is null) return false;
        IReadOnlyList<AdminMember> old = append ? _members?.Items ?? Array.Empty<AdminMember>() : Array.Empty<AdminMember>();
        _members = page with { Items = old.Concat(page.Items).DistinctBy(x => x.Id).ToList() };
        _next["Members"] = page.NextOffset; return true;
    }
    private bool StoreDiscord(AdminDiscordData? data)
    {
        if (data is null) return false;
        _discord = data; return true;
    }
    private bool StoreMaintenance(AdminMaintenance? data)
    {
        if (data is null) return false;
        _maintenance = data; return true;
    }
    private bool StoreAudit(AdminPage<AdminAudit>? page, bool append)
    {
        if (page is null) return false;
        IReadOnlyList<AdminAudit> old = append ? _audit?.Items ?? Array.Empty<AdminAudit>() : Array.Empty<AdminAudit>();
        _audit = page with { Items = old.Concat(page.Items).ToList() };
        _next["Audit"] = page.NextOffset; return true;
    }
    private bool HasData(string section) => section switch
    {
        "Requests" => _requests.ContainsKey(section), "Projects" or "Reservations" => _projects is not null,
        "Members" => _members is not null, "Discord" => _discord is not null, "Maintenance" => _maintenance is not null, "Audit" => _audit is not null, _ => false
    };
    private void ClearAllData()
    {
        _requests.Clear(); _projects = null; _members = null; _discord = null; _maintenance = null; _audit = null;
        _loadedAt.Clear(); _next.Clear(); _maintenanceUnavailable = false; _announcementDetail = null;
    }

    private void ShowErrorFor(BridgeResult? result)
    {
        if (result is null) ShowError("Admin_ErrorTitle", "Dashboard_ErrorUnexpected");
        else if (result.Status == 0) ShowError("Admin_ErrorTitle", "Dashboard_ErrorConnection");
        else if (result.Status == 401 || result.ErrorCode == "UNAUTHORIZED")
        {
            ShowError("Admin_ErrorSignInTitle", "Admin_ErrorSignIn");
            var signIn = new Button { Content = L("Dashboard_SignInAgain") }; signIn.Click += (_, _) => _signInAgain(); _status.ActionButton = signIn;
        }
        else if (result.Status == 429 || result.ErrorCode == "RATE_LIMIT") ShowError("Admin_ErrorTitle", "Dashboard_ErrorRateLimit");
        else if (result.Status == 403 || result.ErrorCode == "FORBIDDEN") ShowError("Admin_ErrorForbiddenTitle", "Admin_ErrorForbidden");
        else ShowError("Admin_ErrorTitle", "Dashboard_ErrorUnexpected");
    }
    private void ShowError(string title, string message)
    {
        _status.Severity = title == "Admin_ErrorTitle" ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
        _status.Title = L(title); _status.Message = L(message); _status.ActionButton = null;
        if (title == "Admin_ErrorTitle")
        {
            var retry = new Button { Content = L("Retry") }; retry.Click += (_, _) => _ = LoadSectionAsync(_selected, false); _status.ActionButton = retry;
        }
        _status.IsOpen = true;
    }

    private void RenderSelected()
    {
        if (_forbidden) { RenderErrorMessage(); return; }
        if (_selected == "Maintenance" && _announcementDetail is { } announcement)
        {
            var back = _p.SubtleButton(L("Announcements_Back"), "\uE72B", () => TryGoBack());
            var panel = new StackPanel { Spacing = 16 };
            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(PageParts.Heading(L("Announcements_DetailTitle"), AutomationHeadingLevel.Level2));
            Grid.SetColumn(back, 1); header.Children.Add(back); panel.Children.Add(header);
            panel.Children.Add(AnnouncementCard.Detail(announcement, L, _language));
            _content.Content = panel; _more.Visibility = Visibility.Collapsed; return;
        }
        if (!HasData(_selected))
        {
            if (_loadFailed) RenderErrorMessage();
            else _content.Content = _p.LoadingIndicator("Admin_Loading");
            return;
        }
        FrameworkElement body = _selected switch
        {
            "Requests" => RenderRequests(), "Projects" => RenderProjects(), "Members" => RenderMembers(), "Reservations" => RenderReservations(),
            "Discord" => RenderDiscord(), "Maintenance" => RenderMaintenance(), "Audit" => RenderAudit(), _ => new StackPanel()
        };
        _content.Content = body;
        _more.Visibility = AdministrationModel.IsPaged(_selected) && _next.GetValueOrDefault(_selected) is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderErrorMessage()
    {
        _content.Content = _p.Secondary(_status.Message);
        _more.Visibility = Visibility.Collapsed;
    }

    private StackPanel CardBody() => new() { Spacing = 10, Padding = new Thickness(16, 12, 16, 16) };

    private Border Card(string title, string glyph, StackPanel body, string? badge = null)
    {
        var header = new Grid { ColumnSpacing = 8, Padding = new Thickness(16, 12, 16, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(PageParts.Glyph(glyph, 16));
        var heading = PageParts.Heading(L(title), AutomationHeadingLevel.Level2);
        Grid.SetColumn(heading, 1); header.Children.Add(heading);
        if (badge is not null)
        {
            var count = PageParts.VersionBadge(badge);
            AutomationProperties.SetName(count, badge); Grid.SetColumn(count, 2); header.Children.Add(count);
        }
        var panel = new StackPanel(); panel.Children.Add(header); panel.Children.Add(PageParts.Divider()); panel.Children.Add(body);
        return new Border { Style = PageParts.Res("DashboardCardStyle"), Child = panel };
    }

    private TextBlock Empty(string key) => _p.Secondary(L(key));
    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(x => x.Length > 0));
    private string Date(DateTimeOffset? date) => DashboardModel.FormatDate(date, _language);
    private static TextBlock Mono(string text) => new() { Text = text, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };

}
