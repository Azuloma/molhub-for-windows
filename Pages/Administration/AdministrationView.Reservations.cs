using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace FusionLedger.Windows;

internal sealed partial class AdministrationView
{
    private StackPanel RenderReservations()
    {
        var body = CardBody(); var items = AdministrationModel.Reservations(_projects);
        if (items.Count == 0) body.Children.Add(Empty("Admin_ReservationsEmpty"));
        foreach (var project in items)
        {
            body.Children.Add(PageParts.Heading(project.Name, AutomationHeadingLevel.Level3));
            body.Children.Add(PageParts.Caption(Join(project.Holder ?? "—", Date(project.ReservationStartedAt))));
        }
        return new StackPanel { Spacing = 16, Children = { Card("Admin_Reservations", "\uE823", body) } };
    }
}
