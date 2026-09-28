using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace MolHub.Windows;

/// <summary>
/// Native Dashboard: workspace status, top projects, activity and the recent-commit feed from `/api/v1/dashboard`,
/// laid out like the web workspace (side column + feed) and collapsing to one column on narrow windows.
/// Only data returned by the server is shown; actions without a native implementation are not rendered.
/// </summary>
internal sealed class DashboardView : UserControl
{
    private const double WideLayoutMinWidth = 860;
    private const double SideColumnWidth = 300;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private readonly Func<string, string> _l;
    private readonly AuthenticatedUser _user;
    private readonly string _language;
    private readonly Func<Task<BridgeResult>> _load;
    private readonly Action<NativePage> _navigate;
    private readonly Action<string, string> _openProject;
    private readonly Action<string, string> _openPublish;
    private readonly Action _signInAgain;
    private readonly InfoBar _statusBar = new() { IsClosable = false, IsOpen = false };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private Grid? _columns;
    private FrameworkElement? _side;
    private FrameworkElement? _feed;
    private Button? _refreshButton;
    private DateTimeOffset _loadedAt;
    private bool _hasContent;
    private int _generation;

    public DashboardView(Func<string, string> localize, AuthenticatedUser user, string language,
        Func<Task<BridgeResult>> load, Action<NativePage> navigate, Action<string, string> openProject, Action<string, string> openPublish,
        Action signInAgain)
    {
        _l = localize;
        _user = user;
        _language = language;
        _load = load;
        _navigate = navigate;
        _openProject = openProject;
        _openPublish = openPublish;
        _signInAgain = signInAgain;

        var title = new TextBlock { Text = _l("Page_Dashboard"), Style = Res("TitleTextBlockStyle") };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        var root = new StackPanel { Spacing = 20, Padding = new Thickness(32, 28, 32, 32) };
        root.Children.Add(title);
        root.Children.Add(_statusBar);
        root.Children.Add(_body);
        Content = PageParts.CenteredPage(root, 1280);
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    /// <summary>Reloads the next time the page is shown (a write changed reservations or commits).</summary>
    public void MarkStale() => _loadedAt = default;

    /// <summary>Loads on first display and again when the data is older than a minute.</summary>
    public Task EnsureLoadedAsync() =>
        !_hasContent || DateTimeOffset.Now - _loadedAt > StaleAfter ? RefreshAsync() : Task.CompletedTask;

    public async Task RefreshAsync()
    {
        var generation = ++_generation;
        if (!_hasContent) _body.Content = LoadingIndicator();
        if (_refreshButton is not null) _refreshButton.IsEnabled = false;

        var result = await _load();
        if (generation != _generation) return;
        if (_refreshButton is not null) _refreshButton.IsEnabled = true;

        var data = result.Ok && result.Data is { } json ? DashboardModel.Parse(json) : null;
        if (data is null)
        {
            ShowError(result.Ok ? DashboardError.Unexpected : DashboardModel.ErrorFor(result));
            if (!_hasContent) _body.Content = null;
            return;
        }

        _statusBar.IsOpen = false;
        _body.Content = BuildContent(data);
        _hasContent = true;
        _loadedAt = DateTimeOffset.Now;
        ApplyLayout(ActualWidth);
    }

    private FrameworkElement LoadingIndicator()
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 48, 0, 0) };
        panel.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32 });
        panel.Children.Add(new TextBlock { Text = _l("Dashboard_Loading"), Style = Res("DashboardSecondaryTextStyle") });
        AutomationProperties.SetName(panel, _l("Dashboard_Loading"));
        return panel;
    }

    private void ShowError(DashboardError error)
    {
        var (severity, key) = error switch
        {
            DashboardError.Connection => (InfoBarSeverity.Error, "Dashboard_ErrorConnection"),
            DashboardError.SessionEnded => (InfoBarSeverity.Warning, "Dashboard_ErrorSession"),
            DashboardError.PendingApproval => (InfoBarSeverity.Informational, "Dashboard_ErrorPending"),
            DashboardError.Maintenance => (InfoBarSeverity.Warning, "Dashboard_ErrorMaintenance"),
            DashboardError.RateLimited => (InfoBarSeverity.Warning, "Dashboard_ErrorRateLimit"),
            _ => (InfoBarSeverity.Error, "Dashboard_ErrorUnexpected")
        };
        _statusBar.Severity = severity;
        _statusBar.Title = _l(key + "Title");
        _statusBar.Message = _l(key);
        var action = new Button();
        if (error == DashboardError.SessionEnded)
        {
            action.Content = _l("Dashboard_SignInAgain");
            action.Click += (_, _) => _signInAgain();
        }
        else
        {
            action.Content = _l("Retry");
            action.Click += async (_, _) => await RefreshAsync();
        }
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
    }

    private FrameworkElement BuildContent(DashboardData data)
    {
        var side = new StackPanel { Spacing = 16 };
        side.Children.Add(StatusCard(data));
        side.Children.Add(TopProjectsCard(data));
        side.Children.Add(ActivityCard(data));
        side.Children.Add(StorageNote());

        var feed = new StackPanel { Spacing = 0 };
        var mine = data.ReservedBy(_user.Username);
        if (mine.Count > 0)
        {
            var yourWork = YourWorkCard(mine);
            yourWork.Margin = new Thickness(0, 0, 0, 20);
            feed.Children.Add(yourWork);
        }
        feed.Children.Add(FeedHeader());
        var commits = data.Commits.Take(DashboardModel.CommitLimit).ToList();
        if (commits.Count == 0)
        {
            feed.Children.Add(EmptyText("Dashboard_NoActivity", new Thickness(0, 16, 0, 0)));
        }
        foreach (var commit in commits)
        {
            feed.Children.Add(Divider());
            feed.Children.Add(FeedItem(commit));
        }

        _columns = new Grid { ColumnSpacing = 24, RowSpacing = 24 };
        _side = side;
        _feed = feed;
        _columns.Children.Add(side);
        _columns.Children.Add(feed);
        return _columns;
    }

    private void ApplyLayout(double width)
    {
        if (_columns is null || _side is null || _feed is null || width <= 0) return;
        var wide = width >= WideLayoutMinWidth;
        _columns.ColumnDefinitions.Clear();
        _columns.RowDefinitions.Clear();
        if (wide)
        {
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SideColumnWidth) });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_side, 0);
            Grid.SetRow(_side, 0);
            Grid.SetColumn(_feed, 1);
            Grid.SetRow(_feed, 0);
        }
        else
        {
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_side, 0);
            Grid.SetRow(_side, 0);
            Grid.SetColumn(_feed, 0);
            Grid.SetRow(_feed, 1);
        }
    }

    private Border StatusCard(DashboardData data)
    {
        var rows = new StackPanel();
        AddStatusRow(rows, "Dashboard_ActiveProjects", data.ActiveProjects, first: true);
        AddStatusRow(rows, "Dashboard_InProgress", data.InProgress);
        AddStatusRow(rows, "Dashboard_YourReservations", data.ReservedBy(_user.Username).Count);
        if (DashboardModel.ShowsPendingApprovals(_user.Role, _user.ProjectManager))
        {
            AddStatusRow(rows, "Dashboard_PendingApprovals", data.PendingApprovals);
        }
        rows.Padding = new Thickness(16, 4, 16, 8);
        return Card("Dashboard_WorkspaceStatus", "\uE823", rows);
    }

    private void AddStatusRow(StackPanel rows, string labelKey, int value, bool first = false)
    {
        if (!first) rows.Children.Add(Divider());
        var row = new Grid { Padding = new Thickness(0, 10, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = _l(labelKey), Style = Res("DashboardSecondaryTextStyle"), VerticalAlignment = VerticalAlignment.Center };
        var number = new TextBlock { Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture), Style = Res("BodyStrongTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(number, 1);
        row.Children.Add(label);
        row.Children.Add(number);
        AutomationProperties.SetName(row, $"{_l(labelKey)}: {value}");
        rows.Children.Add(row);
    }

    private Border TopProjectsCard(DashboardData data)
    {
        var list = new StackPanel { Padding = new Thickness(16, 8, 16, 8), Spacing = 2 };
        var projects = data.Projects.Take(DashboardModel.TopProjectLimit).ToList();
        if (projects.Count == 0) list.Children.Add(EmptyText("Dashboard_NoProjects", new Thickness(0, 6, 0, 6)));
        foreach (var project in projects)
        {
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Glyph("\uE8B7", 14));
            var name = new TextBlock { Text = project.Name, Style = Res("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            ToolTipService.SetToolTip(name, project.Name);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            list.Children.Add(ProjectButton(row, project));
        }
        list.Children.Add(LinkButton("Dashboard_ViewAllProjects", () => _navigate(NativePage.Projects), new Thickness(-8, 4, 0, 0)));
        return Card("Dashboard_TopProjects", "\uE8B7", list);
    }

    private Border ActivityCard(DashboardData data)
    {
        var list = new StackPanel { Padding = new Thickness(16, 8, 16, 12), Spacing = 12 };
        var items = data.Activity.Take(DashboardModel.ActivityLimit).ToList();
        if (items.Count == 0) list.Children.Add(EmptyText("Dashboard_NoActivity", new Thickness(0, 6, 0, 0)));
        foreach (var item in items)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Glyph("\uE823", 14);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 3, 0, 0);
            row.Children.Add(icon);
            var text = new StackPanel { Spacing = 2 };
            var project = new TextBlock { Text = item.ProjectName, Style = Res("DashboardAccentTextStyle"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            ToolTipService.SetToolTip(project, item.ProjectName);
            text.Children.Add(project);
            var detail = string.Join(" · ", new[] { item.Actor, DashboardModel.FormatDate(item.CreatedAt, _language) }.Where(part => part.Length > 0));
            text.Children.Add(new TextBlock { Text = detail, Style = Res("DashboardCaptionTextStyle") });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return Card("Dashboard_Activity", "\uE823", list);
    }

    private Border StorageNote()
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Glyph("\uE946", 14);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        row.Children.Add(icon);
        var text = new TextBlock { Text = _l("Dashboard_StorageNote"), Style = Res("DashboardCaptionTextStyle"), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return new Border { Style = Res("DashboardCardStyle"), Child = row };
    }

    private Border YourWorkCard(IReadOnlyList<DashboardProject> mine)
    {
        var list = new StackPanel { Padding = new Thickness(16, 8, 16, 12), Spacing = 10 };
        foreach (var project in mine)
        {
            var item = new StackPanel { Spacing = 2 };
            item.Children.Add(new TextBlock { Text = project.Name, Style = Res("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis });
            var since = DashboardModel.FormatDate(project.Reservation?.StartedAt, _language);
            item.Children.Add(new TextBlock
            {
                Text = since.Length > 0 ? $"{_l("Dashboard_ReservedSince")} {since}" : _l("Dashboard_ReservedSince"),
                Style = Res("DashboardCaptionTextStyle")
            });
            // The project row opens the project; "Publish version" opens its publish form directly, as on the web.
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(ProjectButton(item, project));
            var publish = new Button { Content = _l("Work_PublishShort"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(publish, $"{_l("Work_PublishShort")}: {project.Name}");
            publish.Click += (_, _) => _openPublish(project.Id, project.Name);
            Grid.SetColumn(publish, 1);
            row.Children.Add(publish);
            list.Children.Add(row);
        }
        return Card("Dashboard_YourWork", "\uE823", list);
    }

    /// <summary>Wraps a project row in a subtle full-width button that opens the native project overview.</summary>
    private Button ProjectButton(FrameworkElement content, DashboardProject project)
    {
        var button = new Button
        {
            Content = content,
            Style = Res("SubtleButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(-8, 0, -8, 0)
        };
        AutomationProperties.SetName(button, project.Name);
        button.Click += (_, _) => _openProject(project.Id, project.Name);
        return button;
    }

    private FrameworkElement FeedHeader()
    {
        var header = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Glyph("\uE81C", 16));
        var title = new TextBlock { Text = _l("Dashboard_RecentCommits"), Style = Res("BodyStrongTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var history = LinkButton("Dashboard_ViewYourHistory", () => _navigate(NativePage.CommitHistory), new Thickness(0));
        Grid.SetColumn(history, 2);
        header.Children.Add(history);

        var refreshContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        refreshContent.Children.Add(new FontIcon { Glyph = "\uE72C", FontSize = 14, FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"] });
        refreshContent.Children.Add(new TextBlock { Text = _l("Dashboard_Refresh") });
        _refreshButton = new Button { Content = refreshContent, Style = Res("SubtleButtonStyle") };
        AutomationProperties.SetName(_refreshButton, _l("Dashboard_Refresh"));
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(_refreshButton, 3);
        header.Children.Add(_refreshButton);
        return header;
    }

    private FrameworkElement FeedItem(DashboardCommit commit)
    {
        var item = new StackPanel { Spacing = 10, Padding = new Thickness(0, 16, 0, 16) };

        var byline = new Grid { ColumnSpacing = 10 };
        byline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        byline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var picture = new PersonPicture { Width = 28, Height = 28, DisplayName = commit.AuthorName, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAccessibilityView(picture, AccessibilityView.Raw);
        if (commit.AuthorAvatar is { } avatar) AvatarImage.Attach(picture, avatar, 56);

        byline.Children.Add(picture);

        var who = new Grid { ColumnSpacing = 4 };
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var author = new TextBlock { Text = commit.AuthorName, Style = Res("BodyStrongTextBlockStyle") };
        var verb = new TextBlock { Text = _l("Dashboard_Published"), Style = Res("DashboardSecondaryTextStyle") };
        var project = new TextBlock { Text = commit.ProjectName, Style = Res("DashboardAccentTextStyle"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        ToolTipService.SetToolTip(project, commit.ProjectName);
        Grid.SetColumn(verb, 1);
        Grid.SetColumn(project, 2);
        who.Children.Add(author);
        who.Children.Add(verb);
        who.Children.Add(project);
        var bylineText = new StackPanel { Spacing = 2 };
        bylineText.Children.Add(who);
        bylineText.Children.Add(new TextBlock { Text = DashboardModel.FormatDate(commit.CreatedAt, _language), Style = Res("DashboardCaptionTextStyle") });
        Grid.SetColumn(bylineText, 1);
        byline.Children.Add(bylineText);
        item.Children.Add(byline);

        var body = new StackPanel { Spacing = 8, Padding = new Thickness(16, 14, 16, 14) };
        var title = new TextBlock { Text = commit.Title, Style = Res("BodyStrongTextBlockStyle"), FontSize = 16, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(title);
        var preview = DashboardModel.Preview(commit.Changes);
        if (preview.Length > 0) body.Children.Add(new TextBlock { Text = preview, Style = Res("BodyTextBlockStyle"), TextWrapping = TextWrapping.Wrap });

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        if (commit.Version.Length > 0)
        {
            meta.Children.Add(new Border
            {
                Style = Res("DashboardVersionBadgeStyle"),
                Child = new TextBlock { Text = commit.Version, Style = Res("DashboardVersionTextStyle") }
            });
        }
        meta.Children.Add(IdChip(commit.Id));
        body.Children.Add(meta);
        item.Children.Add(new Border { Style = Res("DashboardCardStyle"), Child = body });

        AutomationProperties.SetName(item, $"{commit.AuthorName} {_l("Dashboard_Published")} {commit.ProjectName}: {commit.Title}");
        return item;
    }

    private FrameworkElement IdChip(string id)
    {
        var shortId = DashboardModel.ShortId(id);
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        chip.Children.Add(new TextBlock { Text = shortId, Style = Res("DashboardIdTextStyle") });
        var icon = new FontIcon { Glyph = "\uE8C8", FontSize = 12, FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"] };
        var copy = new Button { Content = icon, Style = Res("SubtleButtonStyle"), Padding = new Thickness(6, 4, 6, 4), MinWidth = 0, MinHeight = 0 };
        var copyName = string.Format(_l("Dashboard_CopyIdFormat"), shortId);
        AutomationProperties.SetName(copy, copyName);
        ToolTipService.SetToolTip(copy, copyName);
        copy.Click += async (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(id);
            Clipboard.SetContent(package);
            icon.Glyph = "\uE73E";
            ToolTipService.SetToolTip(copy, _l("Dashboard_Copied"));
            if (FrameworkElementAutomationPeer.FromElement(copy) is { } peer)
            {
                peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, _l("Dashboard_Copied"), "DashboardCopy");
            }
            await Task.Delay(1500);
            icon.Glyph = "\uE8C8";
            ToolTipService.SetToolTip(copy, copyName);
        };
        chip.Children.Add(copy);
        return new Border { Style = Res("DashboardIdChipStyle"), Child = chip };
    }

    private Border Card(string titleKey, string glyph, FrameworkElement body)
    {
        var header = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(Glyph(glyph, 16));
        var title = new TextBlock { Text = _l(titleKey), Style = Res("BodyStrongTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(Divider());
        panel.Children.Add(body);
        return new Border { Style = Res("DashboardCardStyle"), Child = panel };
    }

    private HyperlinkButton LinkButton(string key, Action action, Thickness margin)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new TextBlock { Text = _l(key) });
        content.Children.Add(new FontIcon { Glyph = "\uE72A", FontSize = 12, FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"] });
        var button = new HyperlinkButton { Content = content, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, _l(key));
        button.Click += (_, _) => action();
        return button;
    }

    private TextBlock EmptyText(string key, Thickness margin) =>
        new() { Text = _l(key), Style = Res("DashboardSecondaryTextStyle"), Margin = margin, TextWrapping = TextWrapping.Wrap };

    private static FontIcon Glyph(string glyph, double size)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = size, Style = Res("DashboardIconStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    private static Border Divider() => new() { Style = Res("DashboardDividerStyle") };

    private static Microsoft.UI.Xaml.Style Res(string key) => (Microsoft.UI.Xaml.Style)Application.Current.Resources[key];
}
