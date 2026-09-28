using System.Text.Json;
using System.Text.Json.Nodes;

namespace MolHub.Windows;

/// <summary>
/// One write at a time for the whole app (the Projects and Commit history pages share it): a second write is refused
/// with an explanation while another write and its refresh are still running.
/// </summary>
public sealed class WriteGate
{
    public bool InFlight { get; private set; }

    public bool TryEnter()
    {
        if (InFlight) return false;
        InFlight = true;
        return true;
    }

    /// <summary>Raised after every <see cref="Exit"/>, once the gate is free again (the sign-out after a password change waits for it).</summary>
    public event Action? Released;

    public void Exit()
    {
        InFlight = false;
        Released?.Invoke();
    }
}

/// <summary>What the user typed into the publish form.</summary>
public sealed record PublishDraft(string Title, string Version, string Changes, string Url);

[Flags]
public enum PublishFieldError
{
    None = 0,
    TitleMissing = 1,
    TitleTooLong = 2,
    VersionInvalid = 4,
    ChangesMissing = 8,
    ChangesTooLong = 16,
    UrlMissing = 32,
    UrlInvalid = 64,
    /// <summary>Within every field limit, but the whole request is over the 50,000-byte body limit.</summary>
    TooLarge = 128
}

public enum WriteOutcomeKind
{
    /// <summary>The server applied the change.</summary>
    Applied,
    /// <summary>The server answered with an error; nothing changed.</summary>
    Rejected,
    /// <summary>The request never left the app (bridge not connected); nothing changed.</summary>
    NotSent,
    /// <summary>No answer or an unusable one: the change may or may not have happened. Refresh and let the user decide.</summary>
    Unknown
}

/// <summary>A write result for the UI: what happened, and the resource keys that explain it.</summary>
public sealed record WriteOutcome(WriteOutcomeKind Kind, string TitleKey, string MessageKey, bool SessionEnded = false);

/// <summary>
/// Pure rules for the first native writes: start work (`startReservation`), cancel work (`cancelReservation`) and
/// publish a version (`publishCommit`). The database guards are the authority; these rules only shape requests with
/// the server's bounds and explain its answers. A write whose outcome is unknown is never resent automatically.
/// </summary>
public static class WorkModel
{
    public const int MaxTitleLength = 150;
    public const int MaxVersionLength = 40;
    public const int MaxChangesLength = 10_000;
    public const int MaxUrlLength = 2048;

    /// <summary>Start work from the head the user is looking at (the server rejects it as stale if the head moved).</summary>
    public static JsonObject? StartPayload(string projectId, string? head) =>
        ProjectsModel.IsValidId(projectId)
            ? new JsonObject { ["projectId"] = projectId, ["base"] = ProjectsModel.IsValidId(head) ? head : null }
            : null;

    public static JsonObject? CancelPayload(string projectId) =>
        ProjectsModel.IsValidId(projectId) ? new JsonObject { ["projectId"] = projectId } : null;

    /// <summary>Checks the form with the server's limits so a request is only sent when it can be accepted.</summary>
    public static PublishFieldError Validate(PublishDraft draft)
    {
        var errors = PublishFieldError.None;
        var title = draft.Title.Trim();
        if (title.Length == 0) errors |= PublishFieldError.TitleMissing;
        else if (title.Length > MaxTitleLength) errors |= PublishFieldError.TitleTooLong;
        var version = draft.Version.Trim();
        if (version.Length > MaxVersionLength || version.Contains('<') || version.Contains('>')) errors |= PublishFieldError.VersionInvalid;
        var changes = draft.Changes.Trim();
        if (changes.Length == 0) errors |= PublishFieldError.ChangesMissing;
        else if (changes.Length > MaxChangesLength) errors |= PublishFieldError.ChangesTooLong;
        var url = draft.Url.Trim();
        if (url.Length == 0) errors |= PublishFieldError.UrlMissing;
        else if (url.Length > MaxUrlLength || ProjectsModel.SafeShareUri(url) is null) errors |= PublishFieldError.UrlInvalid;
        // Long non-ASCII text can pass the character limits and still exceed the request body limit (ids at their maximum length;
        // characters outside the BMP are measured as escapes, so the check errs on the safe side).
        if (errors == PublishFieldError.None && BridgePolicy.PayloadBytes(Fields(new string('x', 100), new string('x', 100), draft)) > BridgePolicy.MaxPayloadBytes) errors |= PublishFieldError.TooLarge;
        return errors;
    }

    private static readonly PublishFieldError[] FieldGroups =
    {
        PublishFieldError.TitleMissing | PublishFieldError.TitleTooLong,
        PublishFieldError.VersionInvalid,
        // The size error is shown under Changes, where most of the text is.
        PublishFieldError.ChangesMissing | PublishFieldError.ChangesTooLong | PublishFieldError.TooLarge,
        PublishFieldError.UrlMissing | PublishFieldError.UrlInvalid
    };

    /// <summary>
    /// The errors to keep showing while the user edits after a failed publish: a flagged field shows its current error and
    /// clears once it is valid; a field that was not flagged gets no new error until the next Publish.
    /// </summary>
    public static PublishFieldError StillShown(PublishFieldError shown, PublishFieldError current)
    {
        var result = PublishFieldError.None;
        foreach (var group in FieldGroups)
        {
            if ((shown & group) != PublishFieldError.None) result |= current & group;
        }
        return result;
    }

    /// <summary>The publish request; `base` must be the caller's reservation base (which the server also checks against the head).</summary>
    public static JsonObject? PublishPayload(string projectId, string? reservationBase, PublishDraft draft)
    {
        if (!ProjectsModel.IsValidId(projectId) || Validate(draft) != PublishFieldError.None) return null;
        return Fields(projectId, ProjectsModel.IsValidId(reservationBase) ? reservationBase : null, draft);
    }

    private static JsonObject Fields(string projectId, string? reservationBase, PublishDraft draft) => new()
    {
        ["projectId"] = projectId,
        ["base"] = reservationBase,
        ["title"] = draft.Title.Trim(),
        ["changes"] = draft.Changes.Trim(),
        ["url"] = draft.Url.Trim(),
        ["version"] = draft.Version.Trim()
    };

    /// <summary>
    /// Explains a write result. `applied` is success; a JSON error is a definite rejection with a specific reason;
    /// a request that was never sent changed nothing; everything else (timeout, lost bridge, 5xx, non-JSON) is unknown.
    /// </summary>
    public static WriteOutcome Explain(BridgeResult result)
    {
        if (result.Ok && result.Outcome == BridgeOutcome.Applied) return new(WriteOutcomeKind.Applied, string.Empty, string.Empty);
        if (result.Outcome == BridgeOutcome.Rejected && result.Status == 0)
        {
            return result.ErrorCode == "TOO_LARGE"
                ? new(WriteOutcomeKind.NotSent, "Work_RejectedTitle", "Work_ErrorTooLarge")
                : new(WriteOutcomeKind.NotSent, "Dashboard_ErrorConnectionTitle", "Work_ErrorNotSent");
        }
        if (result.Outcome != BridgeOutcome.Rejected) return new(WriteOutcomeKind.Unknown, "Work_UnknownTitle", "Work_Unknown");

        var messageKey = result switch
        {
            { Status: 401 } or { ErrorCode: "UNAUTHORIZED" } => "Work_ErrorSession",
            { ErrorCode: "RESERVATION_EXISTS" } => ReservedByYou(result) ? "Work_ErrorReservedByYou" : "Work_ErrorReservedByOther",
            { ErrorCode: "RESERVATION_MISSING" } => "Work_ErrorReservationMissing",
            { ErrorCode: "NOT_RESERVATION_OWNER" } => "Work_ErrorNotReservationOwner",
            { ErrorCode: "NOT_OWNER" } => "Work_ErrorNotOwner",
            { ErrorCode: "STALE_VERSION" } => "Work_ErrorStale",
            { ErrorCode: "VERSION_CONFLICT" } => "Work_ErrorVersionConflict",
            { ErrorCode: "CONFLICT" } => "Work_ErrorConflict",
            { ErrorCode: "INVALID_URL" } => "Work_ErrorInvalidUrl",
            { ErrorCode: "INVALID_INPUT" } => "Work_ErrorInvalidInput",
            { ErrorCode: "PROJECT_DELETED" } => "Work_ErrorProjectDeleted",
            { ErrorCode: "PROJECT_FORBIDDEN" or "FORBIDDEN" or "NOT_FOUND" } or { Status: 404 } => "Work_ErrorNoAccess",
            { ErrorCode: "NOT_APPROVED" } => "Announcements_ErrorNotApproved",
            { ErrorCode: "MAINTENANCE" or "MAINTENANCE_SETUP_REQUIRED" } or { Status: 503 } => "Dashboard_ErrorMaintenance",
            { ErrorCode: "RATE_LIMIT" } or { Status: 429 } => "Dashboard_ErrorRateLimit",
            _ => "Work_ErrorRejected"
        };
        return new(WriteOutcomeKind.Rejected, "Work_RejectedTitle", messageKey, messageKey == "Work_ErrorSession");
    }

    /// <summary>`details.reservedByYou` from RESERVATION_EXISTS (only `true` counts).</summary>
    public static bool ReservedByYou(BridgeResult result) =>
        result.ErrorDetails is { ValueKind: JsonValueKind.Object } details
        && details.TryGetProperty("reservedByYou", out var mine) && mine.ValueKind == JsonValueKind.True;

    /// <summary>
    /// After an unknown publish, the refreshed project tells which case is likely: publishing ends the reservation in
    /// the same database write, so a reservation that is still the user's means nothing was published.
    /// </summary>
    public static bool StillReservedBy(ProjectSummary? project, string username) =>
        project is not null && ProjectsModel.StateFor(project.Reservation, username) == ReservationState.Yours;
}
