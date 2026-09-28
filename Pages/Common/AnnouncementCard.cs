using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace MolHub.Windows;

/// <summary>Announcement card rendering shared by Server maintenance and Administration.</summary>
internal static class AnnouncementCard
{
    public static FrameworkElement Row(Announcement item, Func<string, string> localize, string language, Action open)
    {
        var p = new PageParts(localize);
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(20, 16, 20, 12) };
        panel.Children.Add(PageParts.Heading(item.Title, AutomationHeadingLevel.Level3));
        panel.Children.Add(Meta(item, p, language));
        if (item.Details.Length > 0) panel.Children.Add(new TextBlock { Text = MaintenanceModel.Preview(item.Details, MaintenanceModel.ListPreviewLength), TextWrapping = TextWrapping.Wrap });
        var details = p.LinkButton(localize("Announcements_Details"), open, new Thickness(-12, 0, 0, 0));
        AutomationProperties.SetName(details, string.Format(localize("Announcements_DetailsForFormat"), item.Title));
        panel.Children.Add(details);
        return new Border { Style = PageParts.Res("DashboardCardStyle"), Child = panel };
    }

    public static FrameworkElement Detail(Announcement item, Func<string, string> localize, string language)
    {
        var p = new PageParts(localize);
        var card = new StackPanel { Spacing = 10, Padding = new Thickness(20, 16, 20, 20) };
        card.Children.Add(PageParts.Heading(item.Title, AutomationHeadingLevel.Level2, "SubtitleTextBlockStyle"));
        card.Children.Add(Meta(item, p, language));
        if (item.Details.Length > 0) card.Children.Add(new TextBlock { Text = item.Details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        return new Border { Style = PageParts.Res("DashboardCardStyle"), Child = card };
    }

    private static FrameworkElement Meta(Announcement item, PageParts p, string language)
    {
        var meta = new InlineWrapPanel { HorizontalSpacing = 8 };
        if (item.Version.Length > 0) meta.Children.Add(PageParts.VersionBadge(item.Version));
        var date = DashboardModel.FormatDate(item.DisplayDate, language);
        if (date.Length > 0) meta.Children.Add(p.Secondary(date, wrap: false));
        return meta;
    }
}
