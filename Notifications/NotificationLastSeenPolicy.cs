using System.Globalization;

namespace MolHub.Windows;

/// <summary>Pure key/value rules for the per-account last-seen notification id (stored in LocalSettings by <see cref="NotificationLastSeenStore"/>).</summary>
public static class NotificationLastSeenPolicy
{
    public const string KeyPrefix = "notifications.lastSeen.";
    public const int MaxDigits = 15;

    /// <summary>The setting key for an account, or null when the id is missing or not a valid id.</summary>
    public static string? KeyFor(string? userId)
    {
        var id = ProfilePayloadParser.NormalizeUserId(userId);
        return id is null ? null : KeyPrefix + id;
    }

    /// <summary>Accepts a string of 1-15 ASCII digits or an Int64 of 0 or more; anything else is null.</summary>
    public static long? ParseStored(object? value)
    {
        switch (value)
        {
            case string text:
                if (text.Length is < 1 or > MaxDigits || !text.All(char.IsAsciiDigit)) return null;
                return long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            case long number:
                return number >= 0 && number <= 999_999_999_999_999L ? number : null;
            default:
                return null;
        }
    }

    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
