using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json.Nodes;

namespace MolHub.Windows;

/// <summary>
/// The notification center of the title bar: owns the <see cref="NotificationPoller"/> and its handlers, starts and stops it,
/// draws the bell badge and the flyout list, and shows toasts for items that arrive while the window is inactive.
/// Navigation, the flyout placement and the page reload go through callbacks into <see cref="MainWindow"/>.
/// </summary>
internal sealed class NotificationCenter
{
    private readonly Func<string, string> _localize;
    private readonly InfoBadge _badge;
    private readonly Button _bellButton;
    private readonly Flyout _flyout;
    private readonly TextBlock _statusText;
    private readonly Panel _listPanel;
    private readonly Func<bool> _isSigningOut;
    private readonly Func<bool> _isSignOutRequested;
    private readonly Func<bool> _isWindowActive;
    private readonly Func<bool> _bridgeConnected;
    private readonly Func<bool> _projectsAvailable;
    private readonly Func<string?> _userId;
    private readonly Action<string, string> _openProject;
    private readonly Action<string, string, string> _openCommit;
    private readonly Action _showFlyout;
    private readonly Action _restoreWindow;
    private readonly Action _reloadCurrentPage;
    // Polls only while the bridge is connected and an approved account with a known id is signed in.
    private readonly NotificationPoller _notifications;
    private bool _notificationsRunning;
    private bool _notificationsFlyoutOpen;
    // Ids that were unread when the flyout opened (accent dot); cleared when it closes.
    private readonly HashSet<long> _unreadSnapshot = [];

    public NotificationCenter(
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        Func<string, JsonObject?, Task<BridgeResult>> request,
        Func<string, string> localize,
        InfoBadge badge,
        Button bellButton,
        Flyout flyout,
        TextBlock statusText,
        Panel listPanel,
        Func<bool> isSigningOut,
        Func<bool> isSignOutRequested,
        Func<bool> isWindowActive,
        Func<bool> bridgeConnected,
        Func<bool> projectsAvailable,
        Func<string?> userId,
        Action<string, string> openProject,
        Action<string, string, string> openCommit,
        Action showFlyout,
        Action restoreWindow,
        Action reloadCurrentPage)
    {
        _localize = localize;
        _badge = badge;
        _bellButton = bellButton;
        _flyout = flyout;
        _statusText = statusText;
        _listPanel = listPanel;
        _isSigningOut = isSigningOut;
        _isSignOutRequested = isSignOutRequested;
        _isWindowActive = isWindowActive;
        _bridgeConnected = bridgeConnected;
        _projectsAvailable = projectsAvailable;
        _userId = userId;
        _openProject = openProject;
        _openCommit = openCommit;
        _showFlyout = showFlyout;
        _restoreWindow = restoreWindow;
        _reloadCurrentPage = reloadCurrentPage;
        _notifications = new NotificationPoller(dispatcher, request);
        _notifications.Changed += Notifications_Changed;
        _notifications.PageRefreshRequested += Notifications_PageRefreshRequested;
        // Toasts come only from items that arrive by polling (never the first load or a reset).
        _notifications.ItemsArrived += Notifications_ItemsArrived;
    }

    private string L(string key) => _localize(key);

    /// <summary>Unsubscribes the poller handlers and stops it (the window is closing).</summary>
    public void Shutdown()
    {
        _notifications.Changed -= Notifications_Changed;
        _notifications.PageRefreshRequested -= Notifications_PageRefreshRequested;
        _notifications.ItemsArrived -= Notifications_ItemsArrived;
        _notificationsRunning = false;
        _notifications.Stop();
    }

    /// <summary>Starts or stops the poller so it runs only while the bridge is connected, an approved account with a known id is signed in and no sign-out is under way.</summary>
    public void SyncNotifications()
    {
        var userId = _userId();
        var shouldRun = !_isSigningOut() && !_isSignOutRequested()
            && _bridgeConnected()
            && _projectsAvailable()
            && userId is not null;
        if (shouldRun == _notificationsRunning) return;
        _notificationsRunning = shouldRun;
        if (shouldRun) _notifications.Start(userId);
        else _notifications.Stop();
    }

    /// <summary>Removes the toasts that are still shown (sign-out).</summary>
    public void ClearToasts() => NotificationToasts.RemoveAllAsync();

    /// <summary>Redraws the bell badge and name, and the flyout while it is open (for example after a language change).</summary>
    public void RefreshUi() => UpdateNotificationsUi();

    public void FlyoutOpened()
    {
        _notificationsFlyoutOpen = true;
        SnapshotUnread();
        _notifications.MarkAllSeen();
    }

    public void FlyoutClosed()
    {
        _notificationsFlyoutOpen = false;
        _unreadSnapshot.Clear();
        _listPanel.Children.Clear();
    }

    private void Notifications_ItemsArrived(IReadOnlyList<NotificationItem> items) =>
        NotificationToasts.Show(items, _isWindowActive(), L);

    /// <summary>A toast was clicked while the app runs (already validated by <see cref="NotificationToastPolicy"/>): open the item, or the list when it is no longer known.</summary>
    public void HandleToastActivation(ToastTarget target)
    {
        if (_isSigningOut() || _isSignOutRequested()) return;
        _restoreWindow();
        NotificationItem? match = null;
        if (target.Action == ToastAction.Open)
        {
            foreach (var item in _notifications.Feed.Items)
            {
                if (item.ProjectId != target.ProjectId) continue;
                if (target.CommitId is not null && item.Commit?.Id != target.CommitId) continue;
                match = item;
                break;
            }
        }
        if (match is not null) OpenNotification(match);
        else _showFlyout();
    }

    private void Notifications_Changed(object? sender, EventArgs e)
    {
        // Items that arrive while the flyout is open are shown at once and never counted as unread.
        if (_notificationsFlyoutOpen && _notifications.Feed.UnreadCount > 0)
        {
            SnapshotUnread();
            _notifications.MarkAllSeen();
            return;
        }
        UpdateNotificationsUi();
    }

    private void Notifications_PageRefreshRequested(object? sender, EventArgs e)
    {
        if (_isSigningOut()) return;
        // The server reset the cursor: reload what is on screen through the shared stale path.
        _reloadCurrentPage();
    }

    private void SnapshotUnread()
    {
        var feed = _notifications.Feed;
        var seen = feed.LastSeen ?? 0;
        foreach (var item in feed.Items)
        {
            if (item.Id > seen) _unreadSnapshot.Add(item.Id);
        }
    }

    /// <summary>Refreshes the bell badge and name, and (while the flyout is open) the status line and the list.</summary>
    private void UpdateNotificationsUi()
    {
        var feed = _notifications.Feed;
        var count = feed.UnreadCount;
        var overflow = feed.UnreadOverflow || count > 99;
        if (count > 0)
        {
            // InfoBadge shows numbers only, so 99+ is a "99" badge whose name (below) says "99+".
            _badge.Value = Math.Min(count, 99);
            _badge.Visibility = Visibility.Visible;
        }
        else
        {
            _badge.Visibility = Visibility.Collapsed;
        }
        var name = count > 0
            ? string.Format(L("Notifications_UnreadFormat"), overflow ? "99+" : count.ToString(System.Globalization.CultureInfo.CurrentCulture))
            : L("Notifications");
        ToolTipService.SetToolTip(_bellButton, name);
        AutomationProperties.SetName(_bellButton, name);

        if (!_notificationsFlyoutOpen) return;
        var items = feed.Items;
        var status = _notifications.State switch
        {
            NotificationPollerState.Loading => L("Notifications_Loading"),
            NotificationPollerState.Ready => items.Count == 0 ? L("Notifications_Empty") : null,
            NotificationPollerState.Paused => L("Notifications_Paused"),
            NotificationPollerState.Error => L("Notifications_Error"),
            _ => L("NotConnected")
        };
        _statusText.Text = status ?? string.Empty;
        _statusText.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        _listPanel.Children.Clear();
        var language = SettingsView.ReadLanguage();
        foreach (var item in items) _listPanel.Children.Add(CreateNotificationRow(item, _unreadSnapshot.Contains(item.Id), language));
    }

    private Button CreateNotificationRow(NotificationItem item, bool unread, string language)
    {
        var sentence = NotificationText.Sentence(item, L);
        var commitLine = NotificationText.CommitLine(item, L);
        var time = DashboardModel.FormatDate(item.CreatedAt, language);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = NotificationText.Glyph(item.Kind),
            FontSize = 16,
            FontFamily = PageParts.SymbolFont,
            Style = PageParts.Res("DashboardIconStyle"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0)
        };
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        grid.Children.Add(icon);

        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(new TextBlock { Text = sentence, TextWrapping = TextWrapping.Wrap });
        if (commitLine is not null)
        {
            body.Children.Add(new TextBlock
            {
                Text = commitLine,
                Style = PageParts.Res("DashboardSecondaryTextStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }
        if (time.Length > 0) body.Children.Add(new TextBlock { Text = time, Style = PageParts.Res("DashboardCaptionTextStyle") });
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        if (unread)
        {
            var dot = new Ellipse { Style = PageParts.Res("NotificationUnreadDotStyle"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetAccessibilityView(dot, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetColumn(dot, 2);
            grid.Children.Add(dot);
        }

        var button = new Button { Style = PageParts.Res("NotificationItemButtonStyle"), Content = grid };
        var parts = new List<string>();
        if (unread) parts.Add(L("Notifications_UnreadItem"));
        parts.Add(sentence);
        if (commitLine is not null) parts.Add(commitLine);
        if (time.Length > 0) parts.Add(time);
        AutomationProperties.SetName(button, string.Join(", ", parts));
        button.Click += (_, _) => OpenNotification(item);
        return button;
    }

    /// <summary>A row was clicked: reservation kinds open the project, a published commit opens the commit; nothing happens when Projects is unavailable to this account.</summary>
    private void OpenNotification(NotificationItem item)
    {
        _flyout.Hide();
        if (_isSigningOut() || !_projectsAvailable()) return;
        if (item.Kind == NotificationKind.CommitPublished && item.Commit is { } commit) _openCommit(item.ProjectId, item.ProjectName, commit.Id);
        else _openProject(item.ProjectId, item.ProjectName);
    }
}
