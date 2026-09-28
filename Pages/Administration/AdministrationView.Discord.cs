using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MolHub.Windows;

internal sealed partial class AdministrationView
{
    private StackPanel RenderDiscord()
    {
        var data = _discord!; var body = CardBody();
        body.Children.Add(PageParts.Heading(L(data.Setup.Configured ? "Admin_DiscordConfigured" : "Admin_DiscordSetupRequired"), AutomationHeadingLevel.Level3));
        foreach (var (name, yes) in new[]
        {
            ("Admin_DiscordApplicationId", data.Setup.ApplicationId), ("Admin_DiscordPublicKey", data.Setup.PublicKey),
            ("Admin_DiscordBotToken", data.Setup.BotToken), ("Admin_DiscordSiteUrl", data.Setup.SiteUrl)
        }) body.Children.Add(PageParts.Caption(Join(yes ? "\uE73E" : "\uE711", L(name), L(yes ? "Admin_Yes" : "Admin_No"))));

        body.Children.Add(PageParts.Heading(L("Admin_DiscordDestinations"), AutomationHeadingLevel.Level3));
        if (data.Links.Count == 0) body.Children.Add(Empty("Admin_DiscordLinksEmpty"));
        foreach (var link in data.Links)
        {
            var projectName = link.ProjectName.Length == 0 ? "—" : link.ProjectName;
            var channelName = link.ChannelName.Length == 0 ? "—" : link.ChannelName;
            body.Children.Add(PageParts.Heading(Join(projectName, "→ #" + channelName, link.Deleted ? L("Admin_DeletedSuffix") : ""), AutomationHeadingLevel.Level3));
            var ids = new InlineWrapPanel { HorizontalSpacing = 6 };
            ids.Children.Add(_p.IdChip(link.GuildId, L("Admin_CopyIdFormat"), "Admin_Copied"));
            ids.Children.Add(_p.IdChip(link.ChannelId, L("Admin_CopyIdFormat"), "Admin_Copied"));
            var linkPanel = new StackPanel { Spacing = 4 }; linkPanel.Children.Add(ids);
            linkPanel.Children.Add(PageParts.Caption(Join(L(link.Enabled ? "Admin_DiscordEnabled" : "Admin_DiscordStopped"), link.Language)));
            body.Children.Add(linkPanel);
        }

        body.Children.Add(PageParts.Heading(L("Admin_DiscordDeliveries"), AutomationHeadingLevel.Level3));
        if (data.Deliveries.Count == 0) body.Children.Add(Empty("Admin_DiscordDeliveriesEmpty"));
        foreach (var delivery in data.Deliveries)
        {
            body.Children.Add(PageParts.Heading((delivery.ProjectName.Length == 0 ? "—" : delivery.ProjectName) + " → #" + (delivery.ChannelName.Length == 0 ? "—" : delivery.ChannelName), AutomationHeadingLevel.Level3));
            var deliveryKey = AdministrationModel.DeliveryStatusKey(delivery.Status);
            var deliveryLabel = deliveryKey == delivery.Status ? delivery.Status : L(deliveryKey);
            body.Children.Add(PageParts.Caption(Join(L("Admin_DiscordStatus"), deliveryLabel, L("Admin_DiscordAttempts") + ": " + delivery.Attempts, delivery.LastError)));
            body.Children.Add(_p.IdChip(delivery.CommitId, L("Admin_CopyIdFormat"), "Admin_Copied"));
        }
        body.Children.Add(_p.Secondary(L("Admin_DiscordReadOnlyNote")));
        return new StackPanel { Spacing = 16, Children = { Card("Admin_Discord", "\uE71B", body) } };
    }
}
