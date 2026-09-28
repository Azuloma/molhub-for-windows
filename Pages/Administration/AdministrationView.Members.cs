using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace FusionLedger.Windows;

internal sealed partial class AdministrationView
{
    private StackPanel RenderMembers()
    {
        var body = CardBody(); var items = _members?.Items ?? [];
        if (items.Count == 0) body.Children.Add(Empty("Admin_MembersEmpty"));
        var signedIn = _currentUser().Username;
        foreach (var member in items)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(PageParts.Avatar(member.Username, member.Avatar, 40));
            var info = new StackPanel { Spacing = 3 };
            info.Children.Add(PageParts.Heading(member.Username, AutomationHeadingLevel.Level3));
            var roleKey = "Role_" + member.Role;
            var roleLabel = new[] { "member", "admin" }.Contains(member.Role, StringComparer.Ordinal) ? L(roleKey) : member.Role;
            var statusKey = StatusKey(member.Status);
            info.Children.Add(PageParts.Caption(roleLabel)); info.Children.Add(MemberStatus(member.Status, statusKey));
            Grid.SetColumn(info, 1); row.Children.Add(info);
            if (MemberActionsPanel(member, AdminUserWriteModel.Actions(member, signedIn), StatusLabel(member.Status)) is { } actions)
            {
                Grid.SetColumn(actions, 2); row.Children.Add(actions);
            }
            body.Children.Add(row);
        }
        return new StackPanel { Spacing = 16, Children = { Card("Admin_Members", "\uE716", body, items.Count.ToString()) } };
    }

    private static string StatusKey(string status) => status == "pending" ? "Admin_Status_pending" : ManagementModel.StatusKey(status) ?? "";

    private string StatusLabel(string status) => StatusKey(status) is { Length: > 0 } key ? L(key) : status;

    private FrameworkElement MemberStatus(string status, string key)
    {
        var text = new TextBlock
        {
            Text = key.Length == 0 ? status : L(key),
            Style = status == "approved" ? PageParts.Res("ProjectStatusAvailableTextStyle")
                : status == "pending" ? PageParts.Res("ProjectStatusWorkingTextStyle")
                : PageParts.Res("DashboardCaptionTextStyle")
        };
        var pill = new Border { Style = PageParts.Res("ProjectPrivateBadgeStyle"), Child = text };
        AutomationProperties.SetName(pill, text.Text);
        return pill;
    }
}
