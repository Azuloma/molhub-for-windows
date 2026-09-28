using System.Text.Json;
using FusionLedger.Windows;

internal static class AdministrationPolicyTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Administration: " + message);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static void Run(string sourceRoot)
    {
        var view = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationView.cs"));
        var model = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationModel.cs"));
        var administrationCode = string.Join("\r\n", Directory.GetFiles(Path.Combine(sourceRoot, "Pages", "Administration"), "*.cs").Select(File.ReadAllText));
        var shell = File.ReadAllText(Path.Combine(sourceRoot, "Shell", "MainWindow.xaml.cs"));
        Check(view.Contains("PageParts.CenteredPage(root, 1280)", StringComparison.Ordinal), "page must use the centered 1280-DIP column.");
        Check(view.Contains("result = command is null ? null : await _request(command, payload);", StringComparison.Ordinal)
            && view.Contains("if (result is null)", StringComparison.Ordinal)
            && view.Contains("RenderSelectedSafely();", StringComparison.Ordinal)
            && view.Contains("ClearSectionData(section);", StringComparison.Ordinal),
            "Members loads must turn request, parse, and render failures into the existing localized error state.");
        var directRenders = administrationCode.Split("RenderSelected();").Length - 1;
        var safeStart = view.IndexOf("private void RenderSelectedSafely()", StringComparison.Ordinal);
        var renderStart = view.IndexOf("private void RenderSelected()", StringComparison.Ordinal);
        var safeBlock = safeStart >= 0 && renderStart > safeStart ? view[safeStart..renderStart] : "";
        var directRender = safeBlock.IndexOf("RenderSelected();", StringComparison.Ordinal);
        var renderCatch = safeBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Check(directRenders == 1 && directRender >= 0 && renderCatch > directRender
            && safeBlock.IndexOf("_loadFailed = true;", renderCatch, StringComparison.Ordinal) > renderCatch
            && safeBlock.IndexOf("RenderErrorMessage();", renderCatch, StringComparison.Ordinal) > renderCatch,
            "section rendering must go through RenderSelectedSafely so a render exception cannot escape a selection handler or leave the loading indicator.");
        var logStart = view.IndexOf("private static void LogFailure(", StringComparison.Ordinal);
        var logLine = logStart >= 0 ? view[logStart..view.IndexOf(';', logStart)] : "";
        Check(logLine.Contains("ex.GetType().Name", StringComparison.Ordinal) && logLine.Contains("ex.HResult", StringComparison.Ordinal)
            && !logLine.Contains("ex.Message", StringComparison.Ordinal) && !logLine.Contains("ex}", StringComparison.Ordinal)
            && !logLine.Contains("ToString()", StringComparison.Ordinal)
            && view.Split("LogFailure(\"").Length - 1 == 3, "request, parse, and render failures must log only exception type and HRESULT.");
        Check(AdministrationModel.Sections.SequenceEqual(["Requests", "Projects", "Members", "Reservations", "Discord", "Maintenance", "Audit"])
            && AdministrationModel.DefaultSection == "Requests", "section order/default must match the web.");
        Check(AdministrationModel.Command("Requests") == "adminRequests" && AdministrationModel.Command("Reservations") == "adminProjects"
            && AdministrationModel.Command("Discord") == "adminDiscord" && AdministrationModel.Command("Maintenance") == "adminMaintenance"
            && AdministrationModel.Command("Audit") == "adminAudit" && AdministrationModel.Command("unknown") is null,
            "each section must map only to its specified read command.");
        var listPayload = AdministrationModel.ListPayload("Requests", 75);
        Check(listPayload.Count == 2 && listPayload["limit"]!.GetValue<int>() == 50 && listPayload["offset"]!.GetValue<int>() == 75
            && AdministrationModel.ListPayload("Projects", 0)["limit"]!.GetValue<int>() == 100
            && AdministrationModel.ListPayload("Members", 0)["limit"]!.GetValue<int>() == 50,
            "paged administration payloads must contain only the contract limit/offset keys and page sizes.");
        var allowed = new[] { "adminRequests", "adminProjects", "adminUsers", "adminDiscord", "adminMaintenance", "adminAudit" };
        foreach (var command in allowed) Check(model.Contains("\"" + command + "\"", StringComparison.Ordinal), $"missing read command {command}.");
        var commands = System.Text.RegularExpressions.Regex.Matches(administrationCode, "\\\"(admin[A-Z][A-Za-z0-9]*)\\\"")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Check(commands.SetEquals(allowed), "Administration code may reference only the six read commands.");
        foreach (var write in new[] { "adminApprove", "adminReject", "adminSuspend", "adminCreateProject", "adminDeleteProject", "adminRestoreProject", "adminDiscordAction", "adminMaintenanceStart", "adminMaintenanceEnd", "adminRetryDelivery", "adminResetCode", "adminCreateAdmin" })
            Check(!administrationCode.Contains(write, StringComparison.Ordinal), $"write command {write} must not occur in Administration.");
        Check(model.Contains("ValueKind == JsonValueKind.Number", StringComparison.Ordinal) && model.Contains("ProjectsModel.Paging", StringComparison.Ordinal), "paging must safely parse numeric meta and null nextOffset.");
        Check(shell.Contains("NativePage.Administration) return CreateAdministrationPage()", StringComparison.Ordinal)
            && shell.Contains("_administration?.UserChanged()", StringComparison.Ordinal) && shell.Contains("_administration?.MarkStale()", StringComparison.Ordinal), "administration cache hooks and route are required.");
        Check(NativePageCatalog.IsNested(NativePage.Administration) && NativePageCatalog.CanAdminister(new AuthenticatedUser("Admin", null, "approved", "admin", false))
            && !NativePageCatalog.CanAdminister(new AuthenticatedUser("Member", null, "approved", "member", false)), "Administration remains nested and admin-only.");

        var requests = AdministrationModel.ParseRequests(Json("[{\"id\":\"r1\",\"projectName\":null,\"kind\":\"future_kind\",\"status\":\"future_status\",\"reason\":null,\"requester\":null}]"), Json("{\"total\":1,\"nextOffset\":null}"));
        Check(requests is { Items.Count: 1, NextOffset: null } && requests.Items[0].ProjectName == "" && requests.Items[0].Requester == ""
            && ManagementModel.KindKey(requests.Items[0].Kind) is null && ManagementModel.StatusKey(requests.Items[0].Status) is null, "request null and unknown values must be preserved safely.");
        var nextPage = AdministrationModel.ParseRequests(Json("[]"), Json("{\"total\":100,\"nextOffset\":50}"));
        Check(nextPage is { Total: 100, NextOffset: 50 }, "positive nextOffset must be preserved by the real parser.");
        var projects = AdministrationModel.ParseProjects(Json("[{\"id\":\"p1\",\"name\":\"Active\",\"deletedAt\":null,\"reservation\":{\"username\":\"owner\",\"startedAt\":\"2026-01-01T00:00:00Z\"}},{\"id\":\"p2\",\"name\":\"Deleted\",\"deletedAt\":\"2026-01-02T00:00:00Z\",\"reservation\":{\"username\":\"ignored\"}},{\"id\":\"p3\",\"name\":\"Reserved without username\",\"deletedAt\":null,\"reservation\":{\"username\":null}}]"), Json("{\"total\":3,\"nextOffset\":null}"));
        var reservations = AdministrationModel.Reservations(projects);
        Check(projects is { Items.Count: 3, NextOffset: null } && AdministrationModel.ActiveProjects(projects).Count == 2
            && AdministrationModel.DeletedProjects(projects).Count == 1 && reservations.Count == 2
            && reservations.Any(item => item.Holder == "owner")
            && reservations.Any(item => item.ReservationPresent && item.Holder is null), "deleted projects and reservation presence must be split independently from nullable holder names.");
        var members = AdministrationModel.ParseMembers(Json("[{\"id\":\"u1\",\"username\":\"User\",\"status\":null,\"role\":null,\"avatar\":null},{\"id\":\"u2\",\"username\":\"With avatar\",\"status\":\"approved\",\"role\":\"admin\",\"avatar\":\"data:image/png;base64,not-valid\"}]"), Json("{\"total\":2,\"nextOffset\":50}"));
        Check(members is { Items.Count: 2, Total: 2, NextOffset: 50 } && members.Items[0].Status == "" && members.Items[0].Role == "member"
            && members.Items[1].Avatar is null && members.Items[1].Status == "approved" && members.Items[1].Role == "admin", "member DTO must preserve role/status/paging and fall back safely from an invalid avatar.");
        var discord = AdministrationModel.ParseDiscord(Json("{\"setup\":{\"configured\":true,\"checks\":{\"applicationId\":true}},\"links\":[{\"guildId\":\"g\",\"channelId\":\"c\",\"projectDeleted\":true,\"projectName\":null,\"channelName\":null,\"locale\":null}],\"deliveries\":[{\"status\":\"future\",\"commitId\":\"commit-id\",\"attempts\":null,\"projectName\":null,\"channelName\":null,\"lastError\":null}]}"));
        Check(discord is { Setup.Configured: true, Setup.ApplicationId: true, Links.Count: 1, Deliveries.Count: 1 }
            && discord.Links[0] is { Deleted: true, ProjectName: "", ChannelName: "", Language: "" }
            && discord.Deliveries[0] is { Attempts: 0, ProjectName: "", ChannelName: "", LastError: "" }
            && AdministrationModel.DeliveryStatusKey("future") == "future", "Discord DTOs and unknown delivery labels must parse safely.");
        var maintenance = AdministrationModel.ParseMaintenance(Json("{\"active\":true,\"state\":null,\"history\":[{\"id\":\"a1\",\"title\":\"Done\",\"endedAt\":\"2026-01-02T00:00:00Z\"}]}"));
        Check(maintenance is { Active: true, History.Count: 1, StartedAt: null } && maintenance.History[0].DisplayDate is not null, "maintenance state nulls and announcement DTOs must parse.");
        var audit = AdministrationModel.ParseAudit(Json("[{\"action\":\"future_action\",\"actor\":null,\"target\":null}]"), Json("{\"nextOffset\":null}"));
        Check(audit is { Items.Count: 1, NextOffset: null } && audit.Items[0].Actor == "" && AdministrationModel.ActionLabelKey("future_action") == "future_action"
            && AdministrationModel.ActionLabelKey("approved") == "Admin_Audit_approved", "audit labels and nullable fields must be safe.");
        var action = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Common", "AnnouncementCard.cs"));
        var maintenanceView = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Maintenance", "MaintenanceView.cs"));
        var maintenanceModel = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Maintenance", "MaintenanceModel.cs"));
        var maintenanceAdminView = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationView.Maintenance.cs"));
        var projectsView = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationView.Projects.cs"));
        var reservationsView = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Administration", "AdministrationView.Reservations.cs"));
        Check(maintenanceAdminView.Contains("AnnouncementCard.Row", StringComparison.Ordinal) && maintenanceView.Contains("AnnouncementCard.Row", StringComparison.Ordinal)
            && maintenanceView.Contains("AnnouncementCard.Detail", StringComparison.Ordinal)
            && action.Contains("item.DisplayDate", StringComparison.Ordinal)
            && maintenanceModel.Contains("DisplayDate => EndedAt ?? CreatedAt", StringComparison.Ordinal)
            && maintenanceModel.Contains("ListPreviewLength = 420", StringComparison.Ordinal)
            && action.Contains("MaintenanceModel.Preview(item.Details, MaintenanceModel.ListPreviewLength)", StringComparison.Ordinal)
            && action.Contains("LinkButton(localize(\"Announcements_Details\"), open", StringComparison.Ordinal)
            && action.Contains("AutomationProperties.SetName(details, string.Format(localize(\"Announcements_DetailsForFormat\"), item.Title))", StringComparison.Ordinal),
            "announcement extraction must retain endedAt/createdAt date fallback, 420-character preview, working Details action and accessible name.");
        var loadStart = view.IndexOf("if (!result.Ok)", StringComparison.Ordinal);
        var malformedStart = view.IndexOf("if (!valid)", loadStart, StringComparison.Ordinal);
        var failureBlock = loadStart >= 0 && malformedStart > loadStart ? view[loadStart..malformedStart] : "";
        Check(failureBlock.Contains("ShowErrorFor(result)", StringComparison.Ordinal)
            && failureBlock.Contains("if (section == _selected) RenderSelectedSafely();", StringComparison.Ordinal)
            && view.Contains("private bool _loadFailed;", StringComparison.Ordinal)
            && view.Contains("_loadFailed = true;", StringComparison.Ordinal)
            && view.Contains("if (_loadFailed) RenderErrorMessage();", StringComparison.Ordinal)
            && view.Contains("_content.Content = _p.Secondary(_status.Message);", StringComparison.Ordinal),
            "first-load failures must settle on a localized section message while later failures continue rendering cached data.");
        var selectionStart = view.IndexOf("_sections.SelectionChanged +=", StringComparison.Ordinal);
        var selectionEnd = view.IndexOf("_sections.SelectedIndex", selectionStart, StringComparison.Ordinal);
        var selectionBlock = selectionStart >= 0 && selectionEnd > selectionStart ? view[selectionStart..selectionEnd] : "";
        Check(selectionBlock.Contains("_status.IsOpen = false;", StringComparison.Ordinal)
            && selectionBlock.Contains("_status.ActionButton = null;", StringComparison.Ordinal)
            && selectionBlock.IndexOf("_status.IsOpen = false;", StringComparison.Ordinal) < selectionBlock.IndexOf("RenderSelectedSafely();", StringComparison.Ordinal)
            && selectionBlock.Contains("_activeRequestGeneration++;", StringComparison.Ordinal)
            && view.Contains("_activeRequestGeneration != activeGeneration", StringComparison.Ordinal)
            && view.Contains("_selected != section", StringComparison.Ordinal),
            "switching sections must clear the prior InfoBar before rendering and stale replies must remain ignored.");
        Check(projectsView.Contains("project.ReservationPresent ?", StringComparison.Ordinal)
            && projectsView.Contains("project.Holder ?? \"\u2014\"", StringComparison.Ordinal)
            && reservationsView.Contains("project.Holder ?? \"\u2014\"", StringComparison.Ordinal),
            "reservations with a null holder must remain visible with the em dash fallback.");
        var localizedAdminKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var locale in new[] { "en-US", "ja-JP" })
        {
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
            var keys = document.Root!.Elements("data").Where(x => ((string?)x.Attribute("name"))?.StartsWith("Admin_", StringComparison.Ordinal) == true)
                .ToDictionary(x => (string)x.Attribute("name")!, x => (string?)x.Element("value") ?? "", StringComparer.Ordinal);
            Check(keys.Values.All(value => !string.IsNullOrWhiteSpace(value)), $"{locale} has an empty Administration value.");
            localizedAdminKeys[locale] = keys.Keys.ToHashSet(StringComparer.Ordinal);
        }
        Check(localizedAdminKeys["en-US"].SetEquals(localizedAdminKeys["ja-JP"]), "both locales must have the same complete Administration key set.");
        var requiredKeys = AdministrationModel.Sections.Select(section => "Admin_Section_" + section)
            .Concat(new[] { "approved", "rejected", "suspended", "force_release", "reset_code_issued", "password_reset", "project_created", "admin_created", "admin_recovered", "project_deleted", "project_restored", "auth_smoke_test", "discord_linked", "discord_disabled", "discord_retry", "discord_commands_registered", "discord_commands_migrated" }.Select(action => "Admin_Audit_" + action))
            .Concat(new[] { "pending", "sending", "sent", "failed", "cancelled" }.Select(status => "Admin_Discord_Status_" + status));
        foreach (var key in requiredKeys) Check(localizedAdminKeys["en-US"].Contains(key), $"both locales must define {key}.");
        Check(File.ReadAllText(Path.Combine(sourceRoot, "FusionLedger.Windows.csproj")).Contains("<Version>0.14.6</Version>", StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(sourceRoot, "Package.appxmanifest")).Contains("Version=\"0.14.6.0\"", StringComparison.Ordinal), "release version must be v0.14.6.");
    }
}
