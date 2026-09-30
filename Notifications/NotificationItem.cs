namespace MolHub.Windows;

/// <summary>The four activity kinds of the Web notification feed (wire strings in <see cref="NotificationPayloadParser"/>).</summary>
public enum NotificationKind
{
    ReservationStarted,
    ReservationCancelled,
    ReservationReleased,
    CommitPublished
}

public sealed record NotificationUser(string Id, string? Username);

public sealed record NotificationCommit(string Id, string Title, string Version);

public sealed record NotificationItem(
    long Id,
    NotificationKind Kind,
    string ProjectId,
    string ProjectName,
    NotificationUser? Actor,
    NotificationUser? Holder,
    NotificationCommit? Commit,
    DateTimeOffset? CreatedAt);

public readonly record struct NotificationMeta(long Cursor, bool HasMore, bool Reset, int PollAfterSeconds);

public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, NotificationMeta Meta);
