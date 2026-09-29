using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MolHub.Windows;

/// <summary>
/// Members writes (v0.14.7): status changes and one-time reset codes, rules in <see cref="AdminUserWriteModel"/>. Every
/// action is confirmed (Cancel is the default), sent once through the app-wide <see cref="WriteGate"/> and followed by a
/// Members reload; an unknown outcome is explained and never resent. An issued code is shown once in its own dialog and
/// is not kept, logged or put in a notice.
/// </summary>
internal sealed partial class AdministrationView
{
    private readonly Func<AuthenticatedUser> _currentUser;
    private readonly WriteGate _gate;
    private readonly Action _workChanged;
    private readonly ProgressBar _writeProgress = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private bool _memberWriteBusy;
    private MemberNotice? _pendingMemberNotice;
    private bool _memberNoticeShown;

    private sealed record MemberNotice(InfoBarSeverity Severity, string Title, string Message, bool SessionEnded = false);

    private FrameworkElement? MemberActionsPanel(AdminMember member, MemberActions actions, string statusLabel)
    {
        if (!actions.Any) return null;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (actions.Primary is { } primary)
        {
            var button = new Button { Content = L(AdminUserWriteModel.ActionKey(primary)) };
            AutomationProperties.SetName(button, ActionName(primary, member.Username, statusLabel));
            button.Click += (_, _) => _ = RunMemberActionAsync(member, primary);
            panel.Children.Add(button);
        }
        if (actions.Overflow.Count > 0)
        {
            var menu = new MenuFlyout();
            foreach (var action in actions.Overflow)
            {
                var item = new MenuFlyoutItem { Text = L(AdminUserWriteModel.ActionKey(action)) };
                if (AdminUserWriteModel.IsDanger(action)) item.Style = PageParts.Res("AdminDangerMenuFlyoutItemStyle");
                AutomationProperties.SetName(item, ActionName(action, member.Username, statusLabel));
                item.Click += (_, _) => _ = RunMemberActionAsync(member, action);
                menu.Items.Add(item);
            }
            var more = new Button { Content = new SymbolIcon(Symbol.More), Flyout = menu };
            AutomationProperties.SetName(more, string.Format(L("Admin_MoreActionsForFormat"), member.Username, statusLabel));
            ToolTipService.SetToolTip(more, L("Admin_MoreActions"));
            panel.Children.Add(more);
        }
        return panel;
    }

    private string ActionName(MemberAction action, string username, string statusLabel) =>
        string.Format(L("Admin_ActionNameFormat"), L(AdminUserWriteModel.ActionKey(action)), username, statusLabel);

    /// <summary>Names the account and the effect; Reject, Suspend and a new reset code add a warning. Cancel is the default.</summary>
    private async Task<bool> ConfirmMemberActionAsync(AdminMember member, MemberAction action)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = string.Format(L("Admin_Confirm_" + action), member.Username), TextWrapping = TextWrapping.Wrap });
        if (AdminUserWriteModel.NeedsWarning(action))
        {
            content.Children.Add(new InfoBar
            {
                Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false, Title = L("Admin_ConfirmWarningTitle"),
                Message = L(action == MemberAction.IssueResetCode ? "Admin_ConfirmWarningReset" : "Admin_ConfirmWarningAccess")
            });
        }
        var dialog = new ContentDialog
        {
            Title = string.Format(L("Admin_ConfirmTitleFormat"), L(AdminUserWriteModel.ActionKey(action)), member.Username),
            Content = content,
            PrimaryButtonText = L(AdminUserWriteModel.ActionKey(action)),
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
    /// Confirms, sends the write once, reloads Members from the first page and explains the answer. One write at a time
    /// for the whole app; Members actions, Refresh, Load more and section switching are disabled meanwhile.
    /// </summary>
    private async Task RunMemberActionAsync(AdminMember member, MemberAction action)
    {
        if (_gate.IsBusy)
        {
            ShowMemberNotice(new(InfoBarSeverity.Informational, L(_gate.RefusedTitleKey), L(_gate.RefusedMessageKey)));
            return;
        }
        var payload = AdminUserWriteModel.Payload(action, member.Id);
        if (payload is null) return;
        var dialogRoot = XamlRoot;
        if (!await ConfirmMemberActionAsync(member, action)) return;
        if (!_gate.TryEnter())
        {
            ShowMemberNotice(new(InfoBarSeverity.Informational, L(_gate.RefusedTitleKey), L(_gate.RefusedMessageKey)));
            return;
        }
        IssuedResetCode? issued = null;
        SetMemberWriteBusy(true);
        try
        {
            _status.IsOpen = false;
            BridgeResult result;
            try
            {
                result = await _request(AdminUserWriteModel.Command(action), payload);
            }
            catch
            {
                // The request may have left the app: the outcome is unknown and it is never resent.
                result = BridgeResult.HostFailure(string.Empty, "SERVER_ERROR", false, BridgeOutcome.Unknown);
            }
            WriteOutcome outcome;
            if (action == MemberAction.IssueResetCode) (outcome, issued) = AdminUserWriteModel.ExplainReset(result);
            else outcome = AdminUserWriteModel.Explain(result, action);
            // Account status shows on other pages too; Members reloads itself right now.
            _workChanged();
            var refreshed = await LoadSectionAsync("Members", false);
            ShowMemberNotice(Notice(member, action, outcome, refreshed));
        }
        finally
        {
            SetMemberWriteBusy(false);
            _gate.Exit();
        }
        if (issued is null) return;
        var shown = await ShowResetCodeAsync(dialogRoot, member.Username, issued);
        issued = null;
        if (!shown) ShowMemberNotice(new(InfoBarSeverity.Warning, L("Work_UnknownTitle"), Named(member.Username, L("Admin_Write_ResetNotShown"))));
    }

    private MemberNotice Notice(AdminMember member, MemberAction action, WriteOutcome outcome, bool refreshed)
    {
        var stale = refreshed ? string.Empty : " " + L("Admin_Write_NotRefreshed");
        switch (outcome.Kind)
        {
            case WriteOutcomeKind.Applied:
                var done = AdminUserWriteModel.TargetStatus(action) is { } status
                    ? string.Format(L("Admin_Write_StatusDoneFormat"), member.Username, StatusLabel(status))
                    : string.Format(L("Admin_Write_ResetDoneFormat"), member.Username);
                return new(InfoBarSeverity.Success, L("Work_DoneTitle"), done + stale);
            case WriteOutcomeKind.Unknown:
                var message = L(outcome.MessageKey);
                if (refreshed && action != MemberAction.IssueResetCode
                    && _members?.Items.FirstOrDefault(item => item.Id == member.Id) is { } current)
                {
                    message += " " + string.Format(L("Admin_Write_CurrentStatusFormat"), StatusLabel(current.Status));
                }
                return new(InfoBarSeverity.Warning, L(outcome.TitleKey), Named(member.Username, message) + stale);
            default:
                return new(InfoBarSeverity.Error, L(outcome.TitleKey), Named(member.Username, L(outcome.MessageKey)) + stale, outcome.SessionEnded);
        }
    }

    private static string Named(string username, string message) => $"{username}: {message}";

    private void SetMemberWriteBusy(bool busy)
    {
        _memberWriteBusy = busy;
        _writeProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _content.IsEnabled = !busy;
        _sections.IsEnabled = !busy;
        UpdateControls();
    }

    /// <summary>A write result waits until Members is shown again and then stays until dismissed.</summary>
    private void ShowMemberNotice(MemberNotice notice)
    {
        _pendingMemberNotice = notice;
        ShowPendingMemberNotice();
    }

    private void ShowPendingMemberNotice()
    {
        if (_pendingMemberNotice is not { } notice || _selected != "Members" || !IsLoaded) return;
        _pendingMemberNotice = null;
        _status.Severity = notice.Severity;
        _status.Title = notice.Title;
        _status.Message = notice.Message;
        Button? action = null;
        if (notice.SessionEnded)
        {
            action = new Button { Content = L("Dashboard_SignInAgain") };
            action.Click += (_, _) => _signInAgain();
        }
        _status.ActionButton = action;
        _status.IsOpen = true;
        _memberNoticeShown = true;
        if (FrameworkElementAutomationPeer.FromElement(_status) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
                $"{notice.Title}. {notice.Message}", "AdminMemberWrite");
        }
    }

    /// <summary>
    /// Shows an issued code once: account, selectable code, expiry and an explicit Copy. Enter does not close it. The code
    /// is cleared from the controls when the dialog closes and is never copied automatically.
    /// </summary>
    private async Task<bool> ShowResetCodeAsync(XamlRoot? root, string username, IssuedResetCode issued)
    {
        if (root is null) return false;
        var codeBox = new TextBox
        {
            Text = issued.Code, IsReadOnly = true, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false, TextWrapping = TextWrapping.Wrap,
            Header = L("Admin_ResetCodeLabel"), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas")
        };
        AutomationProperties.SetName(codeBox, L("Admin_ResetCodeLabel"));
        var copied = new TextBlock { Style = PageParts.Res("DashboardCaptionTextStyle"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLiveSetting(copied, AutomationLiveSetting.Polite);
        var copy = new Button { Content = L("Admin_ResetCodeCopy") };
        copy.Click += (_, _) =>
        {
            copied.Text = L(CopyCode(codeBox.Text) ? "Admin_ResetCodeCopied" : "Admin_ResetCodeCopyFailed");
            FrameworkElementAutomationPeer.CreatePeerForElement(copied)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        };
        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        copyRow.Children.Add(copy); copyRow.Children.Add(copied);
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = L("Admin_ResetCodeReady"), Style = PageParts.Res("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_p.Secondary(string.Format(L("Admin_ResetCodeAccountFormat"), username)));
        content.Children.Add(codeBox);
        content.Children.Add(copyRow);
        content.Children.Add(_p.Secondary(string.Format(L("Admin_ResetCodeExpiresFormat"), Date(issued.ExpiresAt))));
        content.Children.Add(_p.Secondary(L("Admin_ResetCodeExpiresNote")));
        content.Children.Add(_p.Secondary(L("Admin_ResetCodeOnce")));
        var dialog = new ContentDialog
        {
            Title = L("Admin_ResetCodeTitle"),
            Content = content,
            CloseButtonText = L("Close"),
            // No default button: an accidental Enter must not dismiss a code that cannot be shown again.
            DefaultButton = ContentDialogButton.None,
            XamlRoot = root,
            RequestedTheme = ActualTheme,
            Style = PageParts.Res("DefaultContentDialogStyle")
        };
        try
        {
            await dialog.ShowAsync();
            return true;
        }
        catch
        {
            // Another dialog is already open; the code is not shown anywhere else.
            return false;
        }
        finally
        {
            codeBox.Text = string.Empty;
            copied.Text = string.Empty;
            content.Children.Clear();
            dialog.Content = null;
        }
    }

    /// <summary>Copies only on the explicit button, kept out of clipboard history and roaming.</summary>
    private static bool CopyCode(string code)
    {
        if (code.Length == 0) return false;
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(code);
            return Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false });
        }
        catch
        {
            return false;
        }
    }
}
