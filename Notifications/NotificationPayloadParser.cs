using System.Globalization;
using System.Text.Json;

namespace MolHub.Windows;

/// <summary>Pure, bounded parser for the bridge `notifications` response. Unknown kinds and invalid items are skipped.</summary>
public static class NotificationPayloadParser
{
    public const int MaxItems = 100;
    public const int MaxProjectNameLength = 200;
    public const int MaxUsernameLength = 128;
    public const int MaxCommitTitleLength = 300;
    public const int MaxCommitVersionLength = 64;
    public const int MinPollAfterSeconds = 15;
    public const int MaxPollAfterSeconds = 300;

    /// <summary>Null when data is not an array of at most 100 objects or meta is missing/invalid.</summary>
    public static NotificationPage? Parse(JsonElement? data, JsonElement? meta)
    {
        if (data is not { ValueKind: JsonValueKind.Array } array) return null;
        var parsedMeta = ParseMeta(meta);
        if (parsedMeta is null) return null;
        if (array.GetArrayLength() > MaxItems) return null;

        var items = new List<NotificationItem>();
        foreach (var element in array.EnumerateArray())
        {
            var item = ParseItem(element);
            if (item is not null) items.Add(item);
        }
        return new NotificationPage(items, parsedMeta.Value);
    }

    public static NotificationMeta? ParseMeta(JsonElement? meta)
    {
        if (meta is not { ValueKind: JsonValueKind.Object } obj) return null;
        if (!ReadInt64(obj, "cursor", out var cursor) || cursor < 0) return null;
        if (!ReadBool(obj, "hasMore", out var hasMore) || !ReadBool(obj, "reset", out var reset)) return null;
        var pollAfter = MinPollAfterSeconds;
        if (obj.TryGetProperty("pollAfter", out var poll) && poll.ValueKind == JsonValueKind.Number && poll.TryGetInt32(out var seconds))
        {
            pollAfter = Math.Clamp(seconds, MinPollAfterSeconds, MaxPollAfterSeconds);
        }
        return new NotificationMeta(cursor, hasMore, reset, pollAfter);
    }

    private static NotificationItem? ParseItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!ReadInt64(element, "id", out var id) || id <= 0) return null;
        if (!element.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String) return null;
        var kind = kindElement.GetString() switch
        {
            "reservation_started" => (NotificationKind?)NotificationKind.ReservationStarted,
            "reservation_cancelled" => NotificationKind.ReservationCancelled,
            "reservation_released" => NotificationKind.ReservationReleased,
            "commit_published" => NotificationKind.CommitPublished,
            _ => null
        };
        if (kind is null) return null;

        var projectId = ReadString(element, "projectId", out var projectIdOk);
        if (!projectIdOk || ProfilePayloadParser.NormalizeUserId(projectId) is null) return null;
        var projectName = ReadString(element, "projectName", out var nameOk)?.Trim();
        if (!nameOk || string.IsNullOrEmpty(projectName) || projectName.Length > MaxProjectNameLength) return null;

        if (!ReadUser(element, "actor", out var actor) || !ReadUser(element, "holder", out var holder)) return null;

        NotificationCommit? commit = null;
        if (kind == NotificationKind.CommitPublished)
        {
            commit = ReadCommit(element);
            if (commit is null) return null;
        }

        DateTimeOffset? createdAt = null;
        if (element.TryGetProperty("createdAt", out var created) && created.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            createdAt = parsed;
        }

        return new NotificationItem(id, kind.Value, projectId!, projectName, actor, holder, commit, createdAt);
    }

    private static bool ReadUser(JsonElement parent, string name, out NotificationUser? user)
    {
        user = null;
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.Object) return false;
        var id = ReadString(value, "id", out var idOk);
        if (!idOk || ProfilePayloadParser.NormalizeUserId(id) is null) return false;
        string? username = null;
        if (value.TryGetProperty("username", out var usernameElement) && usernameElement.ValueKind != JsonValueKind.Null)
        {
            if (usernameElement.ValueKind != JsonValueKind.String) return false;
            username = usernameElement.GetString();
            if (username is not null && username.Length > MaxUsernameLength) return false;
        }
        user = new NotificationUser(id!, username);
        return true;
    }

    private static NotificationCommit? ReadCommit(JsonElement parent)
    {
        if (!parent.TryGetProperty("commit", out var value) || value.ValueKind != JsonValueKind.Object) return null;
        var id = ReadString(value, "id", out var idOk);
        if (!idOk || ProfilePayloadParser.NormalizeUserId(id) is null) return null;
        var title = ReadString(value, "title", out var titleOk);
        if (!titleOk || title is null || title.Length > MaxCommitTitleLength) return null;
        var version = ReadString(value, "version", out var versionOk);
        if (!versionOk || version is null || version.Length > MaxCommitVersionLength) return null;
        return new NotificationCommit(id!, title, version);
    }

    private static string? ReadString(JsonElement parent, string name, out bool ok)
    {
        ok = parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String;
        return ok ? value.GetString() : null;
    }

    private static bool ReadInt64(JsonElement parent, string name, out long value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private static bool ReadBool(JsonElement parent, string name, out bool value)
    {
        value = false;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.ValueKind == JsonValueKind.True;
        return true;
    }
}
