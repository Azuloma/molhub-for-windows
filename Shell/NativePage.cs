namespace FusionLedger.Windows;

public enum NativePage
{
    Dashboard,
    Projects,
    CommitHistory,
    AwaitingApproval,
    ServerMaintenance,
    AppSettings,
    VersionInfo,
    ProfileSettings,
    Administration
}

public static class NativePageCatalog
{
    public static bool IsNested(NativePage page) => page is NativePage.ProfileSettings or NativePage.Administration;

    public static bool IsApproved(AuthenticatedUser user) =>
        string.Equals(user.Status, "approved", StringComparison.OrdinalIgnoreCase);

    /// <summary>Administration is only for an approved site admin.</summary>
    public static bool CanAdminister(AuthenticatedUser user) =>
        string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase)
        && IsApproved(user);

    /// <summary>
    /// Server maintenance (the maintenance state and its announcements) is readable by every signed-in account,
    /// as on the web. An account that is not approved otherwise sees only the approval screen and the local
    /// App settings and Version info pages. Hiding pages is not an authorization boundary; the server answers
    /// NOT_APPROVED for everything else.
    /// </summary>
    public static bool IsAvailable(NativePage page, AuthenticatedUser user) => page switch
    {
        NativePage.AwaitingApproval => !IsApproved(user),
        NativePage.ServerMaintenance or NativePage.AppSettings or NativePage.VersionInfo => true,
        NativePage.Administration => CanAdminister(user),
        _ => IsApproved(user)
    };

    public static NativePage StartPage(AuthenticatedUser user) =>
        IsApproved(user) ? NativePage.Dashboard : NativePage.AwaitingApproval;

    public static string SearchKey(NativePage page) => page switch
    {
        NativePage.Dashboard => "Dashboard",
        NativePage.Projects => "Projects",
        NativePage.CommitHistory => "Commit history",
        NativePage.AwaitingApproval => "Awaiting approval",
        NativePage.ServerMaintenance => "Server maintenance",
        NativePage.AppSettings => "App settings",
        NativePage.VersionInfo => "Version info",
        NativePage.ProfileSettings => "Profile settings",
        NativePage.Administration => "Administration",
        _ => string.Empty
    };
}
