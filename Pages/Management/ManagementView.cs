using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace MolHub.Windows;

/// <summary>
/// Project management (`manageProjects`, `manageProject`) for approved project owners and site admins, following the
/// web manage panel: a project picker, then the project's state and administrator, members, work reservation,
/// approval requests, Discord destinations and activity, with the web's management writes (`ManageWriteModel`):
/// add/remove members, release the reservation, deletion/restoration requests (owner only), Discord destinations and,
/// for site admins, the project administrator. Each write is sent once, the project is reloaded and the answer is
/// explained; an unknown outcome is never resent automatically.
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
    private readonly AuthenticatedUser _user;
    private readonly Func<string, JsonObject?, Task<BridgeResult>> _request;
    private readonly Action _signInAgain;
    private readonly WriteGate _gate;
    private readonly Action _workChanged;
    private readonly InfoBar _statusBar = new() { IsClosable = true, IsOpen = false };
    private readonly ProgressBar _busy = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl _detail = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _picker = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _limitNote;
    private readonly FrameworkElement _content;
    private readonly Button _refresh;
    private IReadOnlyList<ManagedProject> _projects = [];
    private string? _selected;
    private bool _updatingPicker;
    // The picker list and the chosen project have independent freshness and request tickets.
    private readonly LoadState _listLoad = new();
    private readonly LoadState _detailLoad = new();

    public ManagementView(Func<string, string> localize, string language, AuthenticatedUser user,
        Func<string, JsonObject?, Task<BridgeResult>> request, Action signInAgain, WriteGate gate, Action workChanged)
    {
        _p = new PageParts(localize);
        _language = language;
        _user = user;
        _request = request;
        _signInAgain = signInAgain;
        _gate = gate;
        _workChanged = workChanged;
        AutomationProperties.SetName(_busy, L("Work_Sending"));

        _refresh = _p.SubtleButton(L("Dashboard_Refresh"), RefreshGlyph, () => _ = LoadAsync());
        // Toolbar: the project picker (shown only while the body is the content) and Refresh (always visible).
        var toolbar = new Grid { ColumnSpacing = 12 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(_picker);
        _refresh.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(_refresh, 1);
        toolbar.Children.Add(_refresh);

        _picker.Visibility = Visibility.Collapsed;
        _picker.Header = L("History_ProjectFilter");
        AutomationProperties.SetName(_picker, L("History_ProjectFilter"));
        _picker.SelectionChanged += (_, _) =>
        {
            if (_updatingPicker || _picker.SelectedItem is not ComboBoxItem { Tag: string id } || id == _selected) return;
            _selected = id;
            _detailLoad.Reset();
            _ = LoadDetailAsync(_detailLoad.Begin());
        };
        _limitNote = PageParts.Caption(L("Manage_FirstProjectsOnly"));
        _limitNote.Visibility = Visibility.Collapsed;

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(_limitNote);
        content.Children.Add(_detail);
        _content = content;

        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32, 28, 32, 32) };
        root.Children.Add(toolbar);
        root.Children.Add(_statusBar);
        root.Children.Add(_busy);
        root.Children.Add(_body);
        Content = PageParts.CenteredPage(root, 960);
    }

    private string L(string key) => _p.L(key);

    /// <summary>Reloads the next time the page is shown (a write changed a reservation).</summary>
    public void MarkStale()
    {
        _listLoad.Invalidate();
        _detailLoad.Invalidate();
    }

    /// <summary>Loads on first display and again when the data is older than a minute (the chosen project is kept).</summary>
    public Task EnsureLoadedAsync()
    {
        var now = DateTimeOffset.Now;
        if (_listLoad.NeedsLoad(now, StaleAfter)) return LoadAsync();
        return _selected is not null && _detailLoad.NeedsLoad(now, StaleAfter)
            ? LoadDetailAsync(_detailLoad.Begin())
            : Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        var ticket = _listLoad.Begin();
        if (!_listLoad.HasContent)
        {
            _picker.Visibility = Visibility.Collapsed;
            _body.Content = _p.LoadingIndicator("Manage_Loading");
        }
        _refresh.IsEnabled = false;
        var result = await _request("manageProjects", ManagementModel.ListPayload());
        if (!_listLoad.IsCurrent(ticket)) return;

        var list = result.Ok && result.Data is { } data ? ManagementModel.ParseList(data, result.Meta) : null;
        if (list is null)
        {
            _listLoad.Complete(ticket, success: false, DateTimeOffset.Now);
            _refresh.IsEnabled = true;
            ShowError(result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result));
            if (!_listLoad.HasContent)
            {
                _picker.Visibility = Visibility.Collapsed;
                _body.Content = null;
            }
            return;
        }

        _projects = list.Items;
        _listLoad.Complete(ticket, success: true, DateTimeOffset.Now);
        _limitNote.Visibility = list.NextOffset is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_projects.Count == 0)
        {
            // Ownership can also come from deleted projects only on the server side; the list decides.
            _refresh.IsEnabled = true;
            _statusBar.IsOpen = false;
            _selected = null;
            _detailLoad.Reset();
            _detail.Content = null;
            _picker.Visibility = Visibility.Collapsed;
            _body.Content = _p.Secondary(L("Manage_None"));
            return;
        }

        _selected = ManagementModel.Select(_projects, _selected);
        FillPicker();
        _picker.Visibility = Visibility.Visible;
        _body.Content = _content;
        _detailLoad.Reset();
        await LoadDetailAsync(_detailLoad.Begin());
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

    /// <summary>Loads the chosen project; returns whether it is now shown (false when failed or superseded).</summary>
    private async Task<bool> LoadDetailAsync(LoadTicket ticket, bool keepContent = false)
    {
        if (_selected is not { } projectId || ManagementModel.DetailPayload(projectId) is not { } payload)
        {
            _detailLoad.Complete(ticket, success: false, DateTimeOffset.Now);
            return false;
        }
        // The picker stays enabled (keyboard focus stays on it); the ticket drops superseded replies.
        _refresh.IsEnabled = false;
        // After a write the old cards stay visible (disabled) until the reload arrives, so the page does not jump.
        if (!keepContent) _detail.Content = _p.LoadingIndicator("Manage_Loading");
        var result = await _request("manageProject", payload);
        if (!_detailLoad.IsCurrent(ticket)) return false;
        _refresh.IsEnabled = true;

        var detail = result.Ok && result.Data is { } data ? ManagementModel.ParseDetail(data) : null;
        if (detail is null)
        {
            _detailLoad.Complete(ticket, success: false, DateTimeOffset.Now);
            ShowError(result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result));
            _detail.Content = null;
            return false;
        }
        _detailLoad.Complete(ticket, success: true, DateTimeOffset.Now);
        _statusBar.IsOpen = false;
        _detail.Content = BuildDetail(detail);
        return true;
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
        if (ManageWriteModel.CanRequest(detail, _user.Username)) panel.Children.Add(LifecycleCard(detail));
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
        // Only site administrators appoint, replace or remove a project administrator (the web shows the form to them only).
        if (NativePageCatalog.CanAdminister(_user)) body.Children.Add(OwnerForm(detail));
        return _p.Card("History_ProjectFilter", ProjectGlyph, body);
    }

    private FrameworkElement OwnerForm(ManagedProjectDetail detail)
    {
        var form = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var picker = new ComboBox { Header = L("Manage_OwnerAssign"), MinWidth = 280 };
        AutomationProperties.SetName(picker, L("Manage_OwnerAssign"));
        var siteManaged = new ComboBoxItem { Content = L("Manage_SiteManaged"), Tag = string.Empty };
        AutomationProperties.SetName(siteManaged, L("Manage_SiteManaged"));
        picker.Items.Add(siteManaged);
        if (detail.Project.OwnerId is null) picker.SelectedItem = siteManaged;
        foreach (var account in ManageWriteModel.OwnerCandidates(detail))
        {
            var item = new ComboBoxItem { Content = account.Username, Tag = account.Id };
            AutomationProperties.SetName(item, account.Username);
            picker.Items.Add(item);
            if (account.Id == detail.Project.OwnerId) picker.SelectedItem = item;
        }
        form.Children.Add(picker);
        form.Children.Add(PageParts.Caption(L("Manage_OwnerHint")));

        var save = new Button { Content = L("Manage_Save"), IsEnabled = false };
        AutomationProperties.SetName(save, L("Manage_OwnerSave"));
        string? Chosen() => picker.SelectedItem is ComboBoxItem { Tag: string id } ? (id.Length == 0 ? null : id) : detail.Project.OwnerId;
        // Save is offered only for a different choice, so nothing is sent without a change.
        picker.SelectionChanged += (_, _) => save.IsEnabled = picker.SelectedItem is ComboBoxItem && Chosen() != detail.Project.OwnerId;
        save.Click += (_, _) =>
        {
            var chosen = Chosen();
            var name = chosen is null ? L("Manage_SiteManaged") : ((ComboBoxItem)picker.SelectedItem).Content as string ?? string.Empty;
            _ = RunWriteAsync(detail, "assignProjectOwner", ManageWriteModel.AssignOwnerPayload(detail.Project.Id, chosen),
                _ => string.Format(L("Manage_OwnerSavedFormat"), detail.Project.Name, name));
        };
        form.Children.Add(save);
        return form;
    }

    private Border MembersCard(ManagedProjectDetail detail)
    {
        var body = CardBody();
        body.Children.Add(PageParts.Caption(L("Manage_MembershipNote")));
        var candidates = ManageWriteModel.AddCandidates(detail);
        if (candidates.Count > 0) body.Children.Add(AddMemberForm(detail, candidates));
        if (detail.Members.Count == 0) body.Children.Add(Empty());
        foreach (var member in detail.Members)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (ManageWriteModel.CanRemove(member, detail))
            {
                var remove = new Button { Content = L("Manage_Remove"), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(remove, string.Format(L("Manage_RemoveNameFormat"), member.Username));
                remove.Click += async (_, _) =>
                {
                    if (!await ConfirmAsync(L("Manage_Remove"), string.Format(L("Manage_ConfirmRemoveFormat"), member.Username, detail.Project.Name))) return;
                    await RunWriteAsync(detail, "removeMember", ManageWriteModel.RemoveMemberPayload(detail.Project.Id, member.Id),
                        _ => string.Format(L("Manage_RemovedFormat"), member.Username, detail.Project.Name));
                };
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
            }
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

    private FrameworkElement AddMemberForm(ManagedProjectDetail detail, IReadOnlyList<ManagedAccount> candidates)
    {
        var form = new InlineWrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var picker = new ComboBox { Header = L("Manage_ApprovedAccount"), MinWidth = 240, PlaceholderText = "—" };
        AutomationProperties.SetName(picker, L("Manage_ApprovedAccount"));
        foreach (var account in candidates)
        {
            var item = new ComboBoxItem { Content = account.Username, Tag = account.Id };
            AutomationProperties.SetName(item, account.Username);
            picker.Items.Add(item);
        }
        var add = new Button { Content = L("Manage_AddMember"), IsEnabled = false, VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetName(add, L("Manage_AddMember"));
        picker.SelectionChanged += (_, _) => add.IsEnabled = picker.SelectedItem is ComboBoxItem;
        add.Click += (_, _) =>
        {
            if (picker.SelectedItem is not ComboBoxItem { Tag: string id, Content: string name }) return;
            _ = RunWriteAsync(detail, "addMember", ManageWriteModel.AddMemberPayload(detail.Project.Id, id),
                _ => string.Format(L("Manage_AddedFormat"), name, detail.Project.Name));
        };
        form.Children.Add(picker);
        form.Children.Add(add);
        return form;
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
            if (ManageWriteModel.CanRelease(detail))
            {
                var release = new Button { Content = L("Manage_Release") };
                AutomationProperties.SetName(release, L("Manage_Release"));
                release.Click += async (_, _) =>
                {
                    if (!await ConfirmAsync(L("Manage_Release"), string.Format(L("Manage_ConfirmReleaseFormat"), reservation.Username, detail.Project.Name))) return;
                    await RunWriteAsync(detail, "releaseReservation", ManageWriteModel.ReleasePayload(detail.Project.Id),
                        _ => string.Format(L("Manage_ReleasedFormat"), detail.Project.Name));
                };
                body.Children.Add(release);
            }
        }
        return _p.Card("Manage_Reservation", ClockGlyph, body);
    }

    /// <summary>The web's danger zone for the project's own administrator: request deletion (or restoration when deleted).</summary>
    private Border LifecycleCard(ManagedProjectDetail detail)
    {
        var kind = ManageWriteModel.RequestKind(detail);
        var label = L(kind == "restore" ? "Manage_RequestRestore" : "Manage_RequestDelete");
        var body = CardBody();
        body.Children.Add(_p.Secondary(L("Manage_RequestHint")));
        var reason = new TextBox
        {
            Header = L("Manage_Reason"),
            MaxLength = ManageWriteModel.MaxReasonLength,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80
        };
        AutomationProperties.SetName(reason, L("Manage_Reason"));
        var error = FieldError();
        reason.TextChanged += (_, _) => ClearFieldError(error);
        var send = new Button { Content = label };
        AutomationProperties.SetName(send, label);
        send.Click += (_, _) =>
        {
            if (ManageWriteModel.RequestPayload(detail.Project.Id, kind, reason.Text) is not { } payload)
            {
                ShowFieldError(error, L("Manage_ReasonRequired"));
                return;
            }
            _ = RunWriteAsync(detail, "requestProjectChange", payload, _ => L("Manage_RequestSaved"), createsRecord: true);
        };
        body.Children.Add(reason);
        body.Children.Add(error);
        body.Children.Add(send);
        return _p.Card(kind == "restore" ? "Manage_RequestRestore" : "Manage_RequestDelete", RequestGlyph, body);
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
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 2 };
            var channel = link.ChannelName.Length > 0 ? "#" + link.ChannelName : link.ChannelId;
            text.Children.Add(new TextBlock { Text = channel, Style = PageParts.Res("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
            text.Children.Add(PageParts.Caption(Join($"{link.GuildId} / {link.ChannelId}", L(link.Enabled ? "Manage_Active" : "Manage_Status_cancelled"))));
            row.Children.Add(text);
            if (ManageWriteModel.CanStopDiscord(link))
            {
                var stop = new Button { Content = L("Manage_DiscordStop"), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(stop, string.Format(L("Manage_DiscordStopNameFormat"), channel));
                stop.Click += async (_, _) =>
                {
                    if (!await ConfirmAsync(L("Manage_DiscordStop"), string.Format(L("Manage_ConfirmStopFormat"), channel))) return;
                    await RunWriteAsync(detail, "disableProjectDiscord", ManageWriteModel.DisableDiscordPayload(detail.Project.Id, link.Id),
                        _ => string.Format(L("Manage_DiscordStoppedFormat"), channel));
                };
                Grid.SetColumn(stop, 1);
                row.Children.Add(stop);
            }
            body.Children.Add(row);
        }
        if (ManageWriteModel.CanLinkDiscord(detail)) body.Children.Add(DiscordForm(detail));
        return _p.Card("Manage_Discord", LinkGlyph, body);
    }

    /// <summary>
    /// Connect a destination, or request approval for a new one (the server decides). The web lists the Bot's servers and
    /// channels through setup endpoints that are not part of the native contract, so the ids are typed here.
    /// </summary>
    private FrameworkElement DiscordForm(ManagedProjectDetail detail)
    {
        var form = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        form.Children.Add(PageParts.Heading(L("Manage_DiscordConnect"), AutomationHeadingLevel.Level3));
        form.Children.Add(PageParts.Caption(L("Manage_DiscordIdsHint")));
        var guild = new TextBox { Header = L("Manage_DiscordGuild"), MaxLength = 20, InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } } };
        var channel = new TextBox { Header = L("Manage_DiscordChannel"), MaxLength = 20, InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } } };
        var locale = new ComboBox { Header = L("Manage_DiscordLocale"), MinWidth = 200 };
        foreach (var (code, key) in new[] { ("en", "Manage_LocaleEnglish"), ("ja", "Manage_LocaleJapanese") })
        {
            var item = new ComboBoxItem { Content = L(key), Tag = code };
            AutomationProperties.SetName(item, L(key));
            locale.Items.Add(item);
            if (code == ManageWriteModel.DefaultLocale(_language)) locale.SelectedItem = item;
        }
        var reason = new TextBox { Header = L("Manage_Reason"), MaxLength = ManageWriteModel.MaxReasonLength };
        var consent = new CheckBox { Content = new TextBlock { Text = L("Manage_DiscordConsent"), TextWrapping = TextWrapping.Wrap } };
        AutomationProperties.SetName(consent, L("Manage_DiscordConsent"));
        foreach (var box in new[] { guild, channel, reason }) AutomationProperties.SetName(box, (string)box.Header);
        AutomationProperties.SetName(locale, L("Manage_DiscordLocale"));

        var error = FieldError();
        foreach (var box in new[] { guild, channel, reason }) box.TextChanged += (_, _) => ClearFieldError(error);
        consent.Checked += (_, _) => ClearFieldError(error);
        var send = new Button { Content = L("Manage_DiscordConnect") };
        AutomationProperties.SetName(send, L("Manage_DiscordConnect"));
        send.Click += (_, _) =>
        {
            var draft = new DiscordDraft(guild.Text, channel.Text, locale.SelectedItem is ComboBoxItem { Tag: string code } ? code : "en", reason.Text, consent.IsChecked == true);
            var errors = ManageWriteModel.Validate(draft);
            if (errors != DiscordFieldError.None)
            {
                var messages = new List<string>();
                if (errors.HasFlag(DiscordFieldError.GuildInvalid) || errors.HasFlag(DiscordFieldError.ChannelInvalid)) messages.Add(L("Manage_DiscordIdInvalid"));
                if (errors.HasFlag(DiscordFieldError.ReasonMissing) || errors.HasFlag(DiscordFieldError.ReasonTooLong)) messages.Add(L("Manage_ReasonRequired"));
                if (errors.HasFlag(DiscordFieldError.ConsentMissing)) messages.Add(L("Manage_ConsentRequired"));
                ShowFieldError(error, string.Join(" ", messages));
                return;
            }
            _ = RunWriteAsync(detail, "linkProjectDiscord", ManageWriteModel.DiscordLinkPayload(detail.Project.Id, draft),
                result => L(ManageWriteModel.IsPending(result) ? "Manage_RequestSaved" : "Manage_DiscordSaved"), createsRecord: true);
        };
        form.Children.Add(guild);
        form.Children.Add(channel);
        form.Children.Add(locale);
        form.Children.Add(reason);
        form.Children.Add(consent);
        form.Children.Add(error);
        form.Children.Add(send);
        return form;
    }

    // ----- Writes -----

    private static TextBlock FieldError() => new() { Style = PageParts.Res("ManageFieldErrorTextStyle"), Visibility = Visibility.Collapsed };

    private static void ShowFieldError(TextBlock error, string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
        if ((FrameworkElementAutomationPeer.FromElement(error) ?? FrameworkElementAutomationPeer.CreatePeerForElement(error)) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "ManageFormCheck");
        }
    }

    /// <summary>A form's check result disappears as soon as the user edits the form again.</summary>
    private static void ClearFieldError(TextBlock error)
    {
        error.Text = string.Empty;
        error.Visibility = Visibility.Collapsed;
    }

    /// <summary>Removing, releasing and stopping are confirmed first, as on the web ("Cancel" is the default).</summary>
    private async Task<bool> ConfirmAsync(string action, string text)
    {
        if (_gate.IsBusy)
        {
            ShowNotice(InfoBarSeverity.Informational, L(_gate.RefusedTitleKey), L(_gate.RefusedMessageKey));
            return false;
        }
        var dialog = new ContentDialog
        {
            Title = L("Manage_ConfirmTitle"),
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = action,
            CloseButtonText = L("Work_CancelForm"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            // A code-built dialog needs the WinUI 3 style explicitly, or it gets the legacy template.
            Style = PageParts.Res("DefaultContentDialogStyle")
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            // Another dialog is already open; do nothing without a confirmation.
            return false;
        }
    }

    /// <summary>
    /// Sends one management write, reloads the project and explains the answer. One write at a time for the whole app;
    /// the controls are disabled meanwhile. An unknown outcome is explained after the reload and never resent.
    /// </summary>
    private async Task RunWriteAsync(ManagedProjectDetail detail, string command, JsonObject? payload, Func<BridgeResult, string> success, bool createsRecord = false)
    {
        if (payload is null) return;
        if (!_gate.TryEnter())
        {
            ShowNotice(InfoBarSeverity.Informational, L(_gate.RefusedTitleKey), L(_gate.RefusedMessageKey));
            return;
        }
        SetBusy(true);
        try
        {
            _statusBar.IsOpen = false;
            var result = await _request(command, payload);
            var outcome = ManageWriteModel.Explain(result, createsRecord);
            // Membership, reservations and ownership show on other pages too. This page reloads the project itself right
            // now, so returning to it meanwhile does not start a second, competing reload. The reload takes its ticket after
            // the invalidation, so its answer counts as fresh (never an unconditional "now").
            _workChanged();
            var refreshed = await LoadDetailAsync(_detailLoad.Begin(), keepContent: true);
            var stale = refreshed ? string.Empty : " " + L("Work_NotRefreshed");
            switch (outcome.Kind)
            {
                case WriteOutcomeKind.Applied:
                    ShowNotice(InfoBarSeverity.Success, L("Work_DoneTitle"), success(result) + stale);
                    break;
                case WriteOutcomeKind.Unknown:
                    ShowNotice(InfoBarSeverity.Warning, L(outcome.TitleKey), Named(detail.Project.Name, L(refreshed ? outcome.MessageKey : "Work_Unknown")));
                    break;
                default:
                    ShowNotice(InfoBarSeverity.Error, L(outcome.TitleKey), Named(detail.Project.Name, L(outcome.MessageKey)) + stale, outcome.SessionEnded);
                    break;
            }
        }
        finally
        {
            SetBusy(false);
            _gate.Exit();
        }
    }

    private static string Named(string projectName, string message) => $"{projectName}: {message}";

    private void SetBusy(bool value)
    {
        _busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        _detail.IsEnabled = !value;
        _picker.IsEnabled = !value;
        _refresh.IsEnabled = !value;
    }

    private void ShowNotice(InfoBarSeverity severity, string title, string message, bool sessionEnded = false)
    {
        _statusBar.Severity = severity;
        _statusBar.Title = title;
        _statusBar.Message = message;
        Button? action = null;
        if (sessionEnded)
        {
            action = new Button { Content = L("Dashboard_SignInAgain") };
            action.Click += (_, _) => _signInAgain();
        }
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
        if (FrameworkElementAutomationPeer.FromElement(_statusBar) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
                $"{title}. {message}", "ManageWrite");
        }
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
