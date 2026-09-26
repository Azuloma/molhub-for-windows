using System.Text.Json;

namespace FusionLedger.Windows;

public sealed record AuthenticatedUser(
    string Username,
    byte[]? AvatarPng,
    string Status,
    string Role,
    bool ProjectManager);

/// <summary>Pure, bounded validation for the native login observer.</summary>
public static class ProfilePayloadParser
{
    public const int MaxResponseBytes = 256 * 1024;
    public const int MaxAvatarBytes = 32 * 1024;
    public const int MaxUsernameLength = 128;
    public const int MaxStatusLength = 32;
    public const int MaxRoleLength = 32;
    private const string PngDataPrefix = "data:image/png;base64,";
    private static readonly string[] AllowedStatuses = ["pending", "approved", "rejected", "suspended"];
    private static readonly string[] AllowedRoles = ["member", "admin"];

    public static bool IsKnownStatus(string status) => AllowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownRole(string role) => AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static bool IsMeGet(Uri? uri, string? method)
    {
        return uri is not null
            && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "fusion-ledger.desase0175.workers.dev", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath == "/api/me"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && (uri.IsDefaultPort || uri.Port == 443);
    }

    public static AuthenticatedUser? ParseAuthenticatedUser(int statusCode, ReadOnlyMemory<byte> utf8Json)
    {
        if (statusCode == 401 || statusCode != 200 || utf8Json.Length == 0 || utf8Json.Length > MaxResponseBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                MaxDepth = 16,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("user", out var user)
                || user.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var username = ReadBoundedString(user, "username", MaxUsernameLength);
            var status = ReadBoundedString(user, "status", MaxStatusLength);
            var role = ReadBoundedString(user, "role", MaxRoleLength);
            if (username is null || status is null || role is null
                || !AllowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)
                || !AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            byte[]? avatar = null;
            if (user.TryGetProperty("avatar", out var avatarValue) && avatarValue.ValueKind == JsonValueKind.String)
            {
                avatar = ParsePngDataUri(avatarValue.GetString());
            }

            var projectManager = user.TryGetProperty("project_manager", out var pm)
                && pm.ValueKind == JsonValueKind.True;
            return new AuthenticatedUser(username, avatar, status.ToLowerInvariant(), role.ToLowerInvariant(), projectManager);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadBoundedString(JsonElement user, string property, int maxLength)
    {
        if (!user.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var result = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(result) || result.Length > maxLength ? null : result;
    }

    /// <summary>Bounded PNG data-URI decoding shared by the sign-in snapshot and bridge DTO avatars.</summary>
    public static byte[]? ParsePngDataUri(string? value)
    {
        if (value is null || !value.StartsWith(PngDataPrefix, StringComparison.Ordinal)) return null;
        var encoded = value[PngDataPrefix.Length..];
        if (encoded.Length == 0 || encoded.Length > ((MaxAvatarBytes + 2) / 3) * 4) return null;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            return bytes.Length <= MaxAvatarBytes && HasPngSignature(bytes) ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    private static bool HasPngSignature(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        return bytes.Length >= signature.Length && bytes[..signature.Length].SequenceEqual(signature);
    }
}
