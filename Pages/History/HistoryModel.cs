using System.Text.Json;
using System.Text.Json.Nodes;

namespace MolHub.Windows;

/// <summary>Counts over the commits matching the current filters (`summary.total/projects/versions`).</summary>
public sealed record HistorySummary(int Total, int Projects, int Versions);

/// <summary>One page of the personal history. `Summary` is null when the server sent none; no counts are invented.</summary>
public sealed record HistoryPage(IReadOnlyList<ProjectCommit> Commits, int Total, int? NextOffset, HistorySummary? Summary);

public sealed record ProjectOption(string Id, string Name);

/// <summary>
/// Pure rules for the native "Your commit history" page (`history` bridge read). The server always limits the list to
/// the signed-in author; this model only builds the filters and parses the bounded response.
/// </summary>
public static class HistoryModel
{
    /// <summary>The project filter lists the first page of accessible projects, like the web filter.</summary>
    public const int ProjectOptionLimit = 100;

    public static JsonObject HistoryPayload(CommitFilter filter, int offset)
    {
        var payload = new JsonObject();
        if (ProjectsModel.IsValidId(filter.Project)) payload["project"] = filter.Project;
        ProjectsModel.AddCommitFilter(payload, filter, offset);
        return payload;
    }

    public static JsonObject ProjectOptionsPayload() => new() { ["limit"] = ProjectOptionLimit, ["offset"] = 0 };

    /// <summary>Parses `{ items, summary }` with the paging `meta`; null when the shape is wrong.</summary>
    public static HistoryPage? ParseHistory(JsonElement data, JsonElement? meta)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        var commits = ProjectsModel.ParseCommits(items);
        var (total, next) = ProjectsModel.Paging(meta, commits.Count);
        // All three counts must be server numbers; a partial or malformed summary is shown as none, never as zeros.
        HistorySummary? summary = null;
        if (data.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.Object
            && Number(s, "total") is { } totalCount && Number(s, "projects") is { } projectCount && Number(s, "versions") is { } versionCount)
        {
            summary = new HistorySummary(totalCount, projectCount, versionCount);
        }
        return new HistoryPage(commits, total, next, summary);
    }

    // TryGetInt32 throws on non-numbers, so check the kind first.
    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : null;

    /// <summary>Project filter entries in server order (valid ids only, no duplicates).</summary>
    public static IReadOnlyList<ProjectOption> ProjectOptions(ProjectListPage? page) =>
        page is null ? [] : page.Projects.DistinctBy(project => project.Id).Select(project => new ProjectOption(project.Id, project.Name)).ToList();
}
