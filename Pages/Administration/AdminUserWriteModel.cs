using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MolHub.Windows;

/// <summary>The member writes the web admin panel offers (`setUserStatus` and `issueResetCode`).</summary>
public enum MemberAction
{
    Approve,
    Reject,
    Suspend,
    IssueResetCode
}

/// <summary>The row's visible primary action and the remaining actions for its overflow menu.</summary>
public sealed record MemberActions(MemberAction? Primary, IReadOnlyList<MemberAction> Overflow)
{
    public static readonly MemberActions None = new(null, []);
    public bool Any => Primary is not null || Overflow.Count > 0;
}

/// <summary>
/// A reset code issued once. It lives only in the open dialog; the text is redacted from <see cref="ToString"/> so it
/// cannot reach diagnostic output by accident.
/// </summary>
public sealed class IssuedResetCode(string code, DateTimeOffset expiresAt)
{
    public string Code { get; } = code;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => nameof(IssuedResetCode);
}

/// <summary>
/// Pure rules for the Administration Members writes, mirroring the web admin panel and the server's allowed transitions.
/// The server is the authority; these rules only decide which controls are shown, shape the requests and explain the
/// answers. A write is sent once and never resent automatically; issuing another reset code replaces the earlier one.
/// </summary>
public static partial class AdminUserWriteModel
{
    public const string StatusCommand = "setUserStatus";
    public const string ResetCommand = "issueResetCode";
    public const int MaxResetCodeLength = 128;

    // The server issues 64 hex characters; accept a bounded token without whitespace or markup in case the format grows.
    [GeneratedRegex("^[A-Za-z0-9._~-]{8,128}$")]
    private static partial Regex ResetCodePattern();

    /// <summary>
    /// Admin rows (including the signed-in administrator), non-member roles and unknown statuses get no actions. The
    /// web table: pending → Approve | Reject, Suspend, Issue reset code; approved → Issue reset code | Suspend;
    /// rejected → Approve | Suspend; suspended → Approve.
    /// </summary>
    public static MemberActions Actions(AdminMember member, string signedInUsername)
    {
        if (member.Role != "member" || !ProjectsModel.IsValidId(member.Id)
            || string.Equals(member.Username, signedInUsername, StringComparison.OrdinalIgnoreCase)) return MemberActions.None;
        return member.Status switch
        {
            "pending" => new(MemberAction.Approve, [MemberAction.Reject, MemberAction.Suspend, MemberAction.IssueResetCode]),
            "approved" => new(MemberAction.IssueResetCode, [MemberAction.Suspend]),
            "rejected" => new(MemberAction.Approve, [MemberAction.Suspend]),
            "suspended" => new(MemberAction.Approve, []),
            _ => MemberActions.None
        };
    }

    public static string Command(MemberAction action) => action == MemberAction.IssueResetCode ? ResetCommand : StatusCommand;

    /// <summary>The status a status write sets, or null for the reset code.</summary>
    public static string? TargetStatus(MemberAction action) => action switch
    {
        MemberAction.Approve => "approved",
        MemberAction.Reject => "rejected",
        MemberAction.Suspend => "suspended",
        _ => null
    };

    /// <summary>Reject and Suspend remove normal access; a new reset code replaces the earlier one. These get warning wording.</summary>
    public static bool NeedsWarning(MemberAction action) => action != MemberAction.Approve;

    /// <summary>The web styles Suspend (and confirms Reject and Suspend) as dangerous: both remove normal access.</summary>
    public static bool IsDanger(MemberAction action) => action is MemberAction.Reject or MemberAction.Suspend;

    /// <summary>Only the keys the bridge accepts: `{ userId, status }` or `{ userId }`.</summary>
    public static JsonObject? Payload(MemberAction action, string? userId)
    {
        if (!ProjectsModel.IsValidId(userId)) return null;
        return TargetStatus(action) is { } status
            ? new JsonObject { ["userId"] = userId, ["status"] = status }
            : new JsonObject { ["userId"] = userId };
    }

    public static string ActionKey(MemberAction action) => "Admin_Action_" + action;

    /// <summary>
    /// Parses only `{ code, expiresAt }` from a successful response: a bounded code without whitespace and a valid expiry.
    /// Anything else is invalid, and nothing of it is shown.
    /// </summary>
    public static IssuedResetCode? ParseResetCode(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } value) return null;
        if (!value.TryGetProperty("code", out var codeElement) || codeElement.ValueKind != JsonValueKind.String) return null;
        var code = codeElement.GetString();
        if (code is null || code.Length > MaxResetCodeLength || !ResetCodePattern().IsMatch(code)) return null;
        return DashboardModel.Date(value, "expiresAt") is { } expires ? new IssuedResetCode(code, expires) : null;
    }

    /// <summary>
    /// Explains a member write result: the shared write rules, with the Members-specific rejections named and the unknown
    /// outcome worded per action (a reset code that may have been issued cannot be recovered).
    /// </summary>
    public static WriteOutcome Explain(BridgeResult result, MemberAction action)
    {
        var outcome = WorkModel.Explain(result);
        if (outcome.Kind == WriteOutcomeKind.Unknown) return outcome with { MessageKey = UnknownKey(action) };
        if (outcome.Kind != WriteOutcomeKind.Rejected || outcome.SessionEnded) return outcome;
        var key = result switch
        {
            { ErrorCode: "INVALID_INPUT" } => "Admin_Write_ErrorInvalid",
            { ErrorCode: "FORBIDDEN" or "ADMIN_REQUIRED" or "NOT_FOUND" } or { Status: 404 } => "Admin_Write_ErrorForbidden",
            _ => null
        };
        return key is null ? outcome : outcome with { MessageKey = key };
    }

    /// <summary>
    /// A reset-code result with the code itself. An applied answer without a valid `{ code, expiresAt }` is treated as
    /// unknown: a code may have replaced the earlier one, but nothing is shown and nothing is resent.
    /// </summary>
    public static (WriteOutcome Outcome, IssuedResetCode? Code) ExplainReset(BridgeResult result)
    {
        var outcome = Explain(result, MemberAction.IssueResetCode);
        if (outcome.Kind != WriteOutcomeKind.Applied) return (outcome, null);
        return ParseResetCode(result.Data) is { } code
            ? (outcome, code)
            : (new WriteOutcome(WriteOutcomeKind.Unknown, "Work_UnknownTitle", UnknownKey(MemberAction.IssueResetCode)), null);
    }

    private static string UnknownKey(MemberAction action) =>
        action == MemberAction.IssueResetCode ? "Admin_Write_UnknownReset" : "Admin_Write_UnknownStatus";
}
