using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace FusionLedger.Windows;

internal sealed partial class ProjectsView
{
    private const int CommitPreviewLength = 160;

    /// <summary>Below this filter-bar width the date range and "latest only" wrap under the search box.</summary>
    private const double NarrowFilterWidth = 560;

    // ----- Project overview -----

    private void PushProject(string projectId, string name)
    {
        if (!ProjectsModel.IsValidId(projectId)) return;
        // Going to a project that is already in the breadcrumb returns to it instead of stacking a copy.
        var existing = _stack.FindIndex(s => s.Kind == ScreenKind.Project && s.Id == projectId);
        if (existing >= 0)
        {
            PopTo(existing);
            return;
        }
        var screen = new Screen(ScreenKind.Project, projectId, name);
        screen.Load = () => LoadProjectAsync(screen);
        Push(screen);
    }

    private Task LoadProjectAsync(Screen screen) => ReloadProjectAsync(screen);

    /// <summary>Loads the project; returns false only when this load failed (a newer load of the same screen counts as refreshed).</summary>
    private async Task<bool> ReloadProjectAsync(Screen screen)
    {
        // This reload shows the current state, so a pending "changed elsewhere" mark is settled (a write on this page
        // reloads right after marking every page stale; it must not reload again and close its own notice).
        screen.WorkStale = false;
        var generation = ++screen.LoadGeneration;
        var result = await _request("project", ProjectsModel.ProjectPayload(screen.Id));
        // A newer load of the same screen is responsible for what is shown (and for its own error).
        if (generation != screen.LoadGeneration) return true;
        var detail = result.Ok && result.Data is { } data ? ProjectsModel.ParseProjectDetail(data) : null;
        if (detail is null)
        {
            ShowError(screen, result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result), () => LoadProjectAsync(screen));
            return false;
        }
        screen.Title = detail.Project.Name;
        if (Current == screen) UpdateBreadcrumb();
        var (content, layout) = BuildProjectContent(screen, detail);
        SetContent(screen, content, layout);
        return true;
    }

    private (FrameworkElement, TwoColumnLayout?) BuildProjectContent(Screen screen, ProjectDetail detail)
    {
        var project = detail.Project;
        var root = new StackPanel { Spacing = 16 };

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new InlineWrapPanel { HorizontalSpacing = 12 };
        var heading = PageParts.Heading(project.Name, AutomationHeadingLevel.Level1, "TitleTextBlockStyle");
        title.Children.Add(heading);
        title.Children.Add(_p.PrivateBadge());
        header.Children.Add(title);
        var refresh = _p.SubtleButton(L("Projects_Refresh"), "", () => _ = LoadProjectAsync(screen));
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);
        if (project.Description.Length > 0) root.Children.Add(_p.Secondary(project.Description));

        var tabs = new SelectorBar();
        var overviewTab = new SelectorBarItem { Text = L("Projects_Overview"), Icon = new FontIcon { Glyph = "" }, IsSelected = true };
        var commitsTab = new SelectorBarItem { Text = L("Projects_Commits"), Icon = new FontIcon { Glyph = "" } };
        tabs.Items.Add(overviewTab);
        tabs.Items.Add(commitsTab);
        root.Children.Add(tabs);

        var tabBody = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(tabBody);

        var overview = BuildOverview(screen, detail, () => tabs.SelectedItem = commitsTab);
        FrameworkElement? commits = null;
        tabBody.Content = overview.Element;
        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedItem == commitsTab)
            {
                commits ??= BuildCommitsTab(screen, project);
                tabBody.Content = commits;
                screen.Layout = null;
            }
            else
            {
                tabBody.Content = overview.Element;
                screen.Layout = overview;
                overview.Apply(ActualWidth);
            }
        };
        return (root, overview);
    }

    private TwoColumnLayout BuildOverview(Screen screen, ProjectDetail detail, Action showAllCommits)
    {
        var project = detail.Project;
        var head = detail.Head;

        var main = new StackPanel { Spacing = 16 };
        main.Children.Add(VersionBrowser(detail, head, showAllCommits));
        main.Children.Add(OverviewCard(project, head));

        var side = new StackPanel { Spacing = 16 };
        var work = WorkStatus(project.Reservation, compact: false);
        work.Children.Add(WorkActions(screen, project));
        work.Spacing = 10;
        work.Padding = new Thickness(16, 12, 16, 14);
        side.Children.Add(_p.Card("Projects_WorkReservation", "", work));
        side.Children.Add(AboutCard(project, head));
        side.Children.Add(TeamCard(detail.Members));

        return new TwoColumnLayout(main, side, 300, sideOnLeft: false, sideFirstWhenStacked: false);
    }

    private Border VersionBrowser(ProjectDetail detail, ProjectCommit? head, Action showAllCommits)
    {
        var panel = new StackPanel();
        var top = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (head is not null)
        {
            top.Children.Add(PageParts.Avatar(head.AuthorName, head.AuthorAvatar, 28));
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(CommitLink(head.Id, head.Title, head.Title));
            text.Children.Add(PageParts.Caption(JoinDot(head.AuthorName, DashboardModel.FormatDate(head.CreatedAt, _language))));
            Grid.SetColumn(text, 1);
            top.Children.Add(text);
        }
        else
        {
            // No head in the first page is unusual (the head is normally the newest); show its title from `latest`.
            var empty = _p.Secondary(detail.Commits.Count == 0 ? L("Projects_NoChanges") : detail.Project.Latest?.Title ?? string.Empty);
            Grid.SetColumnSpan(empty, 2);
            top.Children.Add(empty);
        }
        var count = Centered(PageParts.Caption(CommitCount(detail.Summary.Total)));
        Grid.SetColumn(count, 2);
        top.Children.Add(count);
        panel.Children.Add(top);

        foreach (var commit in detail.Commits.Take(ProjectsModel.VersionRowLimit))
        {
            panel.Children.Add(PageParts.Divider());
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 6, 16, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(PageParts.Glyph("", 14));
            var label = ProjectsModel.VersionLabel(commit.Version, commit.Id);
            var link = CommitLink(commit.Id, label, $"{label}: {commit.Title}");
            Grid.SetColumn(link, 1);
            row.Children.Add(link);
            var title = Centered(_p.Secondary(commit.Title, wrap: false));
            ToolTipService.SetToolTip(title, commit.Title);
            Grid.SetColumn(title, 2);
            row.Children.Add(title);
            var date = Centered(PageParts.Caption(DashboardModel.FormatDate(commit.CreatedAt, _language)));
            Grid.SetColumn(date, 3);
            row.Children.Add(date);
            panel.Children.Add(row);
        }
        if (detail.Summary.Total > ProjectsModel.VersionRowLimit)
        {
            panel.Children.Add(PageParts.Divider());
            panel.Children.Add(_p.LinkButton(L("Projects_ViewAllHistory"), showAllCommits, new Thickness(8, 4, 8, 6)));
        }
        return new Border { Style = PageParts.Res("DashboardCardStyle"), Child = panel };
    }

    private Border OverviewCard(ProjectSummary project, ProjectCommit? head)
    {
        var body = new StackPanel { Spacing = 12, Padding = new Thickness(20, 16, 20, 20) };
        body.Children.Add(PageParts.Heading(project.Name, AutomationHeadingLevel.Level2, "SubtitleTextBlockStyle"));
        body.Children.Add(_p.Secondary(project.Description.Length > 0 ? project.Description : L("Projects_NoDescription")));

        if (head is not null || project.Latest is not null)
        {
            body.Children.Add(PageParts.Divider());
            body.Children.Add(PageParts.Heading(L("Projects_LatestChange"), AutomationHeadingLevel.Level3, "SubtitleTextBlockStyle"));
            var commitId = head?.Id ?? project.Latest!.Id;
            var label = head is not null
                ? (head.Version.Length > 0 ? head.Version : head.Title)
                : (project.Latest!.Version.Length > 0 ? project.Latest.Version : project.Latest.Title);
            body.Children.Add(new TextBlock { Text = label, Style = PageParts.Res("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
            if (head is { Changes.Length: > 0 }) body.Children.Add(ChangesText(head.Changes));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            if (head?.ShareUri is { } uri) actions.Children.Add(OpenMfaButton(uri));
            actions.Children.Add(_p.SubtleButton(L("Projects_Details"), "", () => PushCommit(commitId, label)));
            body.Children.Add(actions);
        }
        body.Children.Add(_p.StorageNote());
        return _p.Card("Projects_ProjectOverview", "", body);
    }

    private Border AboutCard(ProjectSummary project, ProjectCommit? head)
    {
        var body = new StackPanel { Spacing = 10, Padding = new Thickness(16, 12, 16, 14) };
        body.Children.Add(_p.Secondary(project.Description.Length > 0 ? project.Description : L("Projects_NoDescription")));
        AddInfoRow(body, "Projects_ProjectType", new TextBlock { Text = "Clickteam Fusion", TextWrapping = TextWrapping.Wrap });
        var visibility = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        visibility.Children.Add(PageParts.Glyph("", 12));
        visibility.Children.Add(new TextBlock { Text = L("Projects_Private") });
        AddInfoRow(body, "Projects_Visibility", visibility);
        var created = DashboardModel.FormatDate(project.CreatedAt, _language);
        AddInfoRow(body, "Projects_Created", new TextBlock { Text = created.Length > 0 ? created : L("Projects_NotRecorded") });
        var updated = DashboardModel.FormatDate(head?.CreatedAt ?? project.Latest?.CreatedAt, _language);
        AddInfoRow(body, "Projects_LastUpdated", new TextBlock { Text = updated.Length > 0 ? updated : "—" });
        AddInfoRow(body, "Projects_ProjectId", _p.IdChip(project.Id, L("Projects_CopyProjectIdFormat"), "Projects_Copied"));
        return _p.Card("Projects_About", "", body);
    }

    private void AddInfoRow(StackPanel body, string labelKey, FrameworkElement value)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Centered(_p.Secondary(L(labelKey), wrap: false)));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        body.Children.Add(row);
    }

    private Border TeamCard(IReadOnlyList<ProjectMember> members)
    {
        var list = new StackPanel { Padding = new Thickness(16, 4, 16, 8) };
        if (members.Count == 0) list.Children.Add(_p.Secondary(L("Projects_NoMembers")));
        foreach (var member in members)
        {
            if (list.Children.Count > 0) list.Children.Add(PageParts.Divider());
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(PageParts.Avatar(member.Username, member.Avatar, 28));
            var text = new StackPanel { Spacing = 1 };
            text.Children.Add(new TextBlock { Text = member.Username, Style = PageParts.Res("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis });
            var role = L(ProjectsModel.RoleKey(member.Role));
            text.Children.Add(PageParts.Caption(role));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            AutomationProperties.SetName(row, $"{member.Username}, {role}");
            list.Children.Add(row);
        }
        return _p.Card("Projects_TeamAccess", "", list);
    }

    // ----- Commit timeline (project Commits tab and the personal history) -----

    /// <summary>A parsed page of commits; `Summary` is set only by the personal history.</summary>
    private sealed record TimelinePage(IReadOnlyList<ProjectCommit> Commits, int Total, int? NextOffset, HistorySummary? Summary);

    private FrameworkElement BuildCommitsTab(Screen screen, ProjectSummary project) =>
        BuildCommitTimeline(
            screen,
            "projectCommits",
            (filter, offset) => ProjectsModel.CommitsPayload(project.Id, filter, offset),
            result => result.Data is { } data && ProjectsModel.ParseCommitPage(data, result.Meta) is { } page
                ? new TimelinePage(page.Commits, page.Total, page.NextOffset, null)
                : null,
            projectOptions: null,
            showProject: false,
            onReload: null,
            onPage: null,
            out _);

    /// <summary>
    /// Search, date range, latest only (and, for the history, a project filter), a count line, commits grouped by day
    /// and "Load more". `onReload` runs when a first page starts loading; `reload` reloads it with the current filters.
    /// </summary>
    private FrameworkElement BuildCommitTimeline(
        Screen screen,
        string command,
        Func<CommitFilter, int, JsonObject?> payload,
        Func<BridgeResult, TimelinePage?> parse,
        IReadOnlyList<ProjectOption>? projectOptions,
        bool showProject,
        Action? onReload,
        Action<TimelinePage>? onPage,
        out Func<Task> reload)
    {
        var filter = CommitFilter.None;
        var commits = new List<ProjectCommit>();
        int? next = null;
        var total = 0;
        var loading = false;

        var root = new StackPanel { Spacing = 12 };
        ComboBox? projectFilter = null;
        if (projectOptions is { Count: > 0 })
        {
            // Its own row, so the search box keeps its width in a narrow window.
            projectFilter = new ComboBox { MinWidth = 220, MaxWidth = 360 };
            projectFilter.Items.Add(new ComboBoxItem { Content = L("History_AllProjects"), Tag = string.Empty });
            foreach (var option in projectOptions) projectFilter.Items.Add(new ComboBoxItem { Content = option.Name, Tag = option.Id });
            projectFilter.SelectedIndex = 0;
            AutomationProperties.SetName(projectFilter, L("History_ProjectFilter"));
            root.Children.Add(projectFilter);
        }
        var bar = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var search = new AutoSuggestBox { PlaceholderText = L("Projects_CommitSearchPlaceholder"), QueryIcon = new SymbolIcon(Symbol.Find) };
        AutomationProperties.SetName(search, L("Projects_CommitSearchPlaceholder"));
        bar.Children.Add(search);
        var days = new ComboBox { MinWidth = 150 };
        foreach (var option in ProjectsModel.DayOptions) days.Items.Add(L(option == 0 ? "Projects_AllTime" : $"Projects_Last{option}Days"));
        days.SelectedIndex = 0;
        AutomationProperties.SetName(days, L("Projects_DateRange"));
        bar.Children.Add(days);
        var latestOnly = new CheckBox { Content = L("Projects_LatestOnly"), MinWidth = 0 };
        bar.Children.Add(latestOnly);
        // One row when wide; below NarrowFilterWidth the date range and "latest only" move under the search box.
        void LayoutBar(double width)
        {
            var narrow = width > 0 && width < NarrowFilterWidth;
            Grid.SetColumnSpan(search, narrow ? 3 : 1);
            Grid.SetRow(days, narrow ? 1 : 0);
            Grid.SetColumn(days, narrow ? 0 : 1);
            days.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(latestOnly, narrow ? 1 : 0);
            Grid.SetColumn(latestOnly, narrow ? 1 : 2);
            Grid.SetColumnSpan(latestOnly, narrow ? 2 : 1);
        }
        LayoutBar(0);
        bar.SizeChanged += (_, e) => LayoutBar(e.NewSize.Width);
        root.Children.Add(bar);

        var summaryRow = new Grid { ColumnSpacing = 8 };
        summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var summary = _p.Secondary(string.Empty);
        summary.VerticalAlignment = VerticalAlignment.Center;
        summaryRow.Children.Add(summary);
        // Assigned once the filter controls and the loader exist.
        Action clearFilters = () => { };
        var clear = _p.LinkButton(L("Projects_ClearFilters"), () => clearFilters(), new Thickness(0), glyph: null);
        Grid.SetColumn(clear, 1);
        summaryRow.Children.Add(clear);
        root.Children.Add(summaryRow);

        var timeline = new StackPanel { Spacing = 16 };
        root.Children.Add(timeline);
        var more = new Button { Content = L("Projects_LoadMore"), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        root.Children.Add(more);

        async Task LoadAsync(bool append)
        {
            if (loading && append) return;
            loading = true;
            more.IsEnabled = false;
            var requested = filter;
            if (!append)
            {
                onReload?.Invoke();
                timeline.Children.Clear();
                timeline.Children.Add(_p.LoadingIndicator("Projects_Loading"));
            }
            var result = await _request(command, payload(requested, append ? next ?? 0 : 0));
            loading = false;
            more.IsEnabled = true;
            if (requested != filter) return;
            var page = result.Ok ? parse(result) : null;
            if (page is null)
            {
                if (!append) timeline.Children.Clear();
                ShowError(screen, result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result), () => LoadAsync(append));
                return;
            }
            if (!append) commits.Clear();
            commits.AddRange(page.Commits.Where(commit => commits.All(existing => existing.Id != commit.Id)));
            next = page.NextOffset;
            total = page.Total;
            screen.LoadedAt = DateTimeOffset.Now;
            if (Current == screen) CloseStatusBar();
            onPage?.Invoke(page);
            Render();
        }

        void Render()
        {
            summary.Text = CommitCount(total);
            clear.Visibility = filter.IsActive ? Visibility.Visible : Visibility.Collapsed;
            timeline.Children.Clear();
            if (commits.Count == 0) timeline.Children.Add(_p.Secondary(L(filter.IsActive ? "Projects_NoResults" : "Projects_NoCommits")));
            foreach (var (day, group) in ProjectsModel.GroupByDay(commits))
            {
                var section = new StackPanel { Spacing = 8 };
                var dayHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                dayHeader.Children.Add(PageParts.Glyph("", 14));
                var dayText = ProjectsModel.FormatDay(day, _language);
                dayHeader.Children.Add(PageParts.Heading(dayText.Length > 0 ? dayText : L("Projects_NotRecorded"), AutomationHeadingLevel.Level2));
                dayHeader.Children.Add(Centered(PageParts.Caption(CommitCount(group.Count))));
                section.Children.Add(dayHeader);
                var list = new StackPanel();
                foreach (var commit in group)
                {
                    if (list.Children.Count > 0) list.Children.Add(PageParts.Divider());
                    list.Children.Add(CommitRow(commit, showProject));
                }
                section.Children.Add(new Border { Style = PageParts.Res("DashboardCardStyle"), Child = list });
                timeline.Children.Add(section);
            }
            if (commits.Count > 0) timeline.Children.Add(_p.StorageNote());
            more.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
        }

        void Apply()
        {
            var project = projectFilter?.SelectedItem is ComboBoxItem { Tag: string id } ? id : string.Empty;
            var updated = new CommitFilter(ProjectsModel.NormalizeQuery(search.Text), ProjectsModel.DayOptions[Math.Max(0, days.SelectedIndex)], latestOnly.IsChecked == true, project);
            if (updated == filter && commits.Count > 0) return;
            filter = updated;
            next = null;
            _ = LoadAsync(append: false);
        }

        search.QuerySubmitted += (_, _) => Apply();
        days.SelectionChanged += (_, _) => Apply();
        latestOnly.Click += (_, _) => Apply();
        if (projectFilter is not null) projectFilter.SelectionChanged += (_, _) => Apply();
        clearFilters = () =>
        {
            search.Text = string.Empty;
            days.SelectedIndex = 0;
            latestOnly.IsChecked = false;
            if (projectFilter is not null) projectFilter.SelectedIndex = 0;
            Apply();
        };
        more.Click += async (_, _) => await LoadAsync(append: true);
        clear.Visibility = Visibility.Collapsed;
        reload = () =>
        {
            next = null;
            return LoadAsync(append: false);
        };
        _ = LoadAsync(append: false);
        return root;
    }

    private FrameworkElement CommitRow(ProjectCommit commit, bool showProject)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 12, 16, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(PageParts.Avatar(commit.AuthorName, commit.AuthorAvatar, 28));

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(CommitLink(commit.Id, commit.Title, commit.Title));
        var preview = commit.Changes.Trim();
        if (preview.Length > 0)
        {
            content.Children.Add(_p.Secondary(preview.Length > CommitPreviewLength ? preview[..CommitPreviewLength] + "…" : preview));
        }
        var meta = new InlineWrapPanel();
        meta.Children.Add(PageParts.VersionBadge(ProjectsModel.VersionLabel(commit.Version, commit.Id)));
        // The personal history names the project (the author is always you); a project's list names the author.
        var withProject = showProject && commit.ProjectId.Length > 0 && commit.ProjectName.Length > 0;
        if (withProject) meta.Children.Add(ProjectLink(commit.ProjectId, commit.ProjectName, 14));
        meta.Children.Add(Centered(PageParts.Caption(withProject
            ? DashboardModel.FormatDate(commit.CreatedAt, _language)
            : JoinDot(commit.AuthorName, DashboardModel.FormatDate(commit.CreatedAt, _language)))));
        content.Children.Add(meta);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        actions.Children.Add(_p.IdChip(commit.Id, L("Dashboard_CopyIdFormat"), "Dashboard_Copied"));
        if (commit.ShareUri is { } uri) actions.Children.Add(OpenMfaLink(uri));
        actions.Children.Add(_p.LinkButton(L("Projects_Details"), () => PushCommit(commit.Id, ProjectsModel.VersionLabel(commit.Version, commit.Id)), new Thickness(0), ""));
        content.Children.Add(actions);
        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        AutomationProperties.SetName(row, $"{(withProject ? commit.ProjectName : commit.AuthorName)}: {commit.Title}");
        return row;
    }

    // ----- Commit detail -----

    private void PushCommit(string commitId, string label)
    {
        if (!ProjectsModel.IsValidId(commitId)) return;
        var screen = new Screen(ScreenKind.Commit, commitId, label);
        screen.Load = () => LoadCommitAsync(screen);
        Push(screen);
    }

    private async Task LoadCommitAsync(Screen screen)
    {
        var result = await _request("commit", ProjectsModel.CommitPayload(screen.Id));
        var detail = result.Ok && result.Data is { } data ? ProjectsModel.ParseCommitDetail(data) : null;
        if (detail is null)
        {
            ShowError(screen, result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result), () => LoadCommitAsync(screen));
            return;
        }
        screen.Title = ProjectsModel.VersionLabel(detail.Commit.Version, detail.Commit.Id);
        if (Current == screen) UpdateBreadcrumb();
        var (content, layout) = BuildCommitContent(detail);
        SetContent(screen, content, layout);
    }

    private (FrameworkElement, TwoColumnLayout) BuildCommitContent(CommitDetail detail)
    {
        var commit = detail.Commit;
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(PageParts.Heading(commit.Title, AutomationHeadingLevel.Level1, "TitleTextBlockStyle"));

        var byline = new Grid { ColumnSpacing = 10 };
        byline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        byline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        byline.Children.Add(PageParts.Avatar(commit.AuthorName, commit.AuthorAvatar, 28));
        var who = new StackPanel { Spacing = 2 };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        line.Children.Add(new TextBlock { Text = commit.AuthorName, Style = PageParts.Res("BodyStrongTextBlockStyle") });
        line.Children.Add(_p.Secondary(L("Dashboard_Published"), wrap: false));
        if (commit.ProjectId.Length > 0 && commit.ProjectName.Length > 0) line.Children.Add(ProjectLink(commit.ProjectId, commit.ProjectName, 14));
        who.Children.Add(line);
        who.Children.Add(PageParts.Caption(DashboardModel.FormatDate(commit.CreatedAt, _language)));
        Grid.SetColumn(who, 1);
        byline.Children.Add(who);
        root.Children.Add(byline);

        var main = new StackPanel { Spacing = 16 };
        var changes = new StackPanel { Padding = new Thickness(20, 16, 20, 20), Spacing = 12 };
        changes.Children.Add(commit.Changes.Trim().Length > 0 ? ChangesText(commit.Changes) : _p.Secondary("—"));
        if (commit.ShareUri is { } uri)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(OpenMfaButton(uri));
            changes.Children.Add(actions);
        }
        main.Children.Add(_p.Card("Projects_Changes", "", changes));
        main.Children.Add(_p.StorageNote());

        var info = new StackPanel { Spacing = 10, Padding = new Thickness(16, 12, 16, 14) };
        AddInfoRow(info, "Projects_Version", commit.Version.Length > 0 ? PageParts.VersionBadge(commit.Version) : new TextBlock { Text = "—" });
        AddInfoRow(info, "Projects_VersionId", _p.IdChip(commit.Id, L("Dashboard_CopyIdFormat"), "Dashboard_Copied"));
        FrameworkElement basedOn = detail.Base is { } commitBase
            ? CommitLink(commitBase.Id, ProjectsModel.VersionLabel(commitBase.Version, commitBase.Id), $"{L("Projects_BasedOn")}: {commitBase.Title}")
            : new TextBlock { Text = L("Projects_InitialVersion") };
        AddInfoRow(info, "Projects_BasedOn", basedOn);
        AddInfoRow(info, "Projects_SharedBy", new TextBlock { Text = commit.AuthorName.Length > 0 ? commit.AuthorName : "—", TextTrimming = TextTrimming.CharacterEllipsis });
        var side = new StackPanel { Spacing = 16 };
        side.Children.Add(_p.Card("Projects_CommitInfo", "", info));

        var layout = new TwoColumnLayout(main, side, 300, sideOnLeft: false, sideFirstWhenStacked: false);
        root.Children.Add(layout.Element);
        return (root, layout);
    }

    // ----- Shared pieces -----

    private HyperlinkButton CommitLink(string commitId, string text, string automationName)
    {
        var label = new TextBlock { Text = text, Style = PageParts.Res("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        var link = new HyperlinkButton { Content = label, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(link, automationName);
        ToolTipService.SetToolTip(link, automationName);
        link.Click += (_, _) => PushCommit(commitId, text);
        return link;
    }

    private static TextBlock ChangesText(string changes) => new()
    {
        Text = changes.Trim(),
        Style = PageParts.Res("BodyTextBlockStyle"),
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true
    };

    private Button OpenMfaButton(Uri uri)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new TextBlock { Text = L("Projects_OpenMfa") });
        content.Children.Add(new FontIcon { Glyph = "", FontSize = 14, FontFamily = PageParts.SymbolFont });
        var button = new Button { Content = content, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        DescribeExternal(button, uri);
        button.Click += async (_, _) => await OpenShareAsync(uri);
        return button;
    }

    private HyperlinkButton OpenMfaLink(Uri uri)
    {
        var link = _p.LinkButton(L("Projects_OpenMfa"), () => _ = OpenShareAsync(uri), new Thickness(0), "");
        DescribeExternal(link, uri);
        return link;
    }

    /// <summary>Names the destination host so the user knows where the share link goes before opening it.</summary>
    private void DescribeExternal(FrameworkElement element, Uri uri)
    {
        var description = string.Format(L("Projects_OpenInBrowserFormat"), uri.Host);
        ToolTipService.SetToolTip(element, description);
        AutomationProperties.SetName(element, $"{L("Projects_OpenMfa")}, {description}");
    }

    /// <summary>Share links open only in the default browser, and only after they passed <see cref="ProjectsModel.SafeShareUri"/>.</summary>
    private async Task OpenShareAsync(Uri uri)
    {
        if (ProjectsModel.SafeShareUri(uri.AbsoluteUri) is not { } safe) return;
        bool launched;
        try
        {
            launched = await global::Windows.System.Launcher.LaunchUriAsync(safe);
        }
        catch
        {
            launched = false;
        }
        if (!launched) ShowNotice("Projects_OpenLinkFailedTitle", "Projects_OpenLinkFailed");
    }

    private string CommitCount(int count) =>
        count == 1 ? L("Projects_CommitCountOne") : string.Format(L("Projects_CommitCountFormat"), count);

    private static string JoinDot(params string[] parts) => string.Join(" · ", parts.Where(part => part.Length > 0));
}
