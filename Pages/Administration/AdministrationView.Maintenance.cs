using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MolHub.Windows;

internal sealed partial class AdministrationView
{
    private StackPanel RenderMaintenance()
    {
        var body = CardBody(); var item = _maintenance!;
        if (_maintenanceUnavailable) body.Children.Add(_p.Secondary(L("Maintenance_Unavailable")));
        else
        {
            var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            state.Children.Add(new Ellipse
            {
                Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
                Style = PageParts.Res(item.Active ? "ProjectStatusWorkingDotStyle" : "ProjectStatusAvailableDotStyle")
            });
            state.Children.Add(new TextBlock
            {
                Text = L(item.Active ? "Maintenance_Active" : "Maintenance_Inactive"),
                Style = PageParts.Res(item.Active ? "ProjectStatusWorkingTextStyle" : "ProjectStatusAvailableTextStyle")
            });
            body.Children.Add(state);
        }
        if (item.Active && Date(item.StartedAt) is { Length: > 0 } date) body.Children.Add(PageParts.Caption(string.Format(L("Maintenance_StartedFormat"), date)));
        body.Children.Add(PageParts.Heading(L("Admin_CompletedHistory"), AutomationHeadingLevel.Level3));
        if (item.History.Count == 0) body.Children.Add(Empty("Admin_MaintenanceEmpty"));
        foreach (var announcement in item.History)
        {
            body.Children.Add(AnnouncementCard.Row(announcement, L, _language, () =>
            {
                _maintenanceOffset = _scroll.VerticalOffset;
                _announcementDetail = announcement; RenderSelectedSafely(); _scroll.ChangeView(null, 0, null, true); _navigationChanged();
            }));
        }
        return new StackPanel { Spacing = 16, Children = { Card("Admin_Maintenance", "\uE90F", body) } };
    }
}
