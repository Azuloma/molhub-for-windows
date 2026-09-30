using System.Globalization;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace MolHub.Windows;

/// <summary>Thin Windows App SDK toast wrapper. Failures disable or skip a toast silently; nothing is logged.</summary>
public static class NotificationToasts
{
    private const string Group = "activity";
    private const string SummaryTag = "summary";

    /// <summary>Set by App after a successful <c>Register()</c>.</summary>
    public static bool Registered { get; set; }

    public static void Show(IReadOnlyList<NotificationItem> arrived, bool windowActive, Func<string, string> localize)
    {
        try
        {
            if (!Registered) return;
            var enabled = AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;
            var plan = NotificationToastPolicy.Plan(arrived, windowActive, enabled);
            switch (plan.Kind)
            {
                case ToastPlanKind.PerItem:
                    foreach (var item in plan.Items) ShowItem(item, localize);
                    break;
                case ToastPlanKind.Summary:
                    ShowSummary(plan.Items.Count, localize);
                    break;
            }
        }
        catch
        {
            // Toasts are optional; a failure must not affect the app.
        }
    }

    private static void ShowItem(NotificationItem item, Func<string, string> localize)
    {
        try
        {
            var builder = new AppNotificationBuilder();
            foreach (var pair in NotificationToastPolicy.BuildItemArguments(item)) builder.AddArgument(pair.Key, pair.Value);
            builder.AddText(NotificationText.Sentence(item, localize));
            var commitLine = NotificationText.CommitLine(item, localize);
            if (commitLine is not null) builder.AddText(commitLine);
            builder.SetTag(item.Id.ToString(CultureInfo.InvariantCulture));
            builder.SetGroup(Group);
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch
        {
            // Skip this toast.
        }
    }

    private static void ShowSummary(int count, Func<string, string> localize)
    {
        try
        {
            var builder = new AppNotificationBuilder();
            foreach (var pair in NotificationToastPolicy.BuildListArguments()) builder.AddArgument(pair.Key, pair.Value);
            builder.AddText(string.Format(CultureInfo.CurrentCulture, localize("Notifications_ToastSummaryFormat"), count));
            builder.SetTag(SummaryTag);
            builder.SetGroup(Group);
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch
        {
            // Skip this toast.
        }
    }

    /// <summary>Removes every toast this app showed (sign-out). Fire and forget.</summary>
    public static void RemoveAllAsync()
    {
        if (!Registered) return;
        try
        {
            var task = AppNotificationManager.Default.RemoveAllAsync().AsTask();
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch
        {
            // Nothing to remove or the manager is unavailable.
        }
    }
}
