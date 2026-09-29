using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace MolHub.Windows;

/// <summary>
/// The first native writes, on the project screen's Work reservation card: Start work, Cancel work and Publish
/// version (a form on the project's own stack). The database guards decide; the app sends each write once, refreshes
/// the project afterwards and explains the answer. An unknown outcome is never resent automatically.
/// </summary>
internal sealed partial class ProjectsView
{
    private const string StartGlyph = "\uE768";
    private const string PublishGlyph = "\uE898";
    private const string CancelGlyph = "\uE711";
    private const string NoteGlyph = "\uE946";

    /// <summary>Opens a project's publish form directly (from the Dashboard), with the list and project underneath.</summary>
    public void OpenPublish(string projectId, string name)
    {
        if (!ProjectsModel.IsValidId(projectId)) return;
        if (_stack.Count == 0) _stack.Add(RootScreen());
        _stack.RemoveRange(1, _stack.Count - 1);
        PushProject(projectId, name);
        PushPublish(projectId, name);
    }

    /// <summary>
    /// Makes the first screen and any open project reload when shown again (a write elsewhere changed reservations): every
    /// load state is invalidated, so an answer to a load that started before is shown but never counts as fresh.
    /// </summary>
    public void MarkStale()
    {
        _list.Invalidate();
        foreach (var screen in _stack)
        {
            screen.State.Invalidate();
            screen.Timeline?.Invalidate();
        }
    }

    /// <summary>The actions under the reservation state: Start work when free; Publish version and Cancel work when it is yours.</summary>
    private FrameworkElement WorkActions(Screen screen, ProjectSummary project)
    {
        var panel = new StackPanel { Spacing = 10 };
        var state = ProjectsModel.StateFor(project.Reservation, _user.Username);
        var buttons = new InlineWrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var busy = BusyRing();
        var all = new List<Button>();

        void SetBusy(bool value)
        {
            foreach (var button in all) button.IsEnabled = !value;
            busy.IsActive = value;
            busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task RunAsync(string command, System.Text.Json.Nodes.JsonObject? payload, string successMessage)
        {
            if (payload is null) return;
            // One write at a time for the whole app; the buttons stay enabled and explain a refusal instead of doing nothing.
            if (!_gate.TryEnter())
            {
                ShowBusyNotice();
                return;
            }
            SetBusy(true);
            bool refreshed;
            try
            {
                refreshed = await RunWorkWriteAsync(screen, project.Name, command, payload, successMessage);
            }
            finally
            {
                _gate.Exit();
            }
            // After a successful refresh this card has been replaced by a fresh one. If the refresh failed, this card shows
            // an old state: keep its buttons disabled (Refresh rebuilds the card).
            if (refreshed) SetBusy(false);
            else busy.Visibility = Visibility.Collapsed;
        }

        if (state == ReservationState.Available)
        {
            var start = ActionButton(L("Work_Start"), StartGlyph, accent: true);
            start.Click += async (_, _) => await RunAsync("startReservation", WorkModel.StartPayload(project.Id, project.HeadId),
                string.Format(L("Work_StartedFormat"), project.Name));
            all.Add(start);
        }
        else if (state == ReservationState.Yours)
        {
            var publish = ActionButton(L("Work_PublishShort"), PublishGlyph, accent: true);
            publish.Click += (_, _) =>
            {
                if (_gate.IsBusy) ShowBusyNotice();
                else PushPublish(project.Id, project.Name);
            };
            var cancel = ActionButton(L("Work_Cancel"), CancelGlyph, accent: false);
            cancel.Click += async (_, _) => await RunAsync("cancelReservation", WorkModel.CancelPayload(project.Id),
                string.Format(L("Work_CancelledFormat"), project.Name));
            all.Add(publish);
            all.Add(cancel);
        }

        foreach (var button in all) buttons.Children.Add(button);
        if (all.Count > 0)
        {
            buttons.Children.Add(busy);
            panel.Children.Add(buttons);
        }

        var note = new Grid { ColumnSpacing = 8 };
        note.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        note.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = PageParts.Glyph(NoteGlyph, 12);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        note.Children.Add(icon);
        var text = PageParts.Caption(L("Work_Note"));
        Grid.SetColumn(text, 1);
        note.Children.Add(text);
        panel.Children.Add(note);
        return panel;
    }

    private ProgressRing BusyRing()
    {
        var ring = new ProgressRing { IsActive = false, Width = 16, Height = 16, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(ring, L("Work_Sending"));
        return ring;
    }

    private static Button ActionButton(string text, string glyph, bool accent)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, FontFamily = PageParts.SymbolFont });
        content.Children.Add(new TextBlock { Text = text });
        var button = new Button { Content = content };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        AutomationProperties.SetName(button, text);
        return button;
    }

    /// <summary>"Project: message" so a result shown later on another screen still says which project it is about.</summary>
    private static string Named(string projectName, string message) => $"{projectName}: {message}";

    /// <summary>
    /// Start or cancel work: send once, refresh the project, then explain the result. The caller holds the write gate.
    /// Returns whether the project was refreshed after the write (when it was not, the card still shows the old state).
    /// </summary>
    private async Task<bool> RunWorkWriteAsync(Screen screen, string projectName, string command, System.Text.Json.Nodes.JsonObject payload, string successMessage)
    {
        var outcome = WorkModel.Explain(await _request(command, payload));
        _workChanged();
        var refreshed = await ReloadProjectAsync(screen);
        var stale = refreshed ? string.Empty : " " + L("Work_NotRefreshed");
        switch (outcome.Kind)
        {
            case WriteOutcomeKind.Applied:
                ShowWriteNotice(screen, InfoBarSeverity.Success, L("Work_DoneTitle"), successMessage + stale);
                break;
            case WriteOutcomeKind.Unknown:
                ShowWriteNotice(screen, InfoBarSeverity.Warning, L(outcome.TitleKey),
                    Named(projectName, L(refreshed ? "Work_UnknownReservation" : "Work_Unknown")));
                break;
            default:
                ShowWriteNotice(screen, InfoBarSeverity.Error, L(outcome.TitleKey), Named(projectName, L(outcome.MessageKey)) + stale, outcome.SessionEnded);
                break;
        }
        return refreshed;
    }

    private void ShowBusyNotice(Screen? screen = null) =>
        ShowWriteNotice(screen ?? Current, InfoBarSeverity.Informational, L(_gate.RefusedTitleKey), L(_gate.RefusedMessageKey));

    /// <summary>
    /// A write result on the InfoBar of whatever screen is shown now (the user may have moved on meanwhile); when this page
    /// is not on screen, the result waits and is shown with the next screen displayed. Warnings and errors stay until
    /// dismissed, so an unconfirmed or refused write is never left unexplained.
    /// </summary>
    private void ShowWriteNotice(Screen? screen, InfoBarSeverity severity, string title, string message, bool sessionEnded = false)
    {
        _pendingWriteNotice = (severity, title, message, sessionEnded);
        if (IsLoaded && Current is not null) ShowPendingWriteNotice();
    }

    private void ShowPendingWriteNotice()
    {
        if (_pendingWriteNotice is not { } notice) return;
        _pendingWriteNotice = null;
        _statusBar.Severity = notice.Severity;
        _statusBar.Title = notice.Title;
        _statusBar.Message = notice.Message;
        Button? action = null;
        if (notice.SessionEnded)
        {
            action = new Button { Content = L("Dashboard_SignInAgain") };
            action.Click += (_, _) => _signInAgain();
        }
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
        _stickyNotice = notice.Severity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
        if (FrameworkElementAutomationPeer.FromElement(_statusBar) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
                $"{notice.Title}. {notice.Message}", "WorkWrite");
        }
    }

    /// <summary>Closes the "Check the form" notice once the form is valid; any other notice shown since then stays.</summary>
    private void CloseFormNotice()
    {
        if (_pendingWriteNotice is { } pending && pending.Title == L("Work_FormInvalidTitle")) _pendingWriteNotice = null;
        if (!_statusBar.IsOpen || _statusBar.Title != L("Work_FormInvalidTitle") || _statusBar.Message != L("Work_FormInvalid")) return;
        _statusBar.IsOpen = false;
        _stickyNotice = false;
    }

    private bool _stickyNotice;
    private (InfoBarSeverity Severity, string Title, string Message, bool SessionEnded)? _pendingWriteNotice;

    // ----- Publish version -----

    private void PushPublish(string projectId, string name)
    {
        if (!ProjectsModel.IsValidId(projectId)) return;
        var existing = _stack.FindIndex(s => s.Kind == ScreenKind.Publish && s.Id == projectId);
        if (existing >= 0)
        {
            PopTo(existing);
            return;
        }
        var screen = new Screen(ScreenKind.Publish, projectId, L("Work_PublishShort"));
        screen.Load = () => LoadPublishAsync(screen, name);
        Push(screen);
    }

    /// <summary>Reads the project fresh, so the form always publishes against the current reservation base.</summary>
    private async Task LoadPublishAsync(Screen screen, string name)
    {
        var ticket = screen.State.Begin();
        var result = await _request("project", ProjectsModel.ProjectPayload(screen.Id));
        // A newer load of the same screen (Retry pressed twice) owns what is shown.
        if (!screen.State.IsCurrent(ticket)) return;
        var detail = result.Ok && result.Data is { } data ? ProjectsModel.ParseProjectDetail(data) : null;
        if (detail is null)
        {
            screen.State.Complete(ticket, success: false, DateTimeOffset.Now);
            ShowError(screen, result.Ok ? ProjectsError.Unexpected : ProjectsModel.ErrorFor(result), () => LoadPublishAsync(screen, name));
            return;
        }
        screen.State.Complete(ticket, success: true, DateTimeOffset.Now);
        SetContent(screen, WorkModel.StillReservedBy(detail.Project, _user.Username) ? BuildPublishForm(screen, detail) : NeedsReservation(detail.Project), null);
    }

    private FrameworkElement NeedsReservation(ProjectSummary project)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(PageParts.Heading(L("Work_PublishShort"), AutomationHeadingLevel.Level1, "TitleTextBlockStyle"));
        panel.Children.Add(_p.Secondary(string.Format(L("Work_NeedsReservationFormat"), project.Name)));
        var back = new Button { Content = L("Work_BackToProject") };
        back.Click += (_, _) => TryGoBack();
        panel.Children.Add(back);
        return panel;
    }

    private FrameworkElement BuildPublishForm(Screen screen, ProjectDetail detail)
    {
        var project = detail.Project;
        var reservationBase = project.Reservation?.Base;
        var root = new StackPanel { Spacing = 16, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(PageParts.Heading(L("Work_PublishShort"), AutomationHeadingLevel.Level1, "TitleTextBlockStyle"));
        root.Children.Add(_p.Secondary(project.Name));

        var basedOn = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        basedOn.Children.Add(Centered(PageParts.Caption(L("Projects_BasedOn"))));
        basedOn.Children.Add(reservationBase is { } baseId
            ? PageParts.VersionBadge(project.Latest is { } latest && latest.Id == baseId ? ProjectsModel.VersionLabel(latest.Version, latest.Id) : DashboardModel.ShortId(baseId))
            : Centered(PageParts.Caption(L("Projects_InitialVersion"))));
        root.Children.Add(basedOn);

        var title = new TextBox { Header = L("Work_TitleLabel"), MaxLength = WorkModel.MaxTitleLength };
        var version = new TextBox { Header = L("Work_VersionLabel"), MaxLength = WorkModel.MaxVersionLength, PlaceholderText = "v0.1.0", Description = L("Work_VersionHint") };
        var changes = new TextBox
        {
            Header = L("Work_ChangesLabel"),
            MaxLength = WorkModel.MaxChangesLength,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 160,
            MaxHeight = 360
        };
        ScrollViewer.SetVerticalScrollBarVisibility(changes, ScrollBarVisibility.Auto);
        var url = new TextBox { Header = L("Work_UrlLabel"), MaxLength = WorkModel.MaxUrlLength, InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Url) } } };
        foreach (var box in new[] { title, version, changes, url }) AutomationProperties.SetName(box, (string)box.Header);
        root.Children.Add(title);
        root.Children.Add(version);
        root.Children.Add(changes);
        root.Children.Add(url);
        root.Children.Add(PublishStorageNote());

        var publish = ActionButton(L("Work_Publish"), PublishGlyph, accent: true);
        var cancel = new Button { Content = L("Work_CancelForm") };
        AutomationProperties.SetName(cancel, L("Work_CancelForm"));
        cancel.Click += (_, _) => TryGoBack();
        var busy = BusyRing();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(publish);
        actions.Children.Add(cancel);
        actions.Children.Add(busy);
        root.Children.Add(actions);

        void ShowFieldErrors(PublishFieldError errors)
        {
            title.Description = errors.HasFlag(PublishFieldError.TitleMissing) ? L("Work_FieldRequired")
                : errors.HasFlag(PublishFieldError.TitleTooLong) ? string.Format(L("Work_FieldTooLongFormat"), WorkModel.MaxTitleLength) : null;
            version.Description = errors.HasFlag(PublishFieldError.VersionInvalid) ? L("Work_VersionInvalid") : L("Work_VersionHint");
            changes.Description = errors.HasFlag(PublishFieldError.ChangesMissing) ? L("Work_FieldRequired")
                : errors.HasFlag(PublishFieldError.ChangesTooLong) ? string.Format(L("Work_FieldTooLongFormat"), WorkModel.MaxChangesLength)
                : errors.HasFlag(PublishFieldError.TooLarge) ? L("Work_ErrorTooLarge") : null;
            url.Description = errors.HasFlag(PublishFieldError.UrlMissing) ? L("Work_FieldRequired")
                : errors.HasFlag(PublishFieldError.UrlInvalid) ? L("Work_ErrorInvalidUrl") : null;
        }

        // After a failed Publish, flagged fields are re-checked as the user edits, so fixed fields lose their error and the
        // form notice closes once nothing is flagged (a field that was fine gets no new error until the next Publish).
        var shownErrors = PublishFieldError.None;
        void Recheck()
        {
            if (shownErrors == PublishFieldError.None) return;
            shownErrors = WorkModel.StillShown(shownErrors, WorkModel.Validate(new PublishDraft(title.Text, version.Text, changes.Text, url.Text)));
            ShowFieldErrors(shownErrors);
            if (shownErrors == PublishFieldError.None) CloseFormNotice();
        }
        foreach (var box in new[] { title, version, changes, url }) box.TextChanged += (_, _) => Recheck();

        void SetBusy(bool value)
        {
            foreach (var control in new Control[] { title, version, changes, url, publish, cancel }) control.IsEnabled = !value;
            busy.IsActive = value;
            busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        publish.Click += async (_, _) =>
        {
            if (_gate.IsBusy)
            {
                ShowBusyNotice(screen);
                return;
            }
            var draft = new PublishDraft(title.Text, version.Text, changes.Text, url.Text);
            var errors = WorkModel.Validate(draft);
            ShowFieldErrors(errors);
            shownErrors = errors;
            if (errors != PublishFieldError.None)
            {
                ShowWriteNotice(screen, InfoBarSeverity.Error, L("Work_FormInvalidTitle"), L("Work_FormInvalid"));
                return;
            }
            if (WorkModel.PublishPayload(project.Id, reservationBase, draft) is not { } payload) return;
            if (!await ConfirmPublishAsync(project.Name, draft)) return;
            // From here to the explanation, no other write can start anywhere in the app.
            if (!_gate.TryEnter())
            {
                ShowBusyNotice(screen);
                return;
            }
            SetBusy(true);
            _stickyNotice = false;
            _statusBar.IsOpen = false;
            try
            {
                var outcome = WorkModel.Explain(await _request("publishCommit", payload));
                _workChanged();
                var projectScreen = _stack.FirstOrDefault(s => s.Kind == ScreenKind.Project && s.Id == project.Id);
                if (outcome.Kind == WriteOutcomeKind.Applied)
                {
                    var label = draft.Version.Trim();
                    var message = label.Length > 0 ? string.Format(L("Work_PublishedVersionFormat"), label, project.Name) : string.Format(L("Work_PublishedFormat"), project.Name);
                    var refreshed = projectScreen is not null && await ReloadProjectAsync(projectScreen);
                    if (!refreshed) message += " " + L("Work_NotRefreshed");
                    if (projectScreen is not null && Current == screen) PopTo(_stack.IndexOf(projectScreen));
                    ShowWriteNotice(projectScreen ?? screen, InfoBarSeverity.Success, L("Work_DoneTitle"), message);
                    return;
                }

                if (outcome.Kind == WriteOutcomeKind.Unknown)
                {
                    // Publishing ends the reservation in the same database write, so the refreshed reservation tells which case is likely.
                    var refreshed = await _request("project", ProjectsModel.ProjectPayload(project.Id));
                    var fresh = refreshed.Ok && refreshed.Data is { } data ? ProjectsModel.ParseProjectDetail(data)?.Project : null;
                    if (projectScreen is not null) _ = ReloadProjectAsync(projectScreen);
                    var stillReserved = fresh is not null && WorkModel.StillReservedBy(fresh, _user.Username);
                    var messageKey = fresh is null ? "Work_UnknownNotRefreshed" : stillReserved ? "Work_UnknownStillReserved" : "Work_UnknownReservationEnded";
                    SetBusy(false);
                    // Without a reservation the server would refuse a new publish anyway; offer it only while the reservation is still yours.
                    publish.IsEnabled = stillReserved;
                    ShowWriteNotice(screen, InfoBarSeverity.Warning, L(outcome.TitleKey), Named(project.Name, L(messageKey)));
                    return;
                }

                if (projectScreen is not null) _ = ReloadProjectAsync(projectScreen);
                SetBusy(false);
                ShowWriteNotice(screen, InfoBarSeverity.Error, L(outcome.TitleKey), Named(project.Name, L(outcome.MessageKey)), outcome.SessionEnded);
            }
            finally
            {
                _gate.Exit();
            }
        };
        return root;
    }

    /// <summary>The web's storage note for the publish form: MolHub keeps the link; access is managed by the storage provider.</summary>
    private Border PublishStorageNote()
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = PageParts.Glyph(LockGlyph, 14);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        row.Children.Add(icon);
        var text = PageParts.Caption(L("Work_StorageNote"));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return new Border { Style = PageParts.Res("DashboardCardStyle"), Child = row };
    }

    private const string LockGlyph = "\uE72E";

    /// <summary>Publishing cannot be undone and ends the reservation, so it is confirmed first.</summary>
    private async Task<bool> ConfirmPublishAsync(string projectName, PublishDraft draft)
    {
        var label = draft.Version.Trim().Length > 0 ? draft.Version.Trim() : draft.Title.Trim();
        var dialog = new ContentDialog
        {
            Title = L("Work_ConfirmTitle"),
            Content = new TextBlock { Text = string.Format(L("Work_ConfirmFormat"), label, projectName), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = L("Work_Publish"),
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
            // Another dialog is already open; do not publish without a confirmation.
            return false;
        }
    }
}
