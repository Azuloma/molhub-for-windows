using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MolHub.Windows;

/// <summary>The `/api/v1/profile` user DTO reduced to what Profile settings shows.</summary>
public sealed record ProfileInfo(string Id, string Username, string Status, string Role, byte[]? Avatar, DateTimeOffset? CreatedAt);

/// <summary>The password form fields whose length is outside the server's 12–128 characters.</summary>
[Flags]
public enum PasswordFieldError
{
    None = 0,
    CurrentLength = 1,
    NewLength = 2
}

/// <summary>Which Profile settings write a result belongs to.</summary>
public enum ProfileWrite
{
    Avatar,
    Password
}

/// <summary>
/// Pure rules for native Profile settings: the profile read (`profile`), the icon write (`updateAvatar`, a PNG data URL
/// or null) and the password change (`changePassword`). The server checks are mirrored so a request is only sent when
/// it can be accepted; the server stays the authority. A write whose outcome is unknown is never resent automatically.
/// </summary>
public static class ProfileModel
{
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 128;
    public const int MinAvatarBytes = 33;
    public const int MaxAvatarBytes = 32_768;
    public const int MaxAvatarSide = 128;
    public const int MaxDataUrlLength = 43_714;
    public const string PngDataPrefix = "data:image/png;base64,";

    private const int MaxIdLength = 100;
    private const int MaxUsernameLength = 128;
    private const int MaxStatusLength = 32;
    private const int MaxRoleLength = 32;

    // ----- Profile read -----

    /// <summary>Parses the profile `data` object; null when the shape is not the documented user DTO.</summary>
    public static ProfileInfo? Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        var id = Text(data, "id", MaxIdLength);
        var username = Text(data, "username", MaxUsernameLength);
        var status = Text(data, "status", MaxStatusLength);
        if (id is null || username is null || status is null) return null;
        var role = Text(data, "role", MaxRoleLength) ?? "member";

        byte[]? avatar = null;
        if (data.TryGetProperty("avatar", out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (text is not null && text.Length <= MaxDataUrlLength) avatar = ProfilePayloadParser.ParsePngDataUri(text);
        }

        DateTimeOffset? createdAt = null;
        if (Text(data, "createdAt", 40) is { } created
            && DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            createdAt = parsed;
        }
        return new ProfileInfo(id, username, status.ToLowerInvariant(), role.ToLowerInvariant(), avatar, createdAt);
    }

    /// <summary>The web identity label: Site administrator, Project owner or Member.</summary>
    public static string RoleKey(string role, bool projectManager) =>
        string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ? "Projects_RoleSiteAdmin"
        : projectManager ? "Projects_RoleOwner"
        : "Projects_RoleMember";

    // ----- Icon -----

    /// <summary>The data URL length for a PNG of this many bytes (base64 with padding).</summary>
    public static int DataUrlLength(int byteCount) => PngDataPrefix.Length + (byteCount + 2) / 3 * 4;

    public static string ToDataUrl(byte[] png) => PngDataPrefix + Convert.ToBase64String(png);

    /// <summary>
    /// The server's icon checks: 33–32,768 bytes, the PNG signature, a 13-byte IHDR chunk first, 1–128 pixels on each
    /// side, and a data URL of at most 43,714 characters.
    /// </summary>
    public static bool IsAcceptableAvatar(byte[]? png)
    {
        if (png is null || png.Length < MinAvatarBytes || png.Length > MaxAvatarBytes) return false;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        var bytes = png.AsSpan();
        if (!bytes[..8].SequenceEqual(signature)) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13) return false;
        if (!bytes[12..16].SequenceEqual("IHDR"u8)) return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        if (width is < 1 or > MaxAvatarSide || height is < 1 or > MaxAvatarSide) return false;
        return DataUrlLength(png.Length) <= MaxDataUrlLength;
    }

    /// <summary>The server's data URL checks (`data:image/png;base64,` + base64 only, length) followed by the PNG checks.</summary>
    public static bool IsAcceptableDataUrl(string? dataUrl)
    {
        if (dataUrl is null || dataUrl.Length > MaxDataUrlLength || !dataUrl.StartsWith(PngDataPrefix, StringComparison.Ordinal)) return false;
        var encoded = dataUrl.AsSpan(PngDataPrefix.Length);
        if (encoded.Length == 0) return false;
        foreach (var c in encoded)
        {
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=')) return false;
        }
        try
        {
            return IsAcceptableAvatar(Convert.FromBase64String(encoded.ToString()));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>`{ avatar }`: a valid PNG data URL, or null to remove the icon (the key is always present).</summary>
    public static JsonObject? AvatarPayload(string? dataUrl) =>
        dataUrl is null || IsAcceptableDataUrl(dataUrl) ? new JsonObject { ["avatar"] = dataUrl } : null;

    // ----- Password -----

    /// <summary>The server measures JavaScript string length (UTF-16 code units), which is `string.Length`.</summary>
    public static bool IsValidPassword(string? value) => value is not null && value.Length is >= MinPasswordLength and <= MaxPasswordLength;

    public static PasswordFieldError ValidatePasswords(string current, string next)
    {
        var errors = PasswordFieldError.None;
        if (!IsValidPassword(current)) errors |= PasswordFieldError.CurrentLength;
        if (!IsValidPassword(next)) errors |= PasswordFieldError.NewLength;
        return errors;
    }

    /// <summary>After a failed check a flagged field keeps its error until it is valid; an unflagged field gets none until the next Save.</summary>
    public static PasswordFieldError StillShown(PasswordFieldError shown, PasswordFieldError current) => shown & current;

    /// <summary>`{ current, password }` exactly as typed (passwords are never trimmed); null when a field is out of bounds.</summary>
    public static JsonObject? PasswordPayload(string current, string next) =>
        ValidatePasswords(current, next) == PasswordFieldError.None
            ? new JsonObject { ["current"] = current, ["password"] = next }
            : null;

    // ----- Answers -----

    /// <summary>
    /// Explains a Profile settings write on top of the shared write rules. LOGIN_FAILED is a 401 as well, but it means the
    /// current password is wrong, not that the session ended. An unknown outcome is never resent: the icon is re-read, and
    /// an unconfirmed password change ends with signing in again.
    /// </summary>
    public static WriteOutcome Explain(BridgeResult result, ProfileWrite write)
    {
        var outcome = WorkModel.Explain(result);
        switch (outcome.Kind)
        {
            case WriteOutcomeKind.Applied:
                return outcome;
            case WriteOutcomeKind.Unknown:
                return outcome with { MessageKey = write == ProfileWrite.Password ? "Profile_PasswordUnknown" : "Profile_AvatarUnknown" };
            case WriteOutcomeKind.NotSent:
                return write == ProfileWrite.Avatar && result.ErrorCode == "TOO_LARGE"
                    ? outcome with { MessageKey = "Profile_ErrorInvalidAvatar" }
                    : outcome;
        }

        var titleKey = write == ProfileWrite.Password ? "Profile_PasswordNotChangedTitle" : "Work_RejectedTitle";
        if (write == ProfileWrite.Password && result.ErrorCode == "LOGIN_FAILED")
        {
            return new WriteOutcome(WriteOutcomeKind.Rejected, titleKey, "Profile_ErrorCurrentPassword", SessionEnded: false);
        }
        if (outcome.SessionEnded) return outcome with { TitleKey = titleKey };
        var key = write switch
        {
            ProfileWrite.Avatar => result switch
            {
                { ErrorCode: "INVALID_AVATAR" or "TOO_LARGE" } or { Status: 413 } => "Profile_ErrorInvalidAvatar",
                _ => null
            },
            _ => result switch
            {
                { ErrorCode: "PASSWORD_LENGTH" } => "Profile_ErrorPasswordLength",
                { ErrorCode: "PASSWORD_CHANGE_CONFLICT" } => "Profile_ErrorPasswordConflict",
                { ErrorCode: "RATE_LIMIT" } or { Status: 429 } => "Profile_ErrorPasswordRateLimit",
                _ => null
            }
        };
        return outcome with { TitleKey = titleKey, MessageKey = key ?? outcome.MessageKey };
    }

    private static string? Text(JsonElement item, string property, int maxLength)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > maxLength ? null : text;
    }
}
