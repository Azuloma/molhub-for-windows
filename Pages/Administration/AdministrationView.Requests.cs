using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MolHub.Windows;

internal sealed partial class AdministrationView
{
    private StackPanel RenderRequests()
    {
        var body = CardBody(); var items = _requests.GetValueOrDefault("Requests")?.Items ?? [];
        if (items.Count == 0) body.Children.Add(Empty("Admin_RequestsEmpty"));
        foreach (var x in items)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(PageParts.Heading(Join(x.ProjectName.Length == 0 ? "—" : x.ProjectName,
                ManagementModel.KindKey(x.Kind) is { } k ? L(k) : x.Kind), AutomationHeadingLevel.Level3));
            if (x.Reason.Length > 0) row.Children.Add(new TextBlock { Text = x.Reason, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            var statusKey = ManagementModel.StatusKey(x.Status);
            row.Children.Add(PageParts.Caption(Join(x.Requester.Length == 0 ? "—" : x.Requester, Date(x.CreatedAt), statusKey is null ? x.Status : L(statusKey))));
            body.Children.Add(row);
        }
        return new StackPanel { Spacing = 16, Children = { Card("Admin_Requests", "\uE8A5", body) } };
    }
}
