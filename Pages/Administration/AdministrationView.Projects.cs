using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace FusionLedger.Windows;

internal sealed partial class AdministrationView
{
    private StackPanel RenderProjects()
    {
        var active = AdministrationModel.ActiveProjects(_projects); var deleted = AdministrationModel.DeletedProjects(_projects);
        var body = CardBody();
        if (active.Count == 0) body.Children.Add(Empty("Admin_ProjectsEmpty"));
        foreach (var project in active) body.Children.Add(ProjectRow(project, false));
        if (deleted.Count > 0)
        {
            var expander = new Expander { Header = string.Format(L("Admin_DeletedProjectsFormat"), deleted.Count) };
            var rows = new StackPanel { Spacing = 8 };
            foreach (var project in deleted) rows.Children.Add(ProjectRow(project, true));
            expander.Content = rows; body.Children.Add(expander);
        }
        return new StackPanel { Spacing = 16, Children = { Card("Admin_Projects", "\uE8B7", body, active.Count.ToString()) } };
    }

    private FrameworkElement ProjectRow(AdminProject project, bool deleted)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(PageParts.Heading(project.Name, AutomationHeadingLevel.Level3));
        row.Children.Add(_p.Secondary(deleted ? Date(project.DeletedAt)
            : project.ReservationPresent ? Join(L("Projects_Working"), project.Holder ?? "—") : L("Projects_Available")));
        return row;
    }
}
