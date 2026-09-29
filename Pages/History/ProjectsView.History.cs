using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace MolHub.Windows;

/// <summary>
/// "Your commit history": the first screen of the Commit history page (`history`, always the signed-in author), with
/// the web summary counts, a project filter and the shared commit timeline. Commits and projects open inside this page.
/// </summary>
internal sealed partial class ProjectsView
{
    private Screen HistoryScreen()
    {
        var screen = new Screen(ScreenKind.History, string.Empty, L("History_Title"));
        var loading = false;
        // Leaving and returning before the first reply must not start a second build.
        screen.Load = async () =>
        {
            if (loading) return;
            loading = true;
            try
            {
                await LoadHistoryScreenAsync(screen);
            }
            finally
            {
                loading = false;
            }
        };
        return screen;
    }

    private async Task LoadHistoryScreenAsync(Screen screen)
    {
        // The project filter is best effort: if the list cannot be read, the history itself reports the error.
        var ticket = screen.State.Begin();
        var projects = await _request("projects", HistoryModel.ProjectOptionsPayload());
        // An older load that answers after a newer one must not rebuild the page.
        if (!screen.State.IsCurrent(ticket)) return;
        var options = projects.Ok && projects.Data is { } data ? HistoryModel.ProjectOptions(ProjectsModel.ParseProjectList(data, projects.Meta)) : [];
        // The page is built from this answer either way (the project filter is best effort); building replaces the state with the timeline's.
        screen.State.Complete(ticket, success: true, DateTimeOffset.Now);
        SetContent(screen, BuildHistoryContent(screen, options), null);
    }

    private FrameworkElement BuildHistoryContent(Screen screen, IReadOnlyList<ProjectOption> options)
    {
        var root = new StackPanel { Spacing = 16 };

        // One row: the summary counts (when they exist) with Refresh at the right.
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Assigned once the timeline exists.
        Func<Task> reload = () => Task.CompletedTask;
        var refresh = _p.SubtleButton(L("Projects_Refresh"), "\uE72C", () => _ = reload());
        refresh.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(refresh, 1);

        // Counts come only from the server `summary` for the current filters; hidden until one arrives.
        var stats = new InlineWrapPanel { HorizontalSpacing = 24, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(stats, L("History_Summary"));
        var total = SummaryStat(stats, "History_TotalCommits");
        var projects = SummaryStat(stats, "History_Projects");
        var versions = SummaryStat(stats, "History_NamedVersions");
        stats.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(stats);
        header.Children.Add(refresh);
        root.Children.Add(header);

        root.Children.Add(BuildCommitTimeline(
            screen,
            "history",
            HistoryModel.HistoryPayload,
            result => result.Data is { } data && HistoryModel.ParseHistory(data, result.Meta) is { } page
                ? new TimelinePage(page.Commits, page.Total, page.NextOffset, page.Summary)
                : null,
            options,
            showProject: true,
            // Counts belong to one filter: hide them while a new first page loads (or fails) until its summary arrives.
            onReload: () => stats.Visibility = Visibility.Collapsed,
            onPage: page =>
            {
                if (page.Summary is not { } summary) return;
                total(summary.Total);
                projects(summary.Projects);
                versions(summary.Versions);
                stats.Visibility = Visibility.Visible;
            },
            out var timelineReload));
        // Without project options (their read failed), Refresh rebuilds the page so the project filter can return.
        reload = options.Count > 0 ? timelineReload : () => LoadHistoryScreenAsync(screen);
        screen.Refresh = reload;
        // The history's freshness is that of its timeline (the timeline built just above registered it on the screen).
        if (screen.Timeline is { } timelineState) screen.State = timelineState;
        return root;
    }

    /// <summary>A bold number with its caption ("12 Total commits"); returns the setter for the number.</summary>
    private Action<int> SummaryStat(Panel stats, string labelKey)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var number = new TextBlock { Style = PageParts.Res("BodyStrongTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
        item.Children.Add(number);
        item.Children.Add(Centered(_p.Secondary(L(labelKey), wrap: false)));
        stats.Children.Add(item);
        return value =>
        {
            var text = value.ToString(System.Globalization.CultureInfo.CurrentCulture);
            number.Text = text;
            AutomationProperties.SetName(item, $"{L(labelKey)}: {text}");
        };
    }
}
