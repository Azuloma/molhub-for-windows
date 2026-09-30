using Windows.Storage;

namespace MolHub.Windows;

/// <summary>Thin LocalSettings access for the last-seen notification id; the rules live in <see cref="NotificationLastSeenPolicy"/>.</summary>
public static class NotificationLastSeenStore
{
    public static long? Read(string? userId)
    {
        var key = NotificationLastSeenPolicy.KeyFor(userId);
        if (key is null) return null;
        try { return NotificationLastSeenPolicy.ParseStored(ApplicationData.Current.LocalSettings.Values[key]); }
        catch { return null; }
    }

    public static bool Write(string? userId, long value)
    {
        var key = NotificationLastSeenPolicy.KeyFor(userId);
        if (key is null || value < 0) return false;
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = NotificationLastSeenPolicy.Format(value);
            return true;
        }
        catch { return false; }
    }
}
