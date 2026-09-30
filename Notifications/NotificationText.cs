using System.Globalization;

namespace MolHub.Windows;

/// <summary>Pure text for a notification: the localized sentence and the commit line. The localizer maps a resource key to its format string.</summary>
public static class NotificationText
{
    public static string Sentence(NotificationItem item, Func<string, string> localize)
    {
        var project = item.ProjectName;
        switch (item.Kind)
        {
            case NotificationKind.ReservationStarted:
                return Format(localize("Notifications_ReservationStarted"), UserName(item.Actor, localize), project);
            case NotificationKind.ReservationCancelled:
                // The actor is the holder; fall back to the holder if the actor is missing.
                return Format(localize("Notifications_ReservationCancelled"), UserName(item.Actor ?? item.Holder, localize), project);
            case NotificationKind.ReservationReleased:
                // No actor: the server released the reservation itself (membership removed or account suspended).
                return item.Actor is null
                    ? Format(localize("Notifications_ReservationReleasedSystem"), UserName(item.Holder, localize), project)
                    : Format(localize("Notifications_ReservationReleased"), UserName(item.Actor, localize), UserName(item.Holder, localize), project);
            case NotificationKind.CommitPublished:
                return Format(localize("Notifications_CommitPublished"), UserName(item.Actor, localize), project);
            default:
                return project;
        }
    }

    /// <summary>The "{title} · {version}" second line of a published commit; null for other kinds or a commit without text.</summary>
    public static string? CommitLine(NotificationItem item, Func<string, string> localize)
    {
        if (item.Kind != NotificationKind.CommitPublished || item.Commit is not { } commit) return null;
        var title = commit.Title.Trim();
        var version = commit.Version.Trim();
        if (title.Length == 0 && version.Length == 0) return null;
        if (title.Length == 0) return version;
        if (version.Length == 0) return title;
        return Format(localize("Notifications_CommitLineFormat"), title, version);
    }

    /// <summary>The icon glyph per kind (Segoe Fluent Icons).</summary>
    public static string Glyph(NotificationKind kind) => kind switch
    {
        NotificationKind.ReservationStarted => "\uE768",
        NotificationKind.ReservationCancelled => "\uE711",
        NotificationKind.ReservationReleased => "\uE785",
        _ => "\uE898"
    };

    private static string UserName(NotificationUser? user, Func<string, string> localize) =>
        string.IsNullOrWhiteSpace(user?.Username) ? localize("Notifications_UnknownUser") : user!.Username!;

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
