using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MolHub.Windows;

/// <summary>An active reservation; `Base` is the head it was started from, which a publish must name again.</summary>
public sealed record ProjectReservation(string? Username, DateTimeOffset? StartedAt, string? Base = null);

public sealed record ProjectLatest(string Id, string Title, string Version, DateTimeOffset? CreatedAt);

public sealed record ProjectSummary(
    string Id,
    string Name,
    string Description,
    ProjectLatest? Latest,
    ProjectReservation? Reservation,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? HeadId = null);

public sealed record ProjectListPage(IReadOnlyList<ProjectSummary> Projects, int Total, int? NextOffset);

public sealed record ProjectMember(string Username, string Role, byte[]? Avatar);

public sealed record ProjectCommit(
    string Id,
    string ProjectId,
    string ProjectName,
    string Title,
    string Changes,
    Uri? ShareUri,
    string Version,
    string AuthorName,
    byte[]? AuthorAvatar,
    DateTimeOffset? CreatedAt);

public sealed record ProjectCommitSummary(int Total, int Contributors, int Versions);

public sealed record ProjectDetail(
    ProjectSummary Project,
    IReadOnlyList<ProjectMember> Members,
    IReadOnlyList<ProjectCommit> Commits,
    ProjectCommitSummary Summary)
{
    /// <summary>The published head as a full commit when it is in the first page (it normally is: newest first).</summary>
    public ProjectCommit? Head => Project.Latest is { } latest ? Commits.FirstOrDefault(commit => commit.Id == latest.Id) : null;
}

public sealed record ProjectCommitPage(IReadOnlyList<ProjectCommit> Commits, int Total, int? NextOffset);

public sealed record CommitBase(string Id, string Title, string Version, DateTimeOffset? CreatedAt);

public sealed record CommitDetail(ProjectCommit Commit, CommitBase? Base);

/// <summary>
/// Commit list filters with the same bounds as the web (`q` ≤ 150 characters, `days` 0/7/30/90, latest only). `Project`
/// is used only by the personal history; a project's own commit list already names its project in the path.
/// </summary>
public sealed record CommitFilter(string Query, int Days, bool LatestOnly, string Project = "")
{
    public static readonly CommitFilter None = new(string.Empty, 0, false);

    public bool IsActive => Query.Length > 0 || Days != 0 || LatestOnly || Project.Length > 0;
}

public enum ReservationState
{
    Available,
    Yours,
    Other
}

public enum ProjectsError
{
    Connection,
    SessionEnded,
    PendingApproval,
    Maintenance,
    RateLimited,
    NoAccess,
    Unexpected
}

/// <summary>
/// Pure rules for the read-only native Projects pages (`projects`, `project`, `projectCommits`, `commit` bridge
/// reads), mirroring the web project list and project overview. Nothing here invents data.
/// </summary>
public static partial class ProjectsModel
{
    public const int PageSize = 30;
    public const int VersionRowLimit = 8;
    public const int MaxQueryLength = 150;
    public static readonly IReadOnlyList<int> DayOptions = [0, 7, 30, 90];

    private const int MaxItems = 100;
    private const int MaxOffset = 5000;
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 1000;
    private const int MaxTitleLength = 200;
    private const int MaxChangesLength = 10_000;
    private const int MaxVersionLength = 40;
    private const int MaxUrlLength = 2048;

    // Same pattern as the bridge's `id()` check, so a stored id can always be sent back.
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,100}$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    public static string NormalizeQuery(string? query)
    {
        var text = (query ?? string.Empty).Trim();
        return text.Length > MaxQueryLength ? text[..MaxQueryLength] : text;
    }

    public static JsonObject ListPayload(string? query, int offset)
    {
        var payload = new JsonObject { ["limit"] = PageSize, ["offset"] = Math.Clamp(offset, 0, MaxOffset) };
        var q = NormalizeQuery(query);
        if (q.Length > 0) payload["q"] = q;
        return payload;
    }

    public static JsonObject? ProjectPayload(string projectId) =>
        IsValidId(projectId) ? new JsonObject { ["projectId"] = projectId } : null;

    public static JsonObject? CommitsPayload(string projectId, CommitFilter filter, int offset)
    {
        if (!IsValidId(projectId)) return null;
        var payload = new JsonObject { ["projectId"] = projectId };
        AddCommitFilter(payload, filter, offset);
        return payload;
    }

    /// <summary>Adds paging and the `q`/`days`/`latest` filters shared by project commits and the personal history.</summary>
    internal static void AddCommitFilter(JsonObject payload, CommitFilter filter, int offset)
    {
        payload["limit"] = PageSize;
        payload["offset"] = Math.Clamp(offset, 0, MaxOffset);
        var q = NormalizeQuery(filter.Query);
        if (q.Length > 0) payload["q"] = q;
        if (filter.Days != 0 && DayOptions.Contains(filter.Days)) payload["days"] = filter.Days;
        // The server only treats latest=1 as set.
        if (filter.LatestOnly) payload["latest"] = 1;
    }

    public static JsonObject? CommitPayload(string commitId) =>
        IsValidId(commitId) ? new JsonObject { ["commitId"] = commitId } : null;

    /// <summary>Parses the `projects` list (`data` array, `meta.total`/`meta.nextOffset`); null when the shape is wrong.</summary>
    public static ProjectListPage? ParseProjectList(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var projects = data.EnumerateArray().Take(MaxItems).Select(ParseProject).OfType<ProjectSummary>().ToList();
        var (total, next) = Paging(meta, projects.Count);
        return new ProjectListPage(projects, total, next);
    }

    /// <summary>Parses the `project` overview (`project`, `members`, first commit page, `summary`).</summary>
    public static ProjectDetail? ParseProjectDetail(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        if (!data.TryGetProperty("project", out var projectElement) || ParseProject(projectElement) is not { } project) return null;

        var members = new List<ProjectMember>();
        if (data.TryGetProperty("members", out var memberArray) && memberArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in memberArray.EnumerateArray().Take(MaxItems))
            {
                if (DashboardModel.Text(item, "username", MaxNameLength) is not { } username) continue;
                members.Add(new ProjectMember(
                    username,
                    DashboardModel.Text(item, "role", 20) ?? "member",
                    ProfilePayloadParser.ParsePngDataUri(DashboardModel.Text(item, "avatar", 64 * 1024, trim: false))));
            }
        }

        var commits = data.TryGetProperty("commits", out var commitArray) ? ParseCommits(commitArray) : [];
        var summary = new ProjectCommitSummary(commits.Count, 0, 0);
        if (data.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.Object)
        {
            summary = new ProjectCommitSummary(
                DashboardModel.Count(summaryElement, "total"),
                DashboardModel.Count(summaryElement, "contributors"),
                DashboardModel.Count(summaryElement, "versions"));
        }
        return new ProjectDetail(project, members, commits, summary);
    }

    /// <summary>Parses a `projectCommits` page.</summary>
    public static ProjectCommitPage? ParseCommitPage(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var commits = ParseCommits(data);
        var (total, next) = Paging(meta, commits.Count);
        return new ProjectCommitPage(commits, total, next);
    }

    /// <summary>Parses a `commit` detail with its same-project `baseSummary`.</summary>
    public static CommitDetail? ParseCommitDetail(JsonElement data)
    {
        if (ParseCommit(data) is not { } commit) return null;
        CommitBase? commitBase = null;
        if (data.TryGetProperty("baseSummary", out var b) && b.ValueKind == JsonValueKind.Object
            && DashboardModel.Text(b, "id", 100) is { } baseId && IsValidId(baseId))
        {
            commitBase = new CommitBase(
                baseId,
                DashboardModel.Text(b, "title", MaxTitleLength) ?? string.Empty,
                DashboardModel.Text(b, "version", MaxVersionLength) ?? string.Empty,
                DashboardModel.Date(b, "createdAt"));
        }
        return new CommitDetail(commit, commitBase);
    }

    /// <summary>Only absolute http(s) links without credentials are opened, and only in the default browser.</summary>
    public static Uri? SafeShareUri(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaxUrlLength) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        return uri.UserInfo.Length == 0 ? uri : null;
    }

    public static ReservationState StateFor(ProjectReservation? reservation, string username) =>
        reservation is null ? ReservationState.Available
        : reservation.Username is { } holder && string.Equals(holder, username, StringComparison.OrdinalIgnoreCase) ? ReservationState.Yours
        : ReservationState.Other;

    /// <summary>The web shows the version label, or the 7-character commit ID when no label was given.</summary>
    public static string VersionLabel(string version, string id) => version.Length > 0 ? version : DashboardModel.ShortId(id);

    /// <summary>Resource key for a member's role on the project (`owner` from the server, else the site role).</summary>
    public static string RoleKey(string role) => role switch
    {
        "owner" => "Projects_RoleOwner",
        "admin" => "Projects_RoleSiteAdmin",
        _ => "Projects_RoleMember"
    };

    /// <summary>Groups commits (already newest first) by local calendar day, like the web history timeline.</summary>
    public static IReadOnlyList<(DateOnly? Day, IReadOnlyList<ProjectCommit> Commits)> GroupByDay(IEnumerable<ProjectCommit> commits, TimeZoneInfo? zone = null)
    {
        var groups = new List<(DateOnly? Day, IReadOnlyList<ProjectCommit> Commits)>();
        foreach (var commit in commits)
        {
            DateOnly? day = commit.CreatedAt is { } created ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(created, zone ?? TimeZoneInfo.Local).DateTime) : null;
            if (groups.Count > 0 && groups[^1].Day == day)
            {
                ((List<ProjectCommit>)groups[^1].Commits).Add(commit);
            }
            else
            {
                groups.Add((day, new List<ProjectCommit> { commit }));
            }
        }
        return groups;
    }

    /// <summary>Day heading in the app language ("September 19, 2026" / "2026年9月19日").</summary>
    public static string FormatDay(DateOnly? day, string language) =>
        day is not { } value ? string.Empty
        : language == "ja-JP" ? value.ToString("yyyy年M月d日", CultureInfo.InvariantCulture)
        : value.ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>Maps a failed read to the state shown on the Projects pages.</summary>
    public static ProjectsError ErrorFor(BridgeResult result) => result switch
    {
        { Status: 401 } or { ErrorCode: "UNAUTHORIZED" } => ProjectsError.SessionEnded,
        { ErrorCode: "NOT_APPROVED" } => ProjectsError.PendingApproval,
        { ErrorCode: "FORBIDDEN" or "PROJECT_FORBIDDEN" or "NOT_OWNER" or "NOT_FOUND" } or { Status: 404 } => ProjectsError.NoAccess,
        { ErrorCode: "MAINTENANCE" or "MAINTENANCE_SETUP_REQUIRED" } or { Status: 503 } => ProjectsError.Maintenance,
        { Status: 429 } or { ErrorCode: "RATE_LIMIT" } => ProjectsError.RateLimited,
        { Status: 0 } => ProjectsError.Connection,
        _ => ProjectsError.Unexpected
    };

    private static ProjectSummary? ParseProject(JsonElement item)
    {
        var id = DashboardModel.Text(item, "id", 100);
        var name = DashboardModel.Text(item, "name", MaxNameLength);
        if (!IsValidId(id) || name is null) return null;

        ProjectLatest? latest = null;
        if (item.TryGetProperty("latest", out var l) && l.ValueKind == JsonValueKind.Object
            && DashboardModel.Text(l, "id", 100) is { } latestId && IsValidId(latestId))
        {
            latest = new ProjectLatest(
                latestId,
                DashboardModel.Text(l, "title", MaxTitleLength) ?? string.Empty,
                DashboardModel.Text(l, "version", MaxVersionLength) ?? string.Empty,
                DashboardModel.Date(l, "createdAt"));
        }

        ProjectReservation? reservation = null;
        if (item.TryGetProperty("reservation", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            var reservationBase = DashboardModel.Text(r, "base", 100);
            reservation = new ProjectReservation(DashboardModel.Text(r, "username", MaxNameLength), DashboardModel.Date(r, "startedAt"),
                IsValidId(reservationBase) ? reservationBase : null);
        }

        var head = DashboardModel.Text(item, "head", 100);
        return new ProjectSummary(
            id!,
            name,
            DashboardModel.Text(item, "description", MaxDescriptionLength) ?? string.Empty,
            latest,
            reservation,
            DashboardModel.Date(item, "createdAt"),
            DashboardModel.Date(item, "updatedAt"),
            IsValidId(head) ? head : null);
    }

    internal static List<ProjectCommit> ParseCommits(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Take(MaxItems).Select(ParseCommit).OfType<ProjectCommit>().ToList()
            : [];

    private static ProjectCommit? ParseCommit(JsonElement item)
    {
        var id = DashboardModel.Text(item, "id", 100);
        var title = DashboardModel.Text(item, "title", MaxTitleLength);
        if (!IsValidId(id) || title is null) return null;
        var projectId = DashboardModel.Text(item, "projectId", 100);

        string? authorName = null;
        byte[]? avatar = null;
        if (item.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object)
        {
            authorName = DashboardModel.Text(author, "username", MaxNameLength);
            avatar = ProfilePayloadParser.ParsePngDataUri(DashboardModel.Text(author, "avatar", 64 * 1024, trim: false));
        }

        return new ProjectCommit(
            id!,
            IsValidId(projectId) ? projectId! : string.Empty,
            DashboardModel.Text(item, "projectName", MaxNameLength) ?? string.Empty,
            title,
            DashboardModel.Text(item, "changes", MaxChangesLength, trim: false) ?? string.Empty,
            SafeShareUri(DashboardModel.Text(item, "url", MaxUrlLength)),
            DashboardModel.Text(item, "version", MaxVersionLength) ?? string.Empty,
            authorName ?? string.Empty,
            avatar,
            DashboardModel.Date(item, "createdAt"));
    }

    internal static (int Total, int? NextOffset) Paging(JsonElement? meta, int count)
    {
        if (meta is not { ValueKind: JsonValueKind.Object } m) return (count, null);
        // TryGetInt32 throws on non-numbers, and `nextOffset` is null at the end of a list.
        var total = m.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var value) && value >= count ? value : count;
        int? next = m.TryGetProperty("nextOffset", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var offset)
            && offset > 0 && offset <= MaxOffset ? offset : null;
        return (total, next);
    }
}
