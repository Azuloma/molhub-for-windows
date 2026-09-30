using System.Text.Json;
using System.Text.Json.Nodes;

namespace MolHub.Windows;

public sealed record NotificationApplyResult(
    IReadOnlyList<NotificationItem> NewItems,
    bool Reset,
    bool FetchImmediately,
    bool LastSeenChanged);

public enum NotificationNextKind
{
    /// <summary>Wait <see cref="NotificationNextStep.Seconds"/> and poll again.</summary>
    PollAfter,
    /// <summary>The server has more items: fetch the next page now.</summary>
    Immediate,
    /// <summary>Rate limited: wait <see cref="NotificationNextStep.Seconds"/> (from `details.retryAfter`).</summary>
    RetryAfter,
    /// <summary>Maintenance: pause polling until the connection or maintenance state changes.</summary>
    Pause,
    /// <summary>Not signed in or not approved: stop polling.</summary>
    Stop,
    /// <summary>Other failure or timeout: wait <see cref="NotificationNextStep.Seconds"/>, never a tight loop.</summary>
    Backoff
}

public readonly record struct NotificationNextStep(NotificationNextKind Kind, int Seconds);

/// <summary>Pure feed state: cursor, last-seen id, deduped items (newest 100), unread count and the next-request decision. No WinUI types, no clock.</summary>
public sealed class NotificationFeed
{
    public const int MaxItems = 100;
    public const int FirstLoadLimit = 100;
    public const int PageLimit = 100;
    public const int MinPollSeconds = 15;
    public const int DefaultRetrySeconds = 60;
    public const int MaxRetrySeconds = 900;
    public const int BackoffSeconds = 60;

    private readonly List<NotificationItem> _items = []; // ascending by id

    public long? Cursor { get; private set; }
    public long? LastSeen { get; private set; }
    public bool UnreadOverflow { get; private set; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<NotificationItem> Items => _items.AsEnumerable().Reverse().ToList();

    /// <summary>Items newer than the last-seen id; a reset or a last-seen clamp never changes it.</summary>
    public int UnreadCount { get; private set; }

    public void Initialize(long? storedLastSeen)
    {
        _items.Clear();
        UnreadCount = 0;
        Cursor = null;
        LastSeen = storedLastSeen is >= 0 ? storedLastSeen : null;
        UnreadOverflow = false;
    }

    public JsonObject BuildPayload()
    {
        var payload = new JsonObject();
        if (Cursor is { } cursor) payload["after"] = cursor;
        payload["limit"] = PageLimit;
        return payload;
    }

    public NotificationApplyResult ApplyFirstLoad(NotificationPage page)
    {
        _items.Clear();
        Merge(page.Items);
        Cursor = page.Meta.Cursor;
        var changed = false;
        if (LastSeen is null)
        {
            LastSeen = page.Meta.Cursor;
            changed = true;
        }
        else if (LastSeen > page.Meta.Cursor)
        {
            LastSeen = page.Meta.Cursor;
            changed = true;
        }
        var seen = LastSeen ?? 0;
        UnreadCount = _items.Count(item => item.Id > seen);
        UnreadOverflow = _items.Count == FirstLoadLimit && _items[0].Id > seen;
        return new NotificationApplyResult([], false, false, changed);
    }

    public NotificationApplyResult ApplyPoll(NotificationPage page)
    {
        Cursor = page.Meta.Cursor;
        if (page.Meta.Reset)
        {
            var clamped = false;
            if (LastSeen > page.Meta.Cursor)
            {
                LastSeen = page.Meta.Cursor;
                clamped = true;
            }
            return new NotificationApplyResult([], true, false, clamped);
        }

        var added = Merge(page.Items);
        var seen = LastSeen ?? 0;
        UnreadCount += added.Count(item => item.Id > seen);
        return new NotificationApplyResult(added, false, page.Meta.HasMore, false);
    }

    /// <summary>Marks everything shown as seen. Returns the id to persist, or null when the stored value does not change.</summary>
    public long? MarkAllSeen()
    {
        UnreadOverflow = false;
        UnreadCount = 0;
        var candidate = Math.Max(Math.Max(LastSeen ?? 0, Cursor ?? 0), _items.Count > 0 ? _items[^1].Id : 0);
        if (candidate == LastSeen || (LastSeen is null && candidate == 0)) return null;
        LastSeen = candidate;
        return candidate;
    }

    /// <summary>Adds unknown items (ascending), caps the list at 100 keeping the newest, and returns the genuinely new ones ascending.</summary>
    private List<NotificationItem> Merge(IReadOnlyList<NotificationItem> incoming)
    {
        var known = _items.Select(item => item.Id).ToHashSet();
        var added = new List<NotificationItem>();
        foreach (var item in incoming.OrderBy(item => item.Id))
        {
            if (!known.Add(item.Id)) continue;
            added.Add(item);
            _items.Add(item);
        }
        _items.Sort((a, b) => a.Id.CompareTo(b.Id));
        if (_items.Count > MaxItems) _items.RemoveRange(0, _items.Count - MaxItems);
        return added;
    }

    /// <summary>Decides the next request from a bridge result and (for a successful read) its parsed meta.</summary>
    public static NotificationNextStep Next(BridgeResult result, NotificationMeta? meta)
    {
        if (result.Ok)
        {
            if (meta is not { } m) return new NotificationNextStep(NotificationNextKind.Backoff, BackoffSeconds);
            return m.HasMore
                ? new NotificationNextStep(NotificationNextKind.Immediate, 0)
                : new NotificationNextStep(NotificationNextKind.PollAfter, Math.Max(MinPollSeconds, m.PollAfterSeconds));
        }

        var code = result.ErrorCode;
        if (result.Status == 429 || code == "RATE_LIMIT")
        {
            return new NotificationNextStep(NotificationNextKind.RetryAfter, RetryAfterSeconds(result.ErrorDetails));
        }
        if (result.Status == 503 || code == "MAINTENANCE") return new NotificationNextStep(NotificationNextKind.Pause, 0);
        if (result.Status is 401 or 403 || code is "UNAUTHORIZED" or "NOT_APPROVED") return new NotificationNextStep(NotificationNextKind.Stop, 0);
        return new NotificationNextStep(NotificationNextKind.Backoff, BackoffSeconds);
    }

    private static int RetryAfterSeconds(JsonElement? details)
    {
        if (details is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("retryAfter", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var seconds)
            && double.IsFinite(seconds))
        {
            return (int)Math.Clamp(Math.Ceiling(seconds), MinPollSeconds, MaxRetrySeconds);
        }
        return DefaultRetrySeconds;
    }
}
