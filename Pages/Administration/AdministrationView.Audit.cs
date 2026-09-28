using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MolHub.Windows;

internal sealed partial class AdministrationView
{
    private StackPanel RenderAudit()
    {
        var body = CardBody(); var items = _audit?.Items ?? [];
        if (items.Count == 0) body.Children.Add(Empty("Admin_AuditEmpty"));
        foreach (var item in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0), Style = PageParts.Res("ProjectStatusWorkingDotStyle") });
            var labelKey = AdministrationModel.ActionLabelKey(item.Action);
            var label = labelKey == item.Action ? item.Action : L(labelKey);
            var text = new StackPanel { Spacing = 3 }; text.Children.Add(PageParts.Heading(label, AutomationHeadingLevel.Level3));
            text.Children.Add(PageParts.Caption(Join(item.Actor.Length == 0 ? "—" : item.Actor, Date(item.CreatedAt))));
            text.Children.Add(Mono(item.Target)); row.Children.Add(text); body.Children.Add(row);
        }
        return new StackPanel { Spacing = 16, Children = { Card("Admin_Audit", "\uE81C", body) } };
    }
}
