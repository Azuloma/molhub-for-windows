using System.Globalization;
using System.Text.Json;

namespace MolHub.Windows;

public sealed record DashboardReservation(string? Username, DateTimeOffset? StartedAt);

public sealed record DashboardProject(string Id, string Name, DashboardReservation? Reservation);

public sealed record DashboardCommit(
    string Id,
    string ProjectName,
    string Title,
    string Changes,
    string Version,
    string AuthorName,
    byte[]? AuthorAvatar,
    DateTimeOffset? CreatedAt);

public sealed record DashboardActivity(string ProjectName, string Actor, DateTimeOffset? CreatedAt);

/// <summary>The `/api/v1/dashboard` DTO reduced to what the native Dashboard shows. Every count comes from server data.</summary>
public sealed record DashboardData(
    IReadOnlyList<DashboardProject> Projects,
    IReadOnlyList<DashboardCommit> Commits,
    IReadOnlyList<DashboardActivity> Activity,
    int PendingAccounts,
    int PendingRequests)
{
    public int ActiveProjects => Projects.Count;

    public int InProgress => Projects.Count(project => project.Reservation is not null);

    public int PendingApprovals => PendingAccounts + PendingRequests;

    public IReadOnlyList<DashboardProject> ReservedBy(string username) =>
        Projects.Where(project => project.Reservation?.Username is { } holder
            && string.Equals(holder, username, StringComparison.OrdinalIgnoreCase)).ToList();
}

public enum DashboardError
{
    Connection,
    SessionEnded,
    PendingApproval,
    Maintenance,
    RateLimited,
    Unexpected
}

/// <summary>Pure rules for the native Dashboard, mirroring the web workspace dashboard.</summary>
public static class DashboardModel
{
    public const int TopProjectLimit = 8;
    public const int ActivityLimit = 4;
    public const int CommitLimit = 6;
    public const int ChangesPreviewLength = 420;
    public const int ShortIdLength = 7;

    private const int MaxItems = 100;
    private const int MaxIdLength = 100;
    private const int MaxNameLength = 200;
    private const int MaxTitleLength = 200;
    private const int MaxChangesLength = 10_000;
    private const int MaxVersionLength = 40;

    /// <summary>Parses the dashboard `data` object; null when the shape is not the documented DTO.</summary>
    public static DashboardData? Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        if (!data.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Array) return null;
        if (!data.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Array) return null;

        var projectList = new List<DashboardProject>();
        foreach (var item in projects.EnumerateArray().Take(MaxItems))
        {
            var id = Text(item, "id", MaxIdLength);
            var name = Text(item, "name", MaxNameLength);
            if (id is null || name is null) continue;
            DashboardReservation? reservation = null;
            if (item.TryGetProperty("reservation", out var r) && r.ValueKind == JsonValueKind.Object)
            {
                reservation = new DashboardReservation(Text(r, "username", MaxNameLength), Date(r, "startedAt"));
            }
            projectList.Add(new DashboardProject(id, name, reservation));
        }

        var commitList = new List<DashboardCommit>();
        foreach (var item in commits.EnumerateArray().Take(MaxItems))
        {
            var id = Text(item, "id", MaxIdLength);
            var title = Text(item, "title", MaxTitleLength);
            if (id is null || title is null) continue;
            string? authorName = null;
            byte[]? avatar = null;
            if (item.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object)
            {
                authorName = Text(author, "username", MaxNameLength);
                avatar = ProfilePayloadParser.ParsePngDataUri(Text(author, "avatar", 64 * 1024, trim: false));
            }
            commitList.Add(new DashboardCommit(
                id,
                Text(item, "projectName", MaxNameLength) ?? string.Empty,
                title,
                Text(item, "changes", MaxChangesLength, trim: false) ?? string.Empty,
                Text(item, "version", MaxVersionLength) ?? string.Empty,
                authorName ?? string.Empty,
                avatar,
                Date(item, "createdAt")));
        }

        var activityList = new List<DashboardActivity>();
        if (data.TryGetProperty("activity", out var activity) && activity.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in activity.EnumerateArray().Take(MaxItems))
            {
                var projectName = Text(item, "projectName", MaxNameLength);
                if (projectName is null) continue;
                activityList.Add(new DashboardActivity(projectName, Text(item, "actor", MaxNameLength) ?? string.Empty, Date(item, "createdAt")));
            }
        }

        var pendingAccounts = 0;
        var pendingRequests = 0;
        if (data.TryGetProperty("pending", out var pending) && pending.ValueKind == JsonValueKind.Object)
        {
            pendingAccounts = Count(pending, "accounts");
            pendingRequests = Count(pending, "requests");
        }

        return new DashboardData(projectList, commitList, activityList, pendingAccounts, pendingRequests);
    }

    /// <summary>Pending approvals are shown to site admins and project owners, as on the web dashboard.</summary>
    public static bool ShowsPendingApprovals(string role, bool projectManager) =>
        string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) || projectManager;

    public static string Preview(string changes)
    {
        var text = changes.Trim();
        return text.Length > ChangesPreviewLength ? text[..ChangesPreviewLength] + "…" : text;
    }

    public static string ShortId(string id) => id.Length <= ShortIdLength ? id : id[..ShortIdLength];

    /// <summary>Local date and time in the app language ("Sep 19, 2026, 11:24 PM" / "2026/09/19 23:24").</summary>
    public static string FormatDate(DateTimeOffset? value, string language, TimeZoneInfo? zone = null)
    {
        if (value is not { } instant) return string.Empty;
        var local = TimeZoneInfo.ConvertTime(instant, zone ?? TimeZoneInfo.Local);
        return language == "ja-JP"
            ? local.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("MMM d, yyyy, hh:mm tt", CultureInfo.GetCultureInfo("en-US"));
    }

    /// <summary>Maps a failed bridge result to the Dashboard state shown to the user.</summary>
    public static DashboardError ErrorFor(BridgeResult result) => result switch
    {
        { Status: 401 } or { ErrorCode: "UNAUTHORIZED" } => DashboardError.SessionEnded,
        { ErrorCode: "NOT_APPROVED" } => DashboardError.PendingApproval,
        { ErrorCode: "MAINTENANCE" or "MAINTENANCE_SETUP_REQUIRED" } or { Status: 503 } => DashboardError.Maintenance,
        { Status: 429 } or { ErrorCode: "RATE_LIMIT" } => DashboardError.RateLimited,
        { Status: 0 } => DashboardError.Connection,
        _ => DashboardError.Unexpected
    };

    internal static string? Text(JsonElement item, string property, int maxLength, bool trim = true)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        if (text is null) return null;
        if (trim) text = text.Trim();
        return text.Length == 0 || text.Length > maxLength ? null : text;
    }

    internal static DateTimeOffset? Date(JsonElement item, string property) =>
        Text(item, property, 40) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    internal static int Count(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count > 0 ? count : 0;
}
