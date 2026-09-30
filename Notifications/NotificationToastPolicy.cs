namespace MolHub.Windows;

public enum ToastPlanKind
{
    None,
    PerItem,
    Summary
}

public sealed record ToastPlan(ToastPlanKind Kind, IReadOnlyList<NotificationItem> Items);

public enum ToastAction
{
    Open,
    List
}

public sealed record ToastTarget(ToastAction Action, string? ProjectId, string? CommitId);

/// <summary>Pure toast rules: which toasts to show and the validated activation arguments (only action, projectId and commitId).</summary>
public static class NotificationToastPolicy
{
    public const int SummaryThreshold = 4;
    public const string ActionKey = "action";
    public const string ProjectIdKey = "projectId";
    public const string CommitIdKey = "commitId";
    public const string OpenAction = "open";
    public const string ListAction = "list";

    public static ToastPlan Plan(IReadOnlyList<NotificationItem> arrived, bool windowActive, bool enabled)
    {
        if (!enabled || windowActive || arrived.Count == 0) return new ToastPlan(ToastPlanKind.None, []);
        return arrived.Count >= SummaryThreshold
            ? new ToastPlan(ToastPlanKind.Summary, arrived)
            : new ToastPlan(ToastPlanKind.PerItem, arrived);
    }

    /// <summary>Arguments of a per-item toast, built only from the item's own ids.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> BuildItemArguments(NotificationItem item)
    {
        var list = new List<KeyValuePair<string, string>>
        {
            new(ActionKey, OpenAction),
            new(ProjectIdKey, item.ProjectId)
        };
        if (item.Kind == NotificationKind.CommitPublished && item.Commit is { } commit) list.Add(new(CommitIdKey, commit.Id));
        return list;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> BuildListArguments() => [new(ActionKey, ListAction)];

    /// <summary>Null for an unknown action or an invalid id; unknown keys are ignored.</summary>
    public static ToastTarget? ParseArguments(IDictionary<string, string> args)
    {
        if (!args.TryGetValue(ActionKey, out var action)) return null;
        if (action == ListAction) return new ToastTarget(ToastAction.List, null, null);
        if (action != OpenAction) return null;

        if (!args.TryGetValue(ProjectIdKey, out var projectId) || ProfilePayloadParser.NormalizeUserId(projectId) is null) return null;
        string? commitId = null;
        if (args.TryGetValue(CommitIdKey, out var commit))
        {
            if (ProfilePayloadParser.NormalizeUserId(commit) is null) return null;
            commitId = commit;
        }
        return new ToastTarget(ToastAction.Open, projectId, commitId);
    }
}
