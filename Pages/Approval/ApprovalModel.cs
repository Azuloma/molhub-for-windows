using System.Text.Json;

namespace FusionLedger.Windows;

public enum ApprovalCheck
{
    Approved,
    StillWaiting,
    SessionEnded,
    Connection,
    RateLimited,
    Unexpected
}

/// <summary>
/// Pure rules for the approval-waiting screen. "Refresh" asks the bridge `session` read (the one account read the
/// server answers for every status) and switches to the workspace only when the same account is now approved.
/// </summary>
public static class ApprovalModel
{
    private const int MaxUsernameLength = 128;

    /// <summary>
    /// Reads `/api/v1/session` (`user.status`, `user.role`, camelCase `user.projectManager`). A missing user or a
    /// different username means the sign-in no longer belongs to this window, so it is treated as an ended session.
    /// </summary>
    public static (ApprovalCheck Check, AuthenticatedUser? User) Check(BridgeResult result, AuthenticatedUser current)
    {
        if (!result.Ok)
        {
            return (result switch
            {
                { Status: 401 } or { ErrorCode: "UNAUTHORIZED" } => ApprovalCheck.SessionEnded,
                { Status: 429 } or { ErrorCode: "RATE_LIMIT" } => ApprovalCheck.RateLimited,
                { Status: 0 } => ApprovalCheck.Connection,
                _ => ApprovalCheck.Unexpected
            }, null);
        }

        if (result.Data is not { ValueKind: JsonValueKind.Object } data || !data.TryGetProperty("user", out var user))
            return (ApprovalCheck.Unexpected, null);
        if (user.ValueKind == JsonValueKind.Null) return (ApprovalCheck.SessionEnded, null);
        if (user.ValueKind != JsonValueKind.Object) return (ApprovalCheck.Unexpected, null);

        var username = DashboardModel.Text(user, "username", MaxUsernameLength);
        var status = DashboardModel.Text(user, "status", ProfilePayloadParser.MaxStatusLength)?.ToLowerInvariant();
        var role = DashboardModel.Text(user, "role", ProfilePayloadParser.MaxRoleLength)?.ToLowerInvariant();
        if (username is null || status is null || role is null
            || !ProfilePayloadParser.IsKnownStatus(status) || !ProfilePayloadParser.IsKnownRole(role))
        {
            return (ApprovalCheck.Unexpected, null);
        }
        if (!string.Equals(username, current.Username, StringComparison.OrdinalIgnoreCase)) return (ApprovalCheck.SessionEnded, null);

        var projectManager = user.TryGetProperty("projectManager", out var pm) && pm.ValueKind == JsonValueKind.True;
        var updated = current with { Status = status, Role = role, ProjectManager = projectManager };
        return (NativePageCatalog.IsApproved(updated) ? ApprovalCheck.Approved : ApprovalCheck.StillWaiting, updated);
    }
}
