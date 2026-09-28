using System.Text.Json;
using System.Text.Json.Nodes;

namespace MolHub.Windows;

public sealed record ManagedProject(string Id, string Name, string? OwnerId, bool Deleted);

public sealed record ManagedProjectList(IReadOnlyList<ManagedProject> Items, int Total, int? NextOffset);

public sealed record ManagedMember(string Id, string Username, string Status, byte[]? Avatar, string Role = "member");

/// <summary>An approved account the manager can add (id and name only, as the server sends them).</summary>
public sealed record ManagedAccount(string Id, string Username);

public sealed record ManagedRequest(string Id, string Kind, string Status, string Reason, string Requester, DateTimeOffset? CreatedAt);

public sealed record ManagedEvent(string Kind, string Actor, string Target, DateTimeOffset? CreatedAt);

public sealed record ManagedLink(string Id, string ChannelName, string GuildId, string ChannelId, bool Enabled);

public sealed record ManagedReservation(string Username, byte[]? Avatar, DateTimeOffset? StartedAt);

/// <summary>`/api/v1/manage/projects/{id}` reduced to what the page shows and its writes need.</summary>
public sealed record ManagedProjectDetail(
    ManagedProject Project,
    IReadOnlyList<ManagedMember> Members,
    IReadOnlyDictionary<string, string> Accounts,
    IReadOnlyList<ManagedRequest> Requests,
    IReadOnlyList<ManagedEvent> Events,
    IReadOnlyList<ManagedLink> Links,
    ManagedReservation? Reservation,
    bool DiscordConfigured,
    IReadOnlyList<ManagedAccount>? Eligible = null);

public enum ManagedOwnerKind
{
    Account,
    SiteManaged,
    Unknown
}

/// <summary>
/// Pure rules for the Project management page reads (`manageProjects` / `manageProject`), mirroring the
/// web manage panel. The server lists the projects an approved owner owns (every project for a site admin),
/// soft-deleted ones last. Nothing here invents data: unknown kinds and statuses are shown as sent.
/// </summary>
public static class ManagementModel
{
    public const int ProjectLimit = 100;

    // The server does not limit members or approved accounts; generous caps keep the owner and member names resolvable.
    private const int MaxMembers = 1000;
    private const int MaxAccounts = 5000;
    private const int MaxRows = 200;
    private const int MaxNameLength = 200;
    private const int MaxReasonLength = 1000;
    private const int MaxDiscordIdLength = 40;

    /// <summary>Event kinds whose `target` is a user id (resolved to a username); other targets are internal ids.</summary>
    private static readonly HashSet<string> UserTargetKinds = new(StringComparer.Ordinal) { "member_added", "member_removed", "owner_assigned" };

    private static readonly HashSet<string> KnownStatuses = new(StringComparer.Ordinal) { "pending", "approved", "rejected", "cancelled", "suspended" };

    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal) { "delete", "restore", "discord" };

    private static readonly HashSet<string> KnownEvents = new(StringComparer.Ordinal)
    {
        "member_added", "member_removed", "owner_assigned", "reservation_released", "request_delete", "request_restore",
        "request_discord", "request_approved", "request_rejected", "discord_linked", "discord_disabled", "project_deleted",
        "project_restored"
    };

    public static JsonObject ListPayload() => new() { ["limit"] = ProjectLimit, ["offset"] = 0 };

    public static JsonObject? DetailPayload(string projectId) =>
        ProjectsModel.IsValidId(projectId) ? new JsonObject { ["projectId"] = projectId } : null;

    public static ManagedProjectList? ParseList(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var items = data.EnumerateArray().Take(ProjectLimit).Select(ParseProject).OfType<ManagedProject>().ToList();
        var (total, next) = ProjectsModel.Paging(meta, items.Count);
        return new ManagedProjectList(items, total, next);
    }

    public static ManagedProjectDetail? ParseDetail(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("project", out var projectElement)
            || ParseProject(projectElement) is not { } project)
        {
            return null;
        }

        var members = Items(data, "members", MaxMembers).Select(item =>
        {
            var id = DashboardModel.Text(item, "id", 100);
            var username = DashboardModel.Text(item, "username", MaxNameLength);
            return id is null || username is null ? null : new ManagedMember(id, username,
                DashboardModel.Text(item, "status", 20) ?? string.Empty,
                ProfilePayloadParser.ParsePngDataUri(DashboardModel.Text(item, "avatar", 64 * 1024, trim: false)),
                DashboardModel.Text(item, "role", 20) ?? "member");
        }).OfType<ManagedMember>().ToList();

        // Account names for resolving the owner and user-target events (members first, then approved accounts).
        var accounts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in members) accounts.TryAdd(member.Id, member.Username);
        var eligible = new List<ManagedAccount>();
        foreach (var item in Items(data, "eligible", MaxAccounts))
        {
            if (DashboardModel.Text(item, "id", 100) is not { } id || DashboardModel.Text(item, "username", MaxNameLength) is not { } name) continue;
            accounts.TryAdd(id, name);
            if (ProjectsModel.IsValidId(id)) eligible.Add(new ManagedAccount(id, name));
        }

        var requests = Items(data, "requests", MaxRows).Select(item =>
        {
            var id = DashboardModel.Text(item, "id", 100);
            var kind = DashboardModel.Text(item, "kind", 40);
            var status = DashboardModel.Text(item, "status", 40);
            return id is null || kind is null || status is null ? null : new ManagedRequest(id, kind, status,
                DashboardModel.Text(item, "reason", MaxReasonLength, trim: false)?.Trim() ?? string.Empty,
                DashboardModel.Text(item, "requester", MaxNameLength) ?? string.Empty,
                DashboardModel.Date(item, "createdAt"));
        }).OfType<ManagedRequest>().ToList();

        var events = Items(data, "logs", MaxRows).Select(item =>
            DashboardModel.Text(item, "kind", 60) is { } kind
                ? new ManagedEvent(kind, DashboardModel.Text(item, "actor", MaxNameLength) ?? string.Empty,
                    DashboardModel.Text(item, "target", 100) ?? string.Empty, DashboardModel.Date(item, "createdAt"))
                : null).OfType<ManagedEvent>().ToList();

        var links = Items(data, "links", MaxRows).Select(item =>
        {
            var id = DashboardModel.Text(item, "id", 100);
            var guild = DashboardModel.Text(item, "guildId", MaxDiscordIdLength);
            var channel = DashboardModel.Text(item, "channelId", MaxDiscordIdLength);
            return id is null || guild is null || channel is null ? null : new ManagedLink(id,
                DashboardModel.Text(item, "channelName", MaxNameLength) ?? string.Empty, guild, channel,
                item.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True);
        }).OfType<ManagedLink>().ToList();

        ManagedReservation? reservation = null;
        // A reservation without a holder name is not shown as a nameless holder.
        if (data.TryGetProperty("reservation", out var r) && r.ValueKind == JsonValueKind.Object
            && DashboardModel.Text(r, "username", MaxNameLength) is { } holder)
        {
            reservation = new ManagedReservation(holder,
                ProfilePayloadParser.ParsePngDataUri(DashboardModel.Text(r, "avatar", 64 * 1024, trim: false)),
                DashboardModel.Date(r, "startedAt"));
        }

        var configured = data.TryGetProperty("discord", out var discord) && discord.ValueKind == JsonValueKind.Object
            && discord.TryGetProperty("configured", out var c) && c.ValueKind == JsonValueKind.True;
        return new ManagedProjectDetail(project, members, accounts, requests, events, links, reservation, configured, eligible);
    }

    /// <summary>
    /// The project administrator as the web shows it: the owner's name, "Managed by site administrators" when the
    /// project has no owner, or unknown when the owner is neither a member nor an approved account.
    /// </summary>
    public static (ManagedOwnerKind Kind, string Name) Owner(ManagedProjectDetail detail) =>
        detail.Project.OwnerId is not { } ownerId ? (ManagedOwnerKind.SiteManaged, string.Empty)
        : detail.Accounts.TryGetValue(ownerId, out var name) ? (ManagedOwnerKind.Account, name)
        : (ManagedOwnerKind.Unknown, string.Empty);

    /// <summary>The event's target account name for member events; empty for other kinds or unknown accounts.</summary>
    public static string TargetName(ManagedEvent item, ManagedProjectDetail detail) =>
        UserTargetKinds.Contains(item.Kind) && detail.Accounts.TryGetValue(item.Target, out var name) ? name : string.Empty;

    /// <summary>Keeps the chosen project when it is still listed, else the first one (the web's default).</summary>
    public static string? Select(IReadOnlyList<ManagedProject> projects, string? current) =>
        current is not null && projects.Any(project => project.Id == current) ? current : projects.FirstOrDefault()?.Id;

    public static string? StatusKey(string status) => KnownStatuses.Contains(status) ? "Manage_Status_" + status : null;

    public static string? KindKey(string kind) => KnownKinds.Contains(kind) ? "Manage_Kind_" + kind : null;

    public static string? EventKey(string kind) => KnownEvents.Contains(kind) ? "Manage_Event_" + kind : null;

    private static ManagedProject? ParseProject(JsonElement item)
    {
        var id = DashboardModel.Text(item, "id", 100);
        var name = DashboardModel.Text(item, "name", MaxNameLength);
        if (!ProjectsModel.IsValidId(id) || name is null) return null;
        return new ManagedProject(id!, name, DashboardModel.Text(item, "ownerId", 100),
            item.TryGetProperty("deletedAt", out var deleted) && deleted.ValueKind == JsonValueKind.String);
    }

    private static IEnumerable<JsonElement> Items(JsonElement data, string property, int max) =>
        data.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Take(max)
            : [];
}
