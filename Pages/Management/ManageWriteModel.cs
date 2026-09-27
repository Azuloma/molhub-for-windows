using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FusionLedger.Windows;

/// <summary>The Discord destination form as typed (server and channel ids, notification language, reason, consent).</summary>
public sealed record DiscordDraft(string Guild, string Channel, string Locale, string Reason, bool Consent);

[Flags]
public enum DiscordFieldError
{
    None = 0,
    GuildInvalid = 1,
    ChannelInvalid = 2,
    ReasonMissing = 4,
    ReasonTooLong = 8,
    ConsentMissing = 16
}

/// <summary>
/// Pure rules for the Project management writes, mirroring the web manage panel: add/remove members, force-release
/// the reservation, deletion/restoration requests (owner only), Discord destinations and, for site admins, the
/// project administrator. The server and database guards decide; these rules only decide which controls the web
/// shows, shape the requests with the server's bounds and explain the answers. A write is never resent automatically.
/// </summary>
public static class ManageWriteModel
{
    public const int MaxReasonLength = 1000;

    private static readonly Regex Snowflake = new("^[0-9]{17,20}$", RegexOptions.CultureInvariant);

    // ----- Which controls the web shows -----

    /// <summary>Approved accounts that are not members yet (the web's "Approved account" list), in server order.</summary>
    public static IReadOnlyList<ManagedAccount> AddCandidates(ManagedProjectDetail detail) =>
        detail.Project.Deleted
            ? []
            : (detail.Eligible ?? []).Where(account => detail.Members.All(member => member.Id != account.Id)).ToList();

    /// <summary>The project administrator and site admins cannot be removed (OWNER_PROTECTED); nothing changes on a deleted project.</summary>
    public static bool CanRemove(ManagedMember member, ManagedProjectDetail detail) =>
        !detail.Project.Deleted && member.Id != detail.Project.OwnerId && member.Role != "admin" && ProjectsModel.IsValidId(member.Id);

    /// <summary>A deleted project refuses the release (it is not a managed, active project), so the button is not shown.</summary>
    public static bool CanRelease(ManagedProjectDetail detail) => detail.Reservation is not null && !detail.Project.Deleted;

    /// <summary>Only the project's own administrator can ask for deletion or restoration (OWNER_ONLY, also for site admins).</summary>
    public static bool CanRequest(ManagedProjectDetail detail, string username) =>
        ManagementModel.Owner(detail) is (ManagedOwnerKind.Account, var owner)
        && string.Equals(owner, username, StringComparison.OrdinalIgnoreCase);

    /// <summary>"restore" for a deleted project, else "delete" (the server rejects the other kind as REQUEST_CHANGED).</summary>
    public static string RequestKind(ManagedProjectDetail detail) => detail.Project.Deleted ? "restore" : "delete";

    public static bool CanLinkDiscord(ManagedProjectDetail detail) => !detail.Project.Deleted;

    public static bool CanStopDiscord(ManagedLink link) => link.Enabled && ProjectsModel.IsValidId(link.Id);

    /// <summary>Accounts a site admin can appoint: approved accounts, plus the current owner so the choice shows it.</summary>
    public static IReadOnlyList<ManagedAccount> OwnerCandidates(ManagedProjectDetail detail) => detail.Eligible ?? [];

    // ----- Requests -----

    public static JsonObject? AddMemberPayload(string projectId, string? userId) =>
        ProjectsModel.IsValidId(projectId) && ProjectsModel.IsValidId(userId)
            ? new JsonObject { ["projectId"] = projectId, ["user"] = userId }
            : null;

    public static JsonObject? RemoveMemberPayload(string projectId, string? userId) =>
        ProjectsModel.IsValidId(projectId) && ProjectsModel.IsValidId(userId)
            ? new JsonObject { ["projectId"] = projectId, ["userId"] = userId }
            : null;

    public static JsonObject? ReleasePayload(string projectId) =>
        ProjectsModel.IsValidId(projectId) ? new JsonObject { ["projectId"] = projectId } : null;

    /// <summary>A null owner makes the project "Managed by site administrators".</summary>
    public static JsonObject? AssignOwnerPayload(string projectId, string? userId) =>
        ProjectsModel.IsValidId(projectId) && (userId is null || ProjectsModel.IsValidId(userId))
            ? new JsonObject { ["projectId"] = projectId, ["user"] = userId }
            : null;

    public static bool IsValidReason(string reason) => reason.Trim().Length is > 0 and <= MaxReasonLength;

    public static JsonObject? RequestPayload(string projectId, string kind, string reason) =>
        ProjectsModel.IsValidId(projectId) && kind is "delete" or "restore" && IsValidReason(reason)
            ? new JsonObject { ["projectId"] = projectId, ["kind"] = kind, ["reason"] = reason.Trim() }
            : null;

    public static DiscordFieldError Validate(DiscordDraft draft)
    {
        var errors = DiscordFieldError.None;
        if (!Snowflake.IsMatch(draft.Guild.Trim())) errors |= DiscordFieldError.GuildInvalid;
        if (!Snowflake.IsMatch(draft.Channel.Trim())) errors |= DiscordFieldError.ChannelInvalid;
        var reason = draft.Reason.Trim();
        if (reason.Length == 0) errors |= DiscordFieldError.ReasonMissing;
        else if (reason.Length > MaxReasonLength) errors |= DiscordFieldError.ReasonTooLong;
        if (!draft.Consent) errors |= DiscordFieldError.ConsentMissing;
        return errors;
    }

    /// <summary>Connects an approved destination at once, or files an approval request (the server decides; `pending` tells which).</summary>
    public static JsonObject? DiscordLinkPayload(string projectId, DiscordDraft draft) =>
        ProjectsModel.IsValidId(projectId) && Validate(draft) == DiscordFieldError.None && draft.Locale is "en" or "ja"
            ? new JsonObject
            {
                ["projectId"] = projectId,
                ["guild"] = draft.Guild.Trim(),
                ["channel"] = draft.Channel.Trim(),
                ["locale"] = draft.Locale,
                ["reason"] = draft.Reason.Trim(),
                ["consent"] = true
            }
            : null;

    public static JsonObject? DisableDiscordPayload(string projectId, string? linkId) =>
        ProjectsModel.IsValidId(projectId) && ProjectsModel.IsValidId(linkId)
            ? new JsonObject { ["projectId"] = projectId, ["linkId"] = linkId }
            : null;

    /// <summary>The web's notification language default: Japanese for the Japanese UI, else English.</summary>
    public static string DefaultLocale(string language) => language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";

    /// <summary>`data.pending` from a request or Discord write: true when it waits for a site administrator.</summary>
    public static bool IsPending(BridgeResult result) =>
        result.Data is { ValueKind: System.Text.Json.JsonValueKind.Object } data
        && data.TryGetProperty("pending", out var pending) && pending.ValueKind == System.Text.Json.JsonValueKind.True;

    // ----- Answers -----

    /// <summary>
    /// Explains a management write result: the shared write rules, with the management codes named specifically.
    /// Requests and Discord approvals create a new record on every call, so their unknown outcome says to check first.
    /// </summary>
    public static WriteOutcome Explain(BridgeResult result, bool createsRecord = false)
    {
        var outcome = WorkModel.Explain(result);
        if (outcome.Kind == WriteOutcomeKind.Unknown)
        {
            return outcome with { MessageKey = createsRecord ? "Manage_UnknownRequest" : "Manage_Unknown" };
        }
        if (outcome.Kind != WriteOutcomeKind.Rejected || outcome.SessionEnded) return outcome;
        var key = result.ErrorCode switch
        {
            "MEMBER_NOT_APPROVED" => "Manage_ErrorMemberNotApproved",
            "OWNER_PROTECTED" => "Manage_ErrorOwnerProtected",
            "OWNER_ONLY" => "Manage_ErrorOwnerOnly",
            "REQUEST_CHANGED" => "Manage_ErrorRequestChanged",
            "DISCORD_NOT_CONFIGURED" => "Manage_ErrorDiscordNotConfigured",
            "DISCORD_CHANNEL_ACCESS" => "Manage_ErrorDiscordChannelAccess",
            "DISCORD_LINK_CONFLICT" => "Manage_ErrorDiscordLinkConflict",
            "DISCORD_RATE_LIMIT" => "Manage_ErrorDiscordRateLimit",
            "DISCORD_TOKEN_INVALID" => "Manage_ErrorDiscordTokenInvalid",
            "DISCORD_APPROVAL_REQUIRED" => "Manage_ErrorDiscordApprovalRequired",
            "PROJECT_FORBIDDEN" or "FORBIDDEN" or "NOT_FOUND" => "Manage_ErrorNoAccess",
            _ => null
        };
        return key is null ? outcome : outcome with { MessageKey = key };
    }
}
