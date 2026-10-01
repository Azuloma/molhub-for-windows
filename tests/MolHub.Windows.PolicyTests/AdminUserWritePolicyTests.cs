using System.Text.Json;
using System.Text.RegularExpressions;
using MolHub.Windows;

/// <summary>v0.14.7 Administration Members writes: action matrix, payloads, reset-code parsing, result mapping and source policy.</summary>
internal static class AdminUserWritePolicyTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Administration Members writes: " + message);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static BridgeResult Write(int status, bool ok, string? code, BridgeOutcome outcome, string? data = null) =>
        new("r1", status, ok, data is null ? null : Json(data), null, code, null, false, outcome);

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = from >= 0 ? text.IndexOf(end, from + start.Length, StringComparison.Ordinal) : -1;
        return from >= 0 && to > from ? text[from..to] : "";
    }

    public static void Run(string sourceRoot, string administrationCode, string shell)
    {
        // ----- Action matrix (spec table; the server's allowed transitions) -----
        static AdminMember Member(string status, string role = "member", string id = "u2", string name = "bea") => new(id, name, status, role, null);
        static bool Is(MemberActions actions, MemberAction? primary, params MemberAction[] overflow) =>
            actions.Primary == primary && actions.Overflow.SequenceEqual(overflow) && actions.Any == (primary is not null || overflow.Length > 0);
        Check(Is(AdminUserWriteModel.Actions(Member("pending"), "azunel"), MemberAction.Approve, MemberAction.Reject, MemberAction.Suspend, MemberAction.IssueResetCode),
            "pending: Approve, then Reject, Suspend, Issue reset code.");
        Check(Is(AdminUserWriteModel.Actions(Member("approved"), "azunel"), MemberAction.IssueResetCode, MemberAction.Suspend), "approved: Issue reset code, then Suspend.");
        Check(Is(AdminUserWriteModel.Actions(Member("rejected"), "azunel"), MemberAction.Approve, MemberAction.Suspend), "rejected: Approve, then Suspend.");
        Check(Is(AdminUserWriteModel.Actions(Member("suspended"), "azunel"), MemberAction.Approve), "suspended: Approve only.");
        foreach (var unknown in new[] { "", "future_status", "Approved", "PENDING" })
            Check(Is(AdminUserWriteModel.Actions(Member(unknown), "azunel"), null), $"unknown status '{unknown}' has no actions.");
        foreach (var status in new[] { "pending", "approved", "rejected", "suspended" })
        {
            Check(!AdminUserWriteModel.Actions(Member(status, role: "admin"), "azunel").Any, $"an admin row ({status}) has no actions.");
            Check(!AdminUserWriteModel.Actions(Member(status, role: "owner"), "azunel").Any, $"a non-member role ({status}) has no actions.");
            Check(!AdminUserWriteModel.Actions(Member(status, name: "Azunel"), "azunel").Any, $"the signed-in account ({status}) has no actions.");
            Check(!AdminUserWriteModel.Actions(Member(status, id: "bad id/"), "azunel").Any, $"an invalid id ({status}) has no actions.");
        }
        // Reject only for pending; reset code only for pending/approved (the server refuses the rest).
        foreach (var status in new[] { "approved", "rejected", "suspended" })
        {
            var actions = AdminUserWriteModel.Actions(Member(status), "azunel");
            Check(actions.Primary != MemberAction.Reject && !actions.Overflow.Contains(MemberAction.Reject), $"Reject must not be offered for {status}.");
        }
        foreach (var status in new[] { "rejected", "suspended" })
        {
            var actions = AdminUserWriteModel.Actions(Member(status), "azunel");
            Check(actions.Primary != MemberAction.IssueResetCode && !actions.Overflow.Contains(MemberAction.IssueResetCode), $"Issue reset code must not be offered for {status}.");
        }
        Check(AdminUserWriteModel.IsDanger(MemberAction.Reject) && AdminUserWriteModel.IsDanger(MemberAction.Suspend)
            && !AdminUserWriteModel.IsDanger(MemberAction.Approve) && !AdminUserWriteModel.IsDanger(MemberAction.IssueResetCode)
            && AdminUserWriteModel.NeedsWarning(MemberAction.Reject) && AdminUserWriteModel.NeedsWarning(MemberAction.Suspend)
            && AdminUserWriteModel.NeedsWarning(MemberAction.IssueResetCode) && !AdminUserWriteModel.NeedsWarning(MemberAction.Approve),
            "Reject/Suspend are danger items; Reject, Suspend and Issue reset code get warning wording.");

        // ----- Commands and payloads -----
        Check(AdminUserWriteModel.Command(MemberAction.Approve) == "setUserStatus" && AdminUserWriteModel.Command(MemberAction.Reject) == "setUserStatus"
            && AdminUserWriteModel.Command(MemberAction.Suspend) == "setUserStatus" && AdminUserWriteModel.Command(MemberAction.IssueResetCode) == "issueResetCode",
            "status actions use setUserStatus; the reset code uses issueResetCode.");
        foreach (var (action, status) in new[] { (MemberAction.Approve, "approved"), (MemberAction.Reject, "rejected"), (MemberAction.Suspend, "suspended") })
        {
            var payload = AdminUserWriteModel.Payload(action, "u2");
            Check(payload is not null && payload.Count == 2 && payload["userId"]!.GetValue<string>() == "u2" && payload["status"]!.GetValue<string>() == status
                && BridgePolicy.BuildRequest("setUserStatus", "r1", payload) is not null, $"{action} payload must be exactly {{ userId, status: {status} }}.");
        }
        var resetPayload = AdminUserWriteModel.Payload(MemberAction.IssueResetCode, "u2");
        Check(resetPayload is not null && resetPayload.Count == 1 && resetPayload["userId"]!.GetValue<string>() == "u2"
            && BridgePolicy.BuildRequest("issueResetCode", "r1", resetPayload) is not null, "reset payload must be exactly { userId }.");
        Check(AdminUserWriteModel.Payload(MemberAction.Approve, null) is null && AdminUserWriteModel.Payload(MemberAction.Suspend, "") is null
            && AdminUserWriteModel.Payload(MemberAction.IssueResetCode, "../u2") is null, "invalid user ids must not produce a payload.");

        // ----- Reset-code response parsing (bounded, all-or-nothing) -----
        var hex = new string('a', 32) + new string('0', 32);
        var parsed = AdminUserWriteModel.ParseResetCode(Json($$"""{"code":"{{hex}}","expiresAt":"2026-09-28T10:30:00.000Z"}"""));
        Check(parsed is not null && parsed.Code == hex && parsed.ExpiresAt == new DateTimeOffset(2026, 9, 28, 10, 30, 0, TimeSpan.Zero)
            && !parsed.ToString().Contains(hex, StringComparison.Ordinal), "a valid { code, expiresAt } parses, and ToString never shows the code.");
        foreach (var invalid in new[]
        {
            "null", "[]", "\"code\"", "{}",
            """{"expiresAt":"2026-09-28T10:30:00Z"}""",
            $$"""{"code":"{{hex}}"}""",
            $$"""{"code":"{{hex}}","expiresAt":"not a date"}""",
            $$"""{"code":"{{hex}}","expiresAt":null}""",
            """{"code":"","expiresAt":"2026-09-28T10:30:00Z"}""",
            """{"code":"short","expiresAt":"2026-09-28T10:30:00Z"}""",
            """{"code":"has space inside","expiresAt":"2026-09-28T10:30:00Z"}""",
            """{"code":"<b>markup</b>","expiresAt":"2026-09-28T10:30:00Z"}""",
            $$"""{"code":" {{hex}}","expiresAt":"2026-09-28T10:30:00Z"}""",
            $$"""{"code":"{{new string('a', 129)}}","expiresAt":"2026-09-28T10:30:00Z"}""",
            """{"code":12345678,"expiresAt":"2026-09-28T10:30:00Z"}"""
        })
            Check(AdminUserWriteModel.ParseResetCode(Json(invalid)) is null, $"invalid reset response must not parse: {invalid[..Math.Min(invalid.Length, 40)]}");
        Check(AdminUserWriteModel.ParseResetCode(null) is null
            && AdminUserWriteModel.ParseResetCode(Json($$"""{"code":"{{new string('a', 128)}}","expiresAt":"2026-09-28T10:30:00Z"}""")) is not null,
            "a missing body is invalid; a 128-character code is the upper bound.");

        // ----- Result mapping -----
        foreach (var action in Enum.GetValues<MemberAction>())
        {
            var unknownKey = action == MemberAction.IssueResetCode ? "Admin_Write_UnknownReset" : "Admin_Write_UnknownStatus";
            Check(AdminUserWriteModel.Explain(Write(200, true, null, BridgeOutcome.Applied, """{"ok":true}"""), action).Kind == WriteOutcomeKind.Applied,
                $"{action}: applied is success.");
            Check(AdminUserWriteModel.Explain(Write(400, false, "INVALID_INPUT", BridgeOutcome.Rejected), action) is { Kind: WriteOutcomeKind.Rejected, MessageKey: "Admin_Write_ErrorInvalid" }
                && AdminUserWriteModel.Explain(Write(403, false, "FORBIDDEN", BridgeOutcome.Rejected), action) is { Kind: WriteOutcomeKind.Rejected, MessageKey: "Admin_Write_ErrorForbidden" }
                && AdminUserWriteModel.Explain(Write(404, false, "NOT_FOUND", BridgeOutcome.Rejected), action).MessageKey == "Admin_Write_ErrorForbidden",
                $"{action}: server rejections are named.");
            Check(AdminUserWriteModel.Explain(Write(401, false, "UNAUTHORIZED", BridgeOutcome.Rejected), action) is { Kind: WriteOutcomeKind.Rejected, SessionEnded: true, MessageKey: "Work_ErrorSession" },
                $"{action}: an ended session offers Sign in again.");
            Check(AdminUserWriteModel.Explain(Write(503, false, "MAINTENANCE", BridgeOutcome.Rejected), action).MessageKey == "Dashboard_ErrorMaintenance"
                && AdminUserWriteModel.Explain(Write(429, false, "RATE_LIMIT", BridgeOutcome.Rejected), action).MessageKey == "Dashboard_ErrorRateLimit"
                && AdminUserWriteModel.Explain(Write(403, false, "NOT_APPROVED", BridgeOutcome.Rejected), action).MessageKey == "Announcements_ErrorNotApproved",
                $"{action}: maintenance, rate-limit and approval rejections keep the shared wording.");
            Check(AdminUserWriteModel.Explain(BridgeResult.HostFailure("r1", "TIMEOUT", false, BridgeOutcome.Unknown), action) is { Kind: WriteOutcomeKind.Unknown, TitleKey: "Work_UnknownTitle" } unknown
                && unknown.MessageKey == unknownKey
                && AdminUserWriteModel.Explain(Write(500, false, "SERVER_ERROR", BridgeOutcome.Unknown), action).MessageKey == unknownKey,
                $"{action}: timeout and 5xx are unknown with the action's wording.");
            Check(AdminUserWriteModel.Explain(BridgeResult.HostFailure("r1", "NOT_CONNECTED", true, BridgeOutcome.Rejected), action).Kind == WriteOutcomeKind.NotSent,
                $"{action}: a request that never left the app is not sent.");
        }
        var (resetOk, resetCode) = AdminUserWriteModel.ExplainReset(Write(200, true, null, BridgeOutcome.Applied, $$"""{"code":"{{hex}}","expiresAt":"2026-09-28T10:30:00Z"}"""));
        Check(resetOk.Kind == WriteOutcomeKind.Applied && resetCode?.Code == hex, "an applied reset returns the parsed code.");
        var (resetBad, badCode) = AdminUserWriteModel.ExplainReset(Write(200, true, null, BridgeOutcome.Applied, """{"code":"","expiresAt":"2026-09-28T10:30:00Z"}"""));
        Check(resetBad is { Kind: WriteOutcomeKind.Unknown, MessageKey: "Admin_Write_UnknownReset" } && badCode is null,
            "an applied reset without a valid code is unknown and shows nothing.");
        var (resetRejected, rejectedCode) = AdminUserWriteModel.ExplainReset(Write(400, false, "INVALID_INPUT", BridgeOutcome.Rejected, $$"""{"code":"{{hex}}","expiresAt":"2026-09-28T10:30:00Z"}"""));
        var (resetUnknown, unknownCode) = AdminUserWriteModel.ExplainReset(Write(500, false, "SERVER_ERROR", BridgeOutcome.Unknown, $$"""{"code":"{{hex}}","expiresAt":"2026-09-28T10:30:00Z"}"""));
        Check(resetRejected.Kind == WriteOutcomeKind.Rejected && rejectedCode is null && resetUnknown.Kind == WriteOutcomeKind.Unknown && unknownCode is null,
            "a rejected or unknown reset never yields a code to show.");

        // ----- Source policy -----
        var view = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationView.MemberActions.cs"));
        var members = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationView.Members.cs"));
        var main = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationView.cs"));
        var run = Between(view, "private async Task RunMemberActionAsync", "private MemberNotice Notice(");
        Check(run.Length > 0 && !administrationCode.Contains("new WriteGate", StringComparison.Ordinal)
            && Regex.Matches(administrationCode, @"_gate\.TryEnter\(\)").Count == 1 && Regex.Matches(administrationCode, @"_gate\.Exit\(\);").Count == 1
            && run.IndexOf("_gate.Exit();", StringComparison.Ordinal) > run.IndexOf("finally", StringComparison.Ordinal),
            "Members writes use the app-wide gate once per action and always release it.");
        Check(Regex.IsMatch(shell, @"new AdministrationView\([^;]*_getUser,\s*_writeGate,\s*OnWorkChanged\);")
            && main.Contains("Func<AuthenticatedUser> currentUser, WriteGate gate, Action workChanged", StringComparison.Ordinal),
            "ShellPageHost passes the signed-in user, the shared WriteGate and OnWorkChanged.");
        var confirm = run.IndexOf("await ConfirmMemberActionAsync(member, action)", StringComparison.Ordinal);
        var enter = run.IndexOf("_gate.TryEnter()", StringComparison.Ordinal);
        var send = run.IndexOf("await _request(AdminUserWriteModel.Command(action), payload)", StringComparison.Ordinal);
        var workChanged = run.IndexOf("_workChanged();", StringComparison.Ordinal);
        var reload = run.IndexOf("await LoadSectionAsync(\"Members\", false)", StringComparison.Ordinal);
        var notice = run.IndexOf("ShowMemberNotice(Notice(member, action, outcome, refreshed))", StringComparison.Ordinal);
        Check(confirm > 0 && enter > confirm && send > enter && workChanged > send && reload > workChanged && notice > reload
            && Regex.Matches(administrationCode, @"_request\(AdminUserWriteModel").Count == 1
            && !Regex.IsMatch(run, @"\b(while|for|foreach|goto)\b") && !run.Contains("Retry", StringComparison.Ordinal),
            "each action is confirmed, then sent exactly once (no loop or retry), then Members reloads from offset zero and the result is explained.");
        var sendBlock = Between(run, "BridgeResult result;", "WriteOutcome outcome;");
        Check(sendBlock.Contains("catch", StringComparison.Ordinal) && sendBlock.Contains("BridgeOutcome.Unknown", StringComparison.Ordinal),
            "a request that throws is treated as unknown, never resent.");
        var confirmDialog = Between(view, "private async Task<bool> ConfirmMemberActionAsync", "private async Task RunMemberActionAsync");
        Check(confirmDialog.Contains("DefaultButton = ContentDialogButton.Close", StringComparison.Ordinal)
            && confirmDialog.Contains("CloseButtonText = L(\"Work_CancelForm\")", StringComparison.Ordinal)
            && confirmDialog.Contains("Style = PageParts.Res(\"DefaultContentDialogStyle\")", StringComparison.Ordinal)
            && confirmDialog.Contains("member.Username", StringComparison.Ordinal) && confirmDialog.Contains("AdminUserWriteModel.NeedsWarning(action)", StringComparison.Ordinal),
            "confirmation names the account, warns where required and keeps Cancel as the default.");
        var busy = Between(view, "private void SetMemberWriteBusy", "private void ShowMemberNotice");
        Check(new[] { "_content.IsEnabled = !busy;", "_sections.IsEnabled = !busy;", "UpdateControls();", "_writeProgress.Visibility" }
                .All(part => busy.Contains(part, StringComparison.Ordinal))
            && main.Contains("_refresh.IsEnabled = !_memberWriteBusy && !set.IsLoading;", StringComparison.Ordinal)
            && main.Contains("_more.IsEnabled = !_memberWriteBusy && set.CanLoadMore;", StringComparison.Ordinal)
            && main.Contains("root.Children.Add(_writeProgress)", StringComparison.Ordinal),
            "a running write disables Members actions, Refresh, Load more and section switching and shows progress while keeping rows.");
        Check(main.Contains("if (shown && !_memberNoticeShown) _status.IsOpen = false;", StringComparison.Ordinal)
            && main.Contains("Loaded += (_, _) => ShowPendingMemberNotice();", StringComparison.Ordinal)
            && view.Contains("if (_pendingMemberNotice is not { } notice || _selected != \"Members\" || !IsLoaded) return;", StringComparison.Ordinal)
            && view.Contains("action.Click += (_, _) => _signInAgain();", StringComparison.Ordinal),
            "a write notice waits until Members is visible, survives the reload and offers Sign in again for an ended session.");
        Check(members.Contains("AdminUserWriteModel.Actions(member, signedIn)", StringComparison.Ordinal)
            && view.Contains("new SymbolIcon(Symbol.More)", StringComparison.Ordinal) && view.Contains("Flyout = menu", StringComparison.Ordinal)
            && view.Contains("\"Admin_MoreActionsForFormat\"", StringComparison.Ordinal) && view.Contains("ActionName(", StringComparison.Ordinal)
            && view.Contains("PageParts.Res(\"AdminDangerMenuFlyoutItemStyle\")", StringComparison.Ordinal),
            "rows show the primary action and an accessible More actions menu with danger styling.");

        // The reset code: shown once in its own dialog, copied only on request, cleared on close, never logged or stored.
        var dialog = Between(view, "private async Task<bool> ShowResetCodeAsync", "private static bool CopyCode");
        Check(dialog.Contains("DefaultButton = ContentDialogButton.None", StringComparison.Ordinal)
            && dialog.Contains("IsReadOnly = true", StringComparison.Ordinal)
            && dialog.Contains("copy.Click +=", StringComparison.Ordinal)
            && dialog.Contains("codeBox.Text = string.Empty;", StringComparison.Ordinal)
            && dialog.IndexOf("codeBox.Text = string.Empty;", StringComparison.Ordinal) > dialog.IndexOf("finally", StringComparison.Ordinal)
            && run.Contains("issued = null;", StringComparison.Ordinal),
            "the code dialog is not closed by Enter, copies only on the button and clears the code when it closes.");
        Check(Regex.Matches(administrationCode, @"\.Code\b").Count == 1 && dialog.Contains("Text = issued.Code", StringComparison.Ordinal)
            && Regex.Matches(administrationCode, @"CopyCode\(").Count == 2
            && view.Contains("IsAllowedInHistory = false", StringComparison.Ordinal) && view.Contains("IsRoamable = false", StringComparison.Ordinal),
            "the code is read only into the dialog's text box and copied only by the explicit button, outside clipboard history.");
        foreach (var forbidden in new[] { "Debug.", "Trace.", "Console.", "LogFailure", "LocalSettings", "ApplicationData", "File.", "Exception ex" })
            Check(!view.Contains(forbidden, StringComparison.Ordinal), $"the Members write code must not log or persist anything ({forbidden}).");
        Check(!Regex.IsMatch(Between(view, "private MemberNotice Notice(", "private static string Named("), @"issued|\.Code|result\.Data"),
            "notices never include the reset code or response data.");
        Check(!Regex.IsMatch(view, "[\uE000-\uF8FF]"), "no raw private-use glyphs in the Members write code.");

        // ----- Strings -----
        var keys = Regex.Matches(view + members, "\"(Admin_[A-Za-z_]+)\"").Select(m => m.Groups[1].Value)
            .Concat(Enum.GetValues<MemberAction>().SelectMany(a => new[] { AdminUserWriteModel.ActionKey(a), "Admin_Confirm_" + a }))
            .Concat(new[] { "Admin_Write_ErrorInvalid", "Admin_Write_ErrorForbidden", "Admin_Write_UnknownStatus", "Admin_Write_UnknownReset" })
            .Where(key => !key.EndsWith('_')).Distinct().ToList();
        Check(keys.Count >= 30, "the Members write strings must be found in the source.");
        foreach (var locale in new[] { "en-US", "ja-JP" })
        {
            var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
            foreach (var key in keys) Check(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"missing {key} in {locale}.");
        }
    }
}
