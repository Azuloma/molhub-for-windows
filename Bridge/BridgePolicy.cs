using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MolHub.Windows;

/// <summary>What happened to a write, as reported by the web bridge or decided by the host.</summary>
public enum BridgeOutcome
{
    /// <summary>Reads carry no outcome.</summary>
    None,
    Applied,
    /// <summary>Nothing changed: the server refused it, or the host never sent it.</summary>
    Rejected,
    /// <summary>The change may or may not have happened; refresh state and let the user decide, never resend blindly.</summary>
    Unknown
}

/// <summary>One bridge response. Status 0 means no HTTP response (network, timeout or host-side failure).</summary>
public sealed record BridgeResult(
    string RequestId,
    int Status,
    bool Ok,
    JsonElement? Data,
    JsonElement? Meta,
    string? ErrorCode,
    JsonElement? ErrorDetails,
    bool Retryable,
    BridgeOutcome Outcome)
{
    public static BridgeResult HostFailure(string requestId, string code, bool retryable, BridgeOutcome outcome) =>
        new(requestId, 0, false, null, null, code, null, retryable, outcome);
}

/// <summary>
/// Host-side rules for the MolHub WebView2 bridge (contract in the web repository's docs/WINDOWS_API.md). The page
/// performs same-origin requests with the HttpOnly session cookie; the host only exchanges
/// `{ command, requestId, payload }` messages and never sees cookies or builds URLs itself.
/// </summary>
public static partial class BridgePolicy
{
    /// <summary>
    /// Canonical document path. Cloudflare static assets answer `/webview-bridge.html` with a 307 to this
    /// extensionless path, so the host navigates here directly and accepts only this exact document.
    /// </summary>
    public const string BridgePath = "/webview-bridge";
    public static readonly Uri BridgeUri = new(new Uri(NavigationPolicy.AppUrl), BridgePath);

    /// <summary>The bridge rejects request bodies above 50,000 bytes; the host refuses them before sending.</summary>
    public const int MaxPayloadBytes = 50_000;

    /// <summary>Upper bound for a response message accepted from the bridge page.</summary>
    public const int MaxResultChars = 2_000_000;

    /// <summary>Host timeouts sit above the page's own 15 s / 30 s aborts so the page normally reports first.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(40);
    /// <summary>Deadline for the whole bridge start: environment, controller creation with retries, and navigation.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    // Mirrors the fixed operation map in public/webview-bridge.js (the duplicate `deleteMember` alias is omitted).
    private static readonly HashSet<string> ReadCommands = new(StringComparer.Ordinal)
    {
        "session", "maintenance", "dashboard", "search", "announcements", "announcement", "projects", "project",
        "history", "projectCommits", "commit", "profile", "manageProjects", "manageProject", "adminUsers",
        "adminProjects", "adminAudit", "adminRequests", "adminMaintenance", "adminDiscord", "notifications"
    };

    private static readonly HashSet<string> WriteCommands = new(StringComparer.Ordinal)
    {
        "startReservation", "cancelReservation", "publishCommit", "updateProjectSettings", "updateAvatar",
        "changePassword", "logout", "addMember", "removeMember", "releaseReservation", "requestProjectChange",
        "linkProjectDiscord", "disableProjectDiscord", "startMaintenance", "endMaintenance", "createProject",
        "deleteProject", "restoreProject", "releaseAdminReservation", "assignProjectOwner", "reviewProjectRequest",
        "setUserStatus", "issueResetCode", "adminDiscordAction"
    };

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,80}$")]
    private static partial Regex RequestIdPattern();

    public static bool IsKnownCommand(string? command) =>
        command is not null && (ReadCommands.Contains(command) || WriteCommands.Contains(command));

    public static bool IsWrite(string? command) => command is not null && WriteCommands.Contains(command);

    public static bool IsValidRequestId(string? requestId) => requestId is not null && RequestIdPattern().IsMatch(requestId);

    public static string NewRequestId() => "w" + Guid.NewGuid().ToString("N");

    /// <summary>Only the exact HTTPS bridge document on the production origin, without query or fragment.</summary>
    public static bool IsBridgeDocument(Uri? uri) =>
        uri is not null
        && uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, BridgeUri.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == BridgeUri.Port
        && string.Equals(uri.AbsolutePath, BridgePath, StringComparison.Ordinal)
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0;

    /// <summary>WebMessageReceived.Source must be the bridge document itself.</summary>
    public static bool IsTrustedSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && IsBridgeDocument(uri);

    // The page re-serializes the payload with JSON.stringify, which keeps non-ASCII text as UTF-8; measuring the default
    // escaped form (6 bytes per Japanese character) would refuse bodies the page and server accept.
    private static readonly JsonSerializerOptions WireSizeOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>UTF-8 size of a payload as the page sends it (for the 50,000-byte body limit).</summary>
    public static int PayloadBytes(JsonObject? payload)
    {
        var body = payload?.DeepClone() as JsonObject ?? new JsonObject();
        body.Remove("requestId");
        return Encoding.UTF8.GetByteCount(body.ToJsonString(WireSizeOptions));
    }

    /// <summary>Builds the JSON posted to the page, or null when the command, id or payload size is not allowed.</summary>
    public static string? BuildRequest(string command, string requestId, JsonObject? payload)
    {
        if (!IsKnownCommand(command) || !IsValidRequestId(requestId)) return null;
        var body = payload?.DeepClone() as JsonObject ?? new JsonObject();
        body.Remove("requestId");
        if (PayloadBytes(body) > MaxPayloadBytes) return null;
        return new JsonObject
        {
            ["command"] = command,
            ["requestId"] = requestId,
            ["payload"] = body
        }.ToJsonString();
    }

    /// <summary>Parses a bridge result; null for anything malformed, oversized or impossible to correlate.</summary>
    public static BridgeResult? ParseResult(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxResultChars) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("requestId", out var id) || id.ValueKind != JsonValueKind.String) return null;
            var requestId = id.GetString();
            if (!IsValidRequestId(requestId)) return null;
            if (!root.TryGetProperty("status", out var statusElement) || !statusElement.TryGetInt32(out var status)
                || status is < 0 or > 599) return null;
            if (!root.TryGetProperty("ok", out var okElement) || okElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;

            var ok = okElement.GetBoolean();
            string? code = null;
            JsonElement? details = null;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String) code = codeElement.GetString();
                if (error.TryGetProperty("details", out var detailsElement)) details = detailsElement.Clone();
            }
            if (!ok && string.IsNullOrEmpty(code)) code = "SERVER_ERROR";

            var outcome = BridgeOutcome.None;
            if (root.TryGetProperty("outcome", out var outcomeElement))
            {
                outcome = outcomeElement.ValueKind == JsonValueKind.String ? outcomeElement.GetString() switch
                {
                    "applied" => BridgeOutcome.Applied,
                    "rejected" => BridgeOutcome.Rejected,
                    _ => BridgeOutcome.Unknown
                } : BridgeOutcome.Unknown;
            }

            return new BridgeResult(
                requestId!,
                status,
                ok,
                Optional(root, "data"),
                Optional(root, "meta"),
                ok ? null : code,
                ok ? null : details,
                root.TryGetProperty("retryable", out var retryable) && retryable.ValueKind == JsonValueKind.True,
                outcome);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Optional(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            ? value.Clone()
            : null;
}
