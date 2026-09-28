using System.Text.Json;
using System.Text.Json.Nodes;

namespace FusionLedger.Windows;

public sealed record AdminRequest(string Id, string ProjectName, string Kind, string Status, string Reason, string Requester, DateTimeOffset? CreatedAt);
public sealed record AdminProject(string Id, string Name, bool Deleted, DateTimeOffset? DeletedAt, bool ReservationPresent, string? Holder, DateTimeOffset? ReservationStartedAt);
public sealed record AdminMember(string Id, string Username, string Status, string Role, byte[]? Avatar);
public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Total, int? NextOffset);
public sealed record AdminDiscordSetup(bool Configured, bool ApplicationId, bool PublicKey, bool BotToken, bool SiteUrl);
public sealed record AdminDiscordLink(string ProjectName, string GuildId, string ChannelId, string ChannelName, string Language, bool Enabled, bool Deleted);
public sealed record AdminDiscordDelivery(string ProjectName, string ChannelName, string Status, int Attempts, string LastError, string CommitId);
public sealed record AdminDiscordData(AdminDiscordSetup Setup, IReadOnlyList<AdminDiscordLink> Links, IReadOnlyList<AdminDiscordDelivery> Deliveries);
public sealed record AdminMaintenance(bool Active, DateTimeOffset? StartedAt, IReadOnlyList<Announcement> History);
public sealed record AdminAudit(string Action, string Actor, string Target, DateTimeOffset? CreatedAt);

/// <summary>Pure, bounded parsing and display rules for the read-only Administration sections.</summary>
public static class AdministrationModel
{
    public const int RequestPageSize = 50;
    public const int ProjectPageSize = 100;
    public const int MemberPageSize = 50;
    public const int AuditPageSize = 50;
    public const int MaxRows = 200;
    public static readonly string[] Sections = ["Requests", "Projects", "Members", "Reservations", "Discord", "Maintenance", "Audit"];
    public const string DefaultSection = "Requests";

    private static readonly HashSet<string> AuditActions = new(StringComparer.Ordinal)
    {
        "approved", "rejected", "suspended", "force_release", "reset_code_issued", "password_reset", "project_created",
        "admin_created", "admin_recovered", "project_deleted", "project_restored", "auth_smoke_test", "discord_linked",
        "discord_disabled", "discord_retry", "discord_commands_registered", "discord_commands_migrated"
    };
    private static readonly HashSet<string> DeliveryStatuses = new(StringComparer.Ordinal) { "pending", "sending", "sent", "failed", "cancelled" };

    public static JsonObject ListPayload(string section, int offset) => new()
    {
        ["limit"] = section switch { "Requests" => RequestPageSize, "Projects" or "Reservations" => ProjectPageSize, "Members" => MemberPageSize, _ => AuditPageSize },
        ["offset"] = Math.Clamp(offset, 0, 5000)
    };
    public static string? Command(string section) => section switch
    {
        "Requests" => "adminRequests", "Projects" or "Reservations" => "adminProjects", "Members" => "adminUsers",
        "Discord" => "adminDiscord", "Maintenance" => "adminMaintenance", "Audit" => "adminAudit", _ => null
    };
    public static bool IsPaged(string section) => section is "Requests" or "Projects" or "Reservations" or "Members" or "Audit";

    public static AdminPage<AdminRequest>? ParseRequests(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var items = data.EnumerateArray().Take(MaxRows).Select(item =>
        {
            var id = DashboardModel.Text(item, "id", 100); var kind = DashboardModel.Text(item, "kind", 60); var status = DashboardModel.Text(item, "status", 60);
            return id is null || kind is null || status is null ? null : new AdminRequest(id,
                DashboardModel.Text(item, "projectName", 200) ?? string.Empty, kind, status,
                DashboardModel.Text(item, "reason", 1000, trim: false)?.Trim() ?? string.Empty,
                DashboardModel.Text(item, "requester", 200) ?? string.Empty, DashboardModel.Date(item, "createdAt"));
        }).OfType<AdminRequest>().ToList();
        var (total, next) = ProjectsModel.Paging(meta, items.Count); return new(items, total, next);
    }

    public static AdminPage<AdminProject>? ParseProjects(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var items = data.EnumerateArray().Take(MaxRows).Select(item =>
        {
            var id = DashboardModel.Text(item, "id", 100); var name = DashboardModel.Text(item, "name", 200);
            if (!ProjectsModel.IsValidId(id) || name is null) return null;
            var reservationPresent = item.TryGetProperty("reservation", out var reservation) && reservation.ValueKind == JsonValueKind.Object;
            return new AdminProject(id!, name, item.TryGetProperty("deletedAt", out var deleted) && deleted.ValueKind == JsonValueKind.String,
                DashboardModel.Date(item, "deletedAt"), reservationPresent,
                reservationPresent ? DashboardModel.Text(reservation, "username", 200) : null,
                reservationPresent ? DashboardModel.Date(reservation, "startedAt") : null);
        }).OfType<AdminProject>().ToList();
        var (total, next) = ProjectsModel.Paging(meta, items.Count); return new(items, total, next);
    }

    public static AdminPage<AdminMember>? ParseMembers(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var items = data.EnumerateArray().Take(MaxRows).Select(item =>
        {
            var id = DashboardModel.Text(item, "id", 100); var name = DashboardModel.Text(item, "username", 200);
            return id is null || name is null ? null : new AdminMember(id, name,
                DashboardModel.Text(item, "status", 40) ?? string.Empty, DashboardModel.Text(item, "role", 40) ?? "member",
                ProfilePayloadParser.ParsePngDataUri(DashboardModel.Text(item, "avatar", 64 * 1024, trim: false)));
        }).OfType<AdminMember>().ToList();
        var (total, next) = ProjectsModel.Paging(meta, items.Count); return new(items, total, next);
    }

    public static AdminDiscordData? ParseDiscord(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        var setup = data.TryGetProperty("setup", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        var checks = setup.ValueKind == JsonValueKind.Object && setup.TryGetProperty("checks", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        var parsedSetup = new AdminDiscordSetup(Bool(setup, "configured"), Bool(checks, "applicationId"), Bool(checks, "publicKey"), Bool(checks, "botToken"), Bool(checks, "siteUrl"));
        var links = Items(data, "links", 200).Select(item =>
        {
            var guild = DashboardModel.Text(item, "guildId", 40); var channel = DashboardModel.Text(item, "channelId", 40);
            return guild is null || channel is null ? null : new AdminDiscordLink(DashboardModel.Text(item, "projectName", 200) ?? string.Empty,
                guild, channel, DashboardModel.Text(item, "channelName", 200) ?? string.Empty, DashboardModel.Text(item, "locale", 20) ?? string.Empty,
                Bool(item, "enabled"), Bool(item, "projectDeleted"));
        }).OfType<AdminDiscordLink>().ToList();
        var deliveries = Items(data, "deliveries", 30).Select(item =>
        {
            var status = DashboardModel.Text(item, "status", 40); var commit = DashboardModel.Text(item, "commitId", 100);
            return status is null || commit is null ? null : new AdminDiscordDelivery(DashboardModel.Text(item, "projectName", 200) ?? string.Empty,
                DashboardModel.Text(item, "channelName", 200) ?? string.Empty, status,
                Int(item, "attempts"), DashboardModel.Text(item, "lastError", 500) ?? string.Empty, commit);
        }).OfType<AdminDiscordDelivery>().ToList();
        return new(parsedSetup, links, deliveries);
    }

    public static AdminMaintenance? ParseMaintenance(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        JsonElement? state = data.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Object ? s : null;
        var started = state is { } value ? DashboardModel.Date(value, "startedAt") : null;
        var history = Items(data, "history", 100).Select(MaintenanceModel.Parse).OfType<Announcement>().ToList();
        return new(active.GetBoolean(), started, history);
    }

    public static AdminPage<AdminAudit>? ParseAudit(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var items = data.EnumerateArray().Take(MaxRows).Select(item =>
        {
            var action = DashboardModel.Text(item, "action", 100);
            return action is null ? null : new AdminAudit(action, DashboardModel.Text(item, "actor", 200) ?? string.Empty,
                DashboardModel.Text(item, "target", 200) ?? string.Empty, DashboardModel.Date(item, "createdAt"));
        }).OfType<AdminAudit>().ToList();
        var (total, next) = ProjectsModel.Paging(meta, items.Count); return new(items, total, next);
    }

    public static string ActionLabelKey(string action) => AuditActions.Contains(action) ? "Admin_Audit_" + action : action;
    public static string DeliveryStatusKey(string status) => DeliveryStatuses.Contains(status) ? "Admin_Discord_Status_" + status : status;
    public static IReadOnlyList<AdminProject> ActiveProjects(AdminPage<AdminProject>? page) => page?.Items.Where(p => !p.Deleted).ToList() ?? [];
    public static IReadOnlyList<AdminProject> DeletedProjects(AdminPage<AdminProject>? page) => page?.Items.Where(p => p.Deleted).ToList() ?? [];
    public static IReadOnlyList<AdminProject> Reservations(AdminPage<AdminProject>? page) => ActiveProjects(page).Where(p => p.ReservationPresent).ToList();

    private static bool Bool(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= 0 ? n : 0;
    private static IEnumerable<JsonElement> Items(JsonElement item, string key, int max) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Take(max) : [];
}
