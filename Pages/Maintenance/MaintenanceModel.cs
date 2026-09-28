using System.Text.Json;
using System.Text.Json.Nodes;

namespace MolHub.Windows;

public sealed record Announcement(
    string Id,
    string Title,
    string Version,
    string Details,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? CreatedAt)
{
    /// <summary>The web dates an announcement by the end of the maintenance it reports; the creation time is the fallback.</summary>
    public DateTimeOffset? DisplayDate => EndedAt ?? CreatedAt;
}

public sealed record AnnouncementPage(IReadOnlyList<Announcement> Items, int Total, int? NextOffset);

/// <summary>`/api/v1/maintenance`: whether maintenance is active, since when, and whether the server has it set up.</summary>
public sealed record MaintenanceState(bool Active, DateTimeOffset? StartedAt, bool Available);

public enum MaintenanceError
{
    Connection,
    SessionEnded,
    NotApproved,
    RateLimited,
    Unexpected
}

/// <summary>
/// Pure rules for the read-only Server maintenance page: the `maintenance` state read and the `announcements`
/// read (the announcements published when a maintenance ends). The server answers both for approved and pending
/// accounts, also during maintenance; announcements accept only `limit`/`offset`.
/// </summary>
public static class MaintenanceModel
{
    public const int PageSize = 30;
    public const int ListPreviewLength = 420;

    private const int MaxItems = 100;
    private const int MaxOffset = 5000;
    private const int MaxTitleLength = 200;
    private const int MaxVersionLength = 100;
    private const int MaxDetailsLength = 5000;

    public static JsonObject ListPayload(int offset) =>
        new() { ["limit"] = PageSize, ["offset"] = Math.Clamp(offset, 0, MaxOffset) };

    /// <summary>Parses the `maintenance` state; null unless `active` and `available` are booleans.</summary>
    public static MaintenanceState? ParseState(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !data.TryGetProperty("available", out var available) || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }
        return new MaintenanceState(active.GetBoolean(), DashboardModel.Date(data, "startedAt"), available.GetBoolean());
    }

    /// <summary>Parses an `announcements` page (`data` array, `meta.total`/`meta.nextOffset`); null when the shape is wrong.</summary>
    public static AnnouncementPage? ParseList(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var items = data.EnumerateArray().Take(MaxItems).Select(Parse).OfType<Announcement>().ToList();
        var (total, next) = ProjectsModel.Paging(meta, items.Count);
        return new AnnouncementPage(items, total, next);
    }

    public static Announcement? Parse(JsonElement item)
    {
        var id = DashboardModel.Text(item, "id", 100);
        var title = DashboardModel.Text(item, "title", MaxTitleLength);
        if (!ProjectsModel.IsValidId(id) || title is null) return null;
        return new Announcement(
            id!,
            title,
            DashboardModel.Text(item, "version", MaxVersionLength) ?? string.Empty,
            DashboardModel.Text(item, "details", MaxDetailsLength, trim: false)?.Trim() ?? string.Empty,
            DashboardModel.Date(item, "startedAt"),
            DashboardModel.Date(item, "endedAt"),
            DashboardModel.Date(item, "createdAt"));
    }

    /// <summary>
    /// List preview: the details with their line breaks (as the web shows them), cut at the limit with "…".
    /// Runs of blank lines are reduced to one.
    /// </summary>
    public static string Preview(string details, int length)
    {
        var text = string.Join('\n', details.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd())).Trim();
        while (text.Contains("\n\n\n", StringComparison.Ordinal)) text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        return text.Length > length ? text[..length].TrimEnd() + "…" : text;
    }

    /// <summary>
    /// The web's member lock note ("…you can still review published announcements here") during maintenance: not
    /// for administrators (not locked out), and only for approved or pending accounts, the ones that can read them.
    /// </summary>
    public static bool ShowsLockNote(AuthenticatedUser user) =>
        !NativePageCatalog.CanAdminister(user)
        && (NativePageCatalog.IsApproved(user) || string.Equals(user.Status, "pending", StringComparison.OrdinalIgnoreCase));

    public static MaintenanceError ErrorFor(BridgeResult result) => result switch
    {
        { Status: 401 } or { ErrorCode: "UNAUTHORIZED" } => MaintenanceError.SessionEnded,
        // Rejected and suspended accounts cannot read announcements.
        { ErrorCode: "NOT_APPROVED" } => MaintenanceError.NotApproved,
        { Status: 429 } or { ErrorCode: "RATE_LIMIT" } => MaintenanceError.RateLimited,
        { Status: 0 } => MaintenanceError.Connection,
        _ => MaintenanceError.Unexpected
    };
}
