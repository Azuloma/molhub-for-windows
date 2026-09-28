using System.Buffers.Binary;
using System.Text;
using FusionLedger.Windows;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Assert(NavigationPolicy.IsAllowed(new Uri(NavigationPolicy.AppUrl)), "Production URL must be allowed.");
Assert(NavigationPolicy.IsAllowed(new Uri("https://fusion-ledger.desase0175.workers.dev/projects")), "Production paths must be allowed.");
Assert(!NavigationPolicy.IsAllowed(new Uri("http://fusion-ledger.desase0175.workers.dev/")), "HTTP must be rejected.");
Assert(!NavigationPolicy.IsAllowed(new Uri("https://example.com/")), "Other origins must be rejected.");
Assert(NavigationPolicy.IsExternalLaunchable(new Uri("https://example.com/")), "External HTTPS must be launchable.");
Assert(NavigationPolicy.IsExternalLaunchable(new Uri("mailto:help@example.com")), "Mail links must be launchable.");
Assert(!NavigationPolicy.IsExternalLaunchable(new Uri(NavigationPolicy.AppUrl)), "Internal URLs must not launch externally.");
Assert(SettingsPolicy.LanguageKey == "ui.language" && SettingsPolicy.ThemeKey == "ui.theme", "Settings schema keys must remain fixed.");
Assert(SettingsPolicy.NormalizeLanguage("ja-JP") == "ja-JP" && SettingsPolicy.NormalizeLanguage("fr-FR") == "en-US", "Language values must be validated with an English fallback.");
Assert(SettingsPolicy.NormalizeTheme("Dark") == "Dark" && SettingsPolicy.NormalizeTheme("invalid") == "System", "Theme values must be validated with a System fallback.");
Assert(ThemePolicy.IsLightColor(255, 255, 255) && !ThemePolicy.IsLightColor(0, 0, 0), "System theme color classification must be deterministic.");
Assert(ThemePolicy.ResolveIsLight("System", true) && !ThemePolicy.ResolveIsLight("System", false)
    && ThemePolicy.ResolveIsLight("Light", false) && !ThemePolicy.ResolveIsLight("Dark", true), "System/Light/Dark theme resolution must respect OS lightness and explicit choices.");

Assert(ProfilePayloadParser.IsMeGet(new Uri("https://fusion-ledger.desase0175.workers.dev/api/me"), "GET"), "Exact /api/me GET must be accepted.");
Assert(!ProfilePayloadParser.IsMeGet(new Uri("https://fusion-ledger.desase0175.workers.dev/api/me?x=1"), "GET"), "Query /api/me must be rejected.");
Assert(!ProfilePayloadParser.IsMeGet(new Uri("https://fusion-ledger.desase0175.workers.dev/api/me"), "POST"), "Only GET /api/me is observed.");

var signedPng = "iVBORw0KGgo=";
var json = Encoding.UTF8.GetBytes($"{{\"user\":{{\"username\":\"  azunel  \",\"status\":\"approved\",\"role\":\"admin\",\"project_manager\":true,\"avatar\":\"data:image/png;base64,{signedPng}\"}}}}");
var parsed = ProfilePayloadParser.ParseAuthenticatedUser(200, json);
Assert(parsed?.Username == "azunel" && parsed.Status == "approved" && parsed.Role == "admin" && parsed.ProjectManager, "Strict user fields must parse.");
Assert(parsed?.AvatarPng?.Length == 8, "Only bounded PNG bytes must be retained.");
Assert(ProfilePayloadParser.ParseAuthenticatedUser(401, Encoding.UTF8.GetBytes("not-json")) is null, "401 must not parse a body.");
Assert(ProfilePayloadParser.ParseAuthenticatedUser(200, Encoding.UTF8.GetBytes("{\"user\":null}")) is null, "Null user cannot authenticate.");
Assert(ProfilePayloadParser.ParseAuthenticatedUser(200, Encoding.UTF8.GetBytes("{\"user\":{\"username\":\"x\",\"status\":\"approved\",\"role\":\"owner\"}}")) is null, "Unknown roles must be rejected.");
Assert(ProfilePayloadParser.ParseAuthenticatedUser(200, Encoding.UTF8.GetBytes("{\"user\":{\"username\":\"x\",\"status\":\"approved\",\"role\":\"member\",\"avatar\":\"https://example.com/a.png\"}}"))?.AvatarPng is null, "External avatars must be rejected.");
Assert(ProfilePayloadParser.ParseAuthenticatedUser(200, new byte[ProfilePayloadParser.MaxResponseBytes + 1]) is null, "Oversized bodies must be rejected.");

var coordinator = new ProfileResponseCoordinator();
var first = coordinator.BeginResponse();
var second = coordinator.BeginResponse();
Assert(!coordinator.IsCurrent(first) && coordinator.IsCurrent(second), "Latest /api/me response must win.");
coordinator.Invalidate();
Assert(!coordinator.IsCurrent(second), "Logout invalidation must stale an in-flight response.");

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "FusionLedger.Windows.csproj"))) root = root.Parent;
Assert(root is not null, "Source root must be discoverable.");
var sourceRoot = root!.FullName;
// ----- Administration (v0.14.5, display only) -----
AdministrationPolicyTests.Run(sourceRoot);
var mainXaml = File.ReadAllText(Path.Combine(sourceRoot, "Shell", "MainWindow.xaml"));
var mainCode = File.ReadAllText(Path.Combine(sourceRoot, "Shell", "MainWindow.xaml.cs"));
var loginXaml = File.ReadAllText(Path.Combine(sourceRoot, "Auth", "LoginWindow.xaml"));
var loginCode = File.ReadAllText(Path.Combine(sourceRoot, "Auth", "LoginWindow.xaml.cs"));
var appCode = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
var versionCode = File.ReadAllText(Path.Combine(sourceRoot, "Settings", "RuntimeVersionInfo.cs"));
var themeCode = File.ReadAllText(Path.Combine(sourceRoot, "Settings", "ThemeService.cs"));
Assert(mainXaml.Contains("controls:TitleBar", StringComparison.Ordinal), "MainWindow must use the WinUI TitleBar control.");
Assert(mainXaml.Contains("BackRequested", StringComparison.Ordinal), "TitleBar back event must remain wired.");
Assert(mainXaml.Contains("NavigationView", StringComparison.Ordinal) && mainXaml.Contains("FooterMenuItems", StringComparison.Ordinal), "Native navigation must expose footer items.");
Assert(mainXaml.Contains("IsPaneToggleButtonVisible=\"True\"", StringComparison.Ordinal)
    && mainXaml.Contains("PaneDisplayMode=\"Auto\"", StringComparison.Ordinal), "NavigationView must show its standard pane toggle in the automatic pane layout.");
Assert(mainXaml.Contains("OpenPaneLength=\"240\"", StringComparison.Ordinal), "Expanded NavigationView pane must be narrower than the 320 DIP default.");
var titleBarMarkup = mainXaml[..mainXaml.IndexOf("</controls:TitleBar>", StringComparison.Ordinal)];
Assert(!mainXaml.Contains("PaneToggleRequested", StringComparison.Ordinal)
    && !titleBarMarkup.Contains("IsPaneToggleButtonVisible", StringComparison.Ordinal), "TitleBar must not host a duplicate pane toggle.");
Assert(mainXaml.Contains("<ContentControl x:Name=\"ContentFrame\" HorizontalContentAlignment=\"Stretch\" VerticalContentAlignment=\"Stretch\">", StringComparison.Ordinal)
    && mainXaml.Contains("<EntranceThemeTransition FromHorizontalOffset=\"40\"", StringComparison.Ordinal)
    && mainCode.Contains("ContentFrame.Content = CreatePlaceholder(page)", StringComparison.Ordinal)
    && !mainCode.Contains("ContentFrame.Navigate", StringComparison.Ordinal), "Section content must animate with the native theme transition without Frame navigation.");
Assert(mainCode.Contains("private void RootGrid_Loaded", StringComparison.Ordinal)
    && mainCode.Contains("_initialNavigationCompleted = true", StringComparison.Ordinal)
    && mainCode.Contains("NavigateTo(NativePage.Dashboard, false)", StringComparison.Ordinal)
    && !mainCode.Contains("Navigation.SelectedItem = Navigation.MenuItems[0]", StringComparison.Ordinal), "Initial frame navigation must wait until the shell has loaded instead of reentering NavigationView selection.");
Assert(mainCode.Contains("_syncingNavigationSelection", StringComparison.Ordinal), "Programmatic selected-item synchronization must not recursively navigate the frame.");
Assert(mainXaml.Contains("Height=\"48\"", StringComparison.Ordinal) && mainXaml.Contains("VerticalContentAlignment=\"Center\"", StringComparison.Ordinal), "TitleBar must use centered 48px alignment.");
Assert(mainXaml.Contains("Height=\"32\"", StringComparison.Ordinal) && mainXaml.Contains("MinWidth=\"96\"", StringComparison.Ordinal) && mainXaml.Contains("MaxWidth=\"540\"", StringComparison.Ordinal), "Page search must use the bounded runtime size.");
Assert(mainXaml.Contains("SizeChanged=\"RootGrid_SizeChanged\"", StringComparison.Ordinal)
    && mainXaml.Contains("Loaded=\"RootGrid_Loaded\"", StringComparison.Ordinal)
    && mainCode.Contains("TitleBarLayoutPolicy.SearchWidthForClient", StringComparison.Ordinal)
    && !mainXaml.Contains("WindowWidthStates", StringComparison.Ordinal), "Page search width must follow actual client-size events, not VisualStates.");
Assert(TitleBarLayoutPolicy.SearchWidthForClient(1906) == 540
    && TitleBarLayoutPolicy.SearchWidthForClient(1600) == 540
    && TitleBarLayoutPolicy.SearchWidthForClient(1200) == 420
    && TitleBarLayoutPolicy.SearchWidthForClient(900) == 320
    && TitleBarLayoutPolicy.SearchWidthForClient(600) == 200
    && TitleBarLayoutPolicy.SearchWidthForClient(599) == 96, "Runtime search width policy must match all target ranges.");
Assert(mainXaml.Contains("Spacing=\"12\" VerticalAlignment=\"Center\"", StringComparison.Ordinal), "TitleBar right header must be centered with 12px spacing.");
Assert(mainXaml.Contains("KeyboardAcceleratorPlacementMode=\"Hidden\"", StringComparison.Ordinal), "Root shortcut placement hints must be hidden.");
Assert(mainXaml.Contains("Background=\"Transparent\"", StringComparison.Ordinal) && mainXaml.Contains("CornerRadius=\"16\"", StringComparison.Ordinal), "TitleBar action buttons must be transparent circular chrome.");
Assert(mainXaml.Contains("Glyph=\"&#xEA8F;\"", StringComparison.Ordinal), "Notifications must retain the Segoe Fluent Ringer glyph.");
Assert(mainXaml.Contains("Glyph=\"&#xEA8F;\" FontSize=\"18\" AutomationProperties.AccessibilityView=\"Raw\"", StringComparison.Ordinal)
    && mainXaml.Contains("x:Name=\"ProfilePicture\"", StringComparison.Ordinal)
    && mainXaml.Contains("DisplayName=\"\" AutomationProperties.AccessibilityView=\"Raw\"", StringComparison.Ordinal), "Decorative title-bar icon and avatar must be hidden from the accessibility tree.");
Assert(mainXaml.Contains("Width=\"320\" MaxHeight=\"440\"", StringComparison.Ordinal) && mainXaml.Contains("AccountSeparator", StringComparison.Ordinal), "Account flyout must have bounded identity/separator structure.");
Assert(mainXaml.Contains("x:Name=\"PageSearchBox\"", StringComparison.Ordinal)
    && mainXaml.Contains("<KeyboardAccelerator Key=\"K\" Modifiers=\"Control\"", StringComparison.Ordinal)
    && mainXaml.Contains("PageSearchKeyboardAccelerator_Invoked", StringComparison.Ordinal)
    && !mainCode.Contains("AddAccelerator(global::Windows.System.VirtualKey.K", StringComparison.Ordinal), "Ctrl+K must belong to the search box, not the root grid.");
Assert(mainCode.Contains("VirtualKey.N", StringComparison.Ordinal)
    && mainCode.Contains("VirtualKey.Left", StringComparison.Ordinal)
    && mainCode.Contains("VK_OEM_COMMA", StringComparison.Ordinal), "Non-search root shortcuts must remain defined.");
Assert(mainXaml.Contains("x:Name=\"NotificationsButton\" Width=\"32\" Height=\"32\"", StringComparison.Ordinal)
    && mainXaml.Contains("CornerRadius=\"6\"", StringComparison.Ordinal)
    && mainXaml.Contains("x:Name=\"ProfileButton\" Width=\"32\" Height=\"32\"", StringComparison.Ordinal)
    && mainXaml.Contains("CornerRadius=\"16\"", StringComparison.Ordinal), "Notification/profile button corner radii must follow their visual roles.");
Assert(mainXaml.IndexOf("x:Name=\"SignOutButton\"", StringComparison.Ordinal) < mainXaml.IndexOf("AccountSeparator", StringComparison.Ordinal)
    && mainXaml.IndexOf("AccountSeparator", StringComparison.Ordinal) < mainXaml.IndexOf("x:Name=\"ProfileSettingsButton\"", StringComparison.Ordinal)
    && mainXaml.IndexOf("x:Name=\"ProfileSettingsButton\"", StringComparison.Ordinal) < mainXaml.IndexOf("x:Name=\"AdministrationButton\"", StringComparison.Ordinal), "Account flyout must keep sign-out in the identity block, then profile/admin rows.");
Assert(mainXaml.Split("<local:ThinAcrylicBackdrop />").Length == 3
    && mainXaml.Split("ShouldConstrainToRootBounds=\"False\"").Length == 3
    && mainXaml.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", StringComparison.Ordinal)
    && mainXaml.Contains("<Setter Property=\"Padding\" Value=\"0\" />", StringComparison.Ordinal), "Account and notification flyouts must use a transparent presenter over an acrylic backdrop and may extend past the window.");
Assert(mainXaml.Contains("<Grid Width=\"360\" MaxHeight=\"440\">", StringComparison.Ordinal)
    && mainXaml.Contains("<ScrollViewer Width=\"320\" MaxHeight=\"440\"", StringComparison.Ordinal), "Notifications must be slightly wider than the 320px account flyout, and both must scroll instead of clipping.");
Assert(!mainXaml.Contains("WebView2", StringComparison.Ordinal) && !mainCode.Contains("WebView2", StringComparison.Ordinal), "MainWindow must not host WebView2.");
Assert(mainCode.Contains("CreateSettingsPage", StringComparison.Ordinal) && mainCode.Contains("CreateVersionInfoPage", StringComparison.Ordinal), "Settings and Version info must be functional native pages.");
Assert(mainCode.Contains("ScrollViewer", StringComparison.Ordinal) && mainCode.Contains("RadioButtons", StringComparison.Ordinal)
    && mainCode.Contains("LocalSettings", StringComparison.Ordinal) && themeCode.Contains("RequestedTheme", StringComparison.Ordinal), "Settings must be scrollable, native, persisted and theme-aware.");
Assert(mainCode.Contains("AppWindowTitleBar.IsCustomizationSupported", StringComparison.Ordinal)
    && mainCode.Contains("PreferredHeightOption = TitleBarHeightOption.Tall", StringComparison.Ordinal)
    && !mainCode.Contains("appWindow.TitleBar.Height", StringComparison.Ordinal), "Title bar must request Tall without assigning physical AppWindow pixels to XAML DIP.");
Assert(mainCode.Contains("SettingsThemeSavedMessage", StringComparison.Ordinal) && mainCode.Contains("SettingsRestartMessage", StringComparison.Ordinal), "Theme and language save feedback must be distinct.");
Assert(versionCode.Contains("RuntimeInformation.ProcessArchitecture", StringComparison.Ordinal)
    && versionCode.Contains("Package.Current", StringComparison.Ordinal)
    && versionCode.Contains("Dependencies", StringComparison.Ordinal)
    && versionCode.Contains("Microsoft.WindowsAppRuntime", StringComparison.Ordinal)
    && versionCode.Contains("GetAvailableBrowserVersionString", StringComparison.Ordinal)
    && versionCode.Contains("WindowsAppSdkVersion", StringComparison.Ordinal)
    && !versionCode.Contains("typeof(Microsoft.UI.Xaml.Application).Assembly.GetName().Version", StringComparison.Ordinal)
    && !versionCode.Contains("0.14.4", StringComparison.Ordinal), "Version info must report observed Windows App SDK/WebView2 runtime details without a hardcoded display fallback.");
Assert(loginXaml.Contains("WebView2", StringComparison.Ordinal) && loginCode.Contains("CoreWebView2", StringComparison.Ordinal), "Only LoginWindow may host WebView2.");
Assert(loginXaml.Contains("x:Name=\"RootGrid\"", StringComparison.Ordinal)
    && loginCode.Contains("ThemeService.ReadSavedPreference", StringComparison.Ordinal)
    && loginCode.Contains("_themeService.Apply", StringComparison.Ordinal), "LoginWindow must apply the persisted theme.");
foreach (var source in new[] { mainXaml, mainCode, loginXaml, loginCode })
{
    Assert(!source.Contains("ExecuteScriptAsync", StringComparison.Ordinal), "Script injection is forbidden.");
    Assert(!source.Contains("GetCookies", StringComparison.Ordinal) && !source.Contains("Request.Headers", StringComparison.Ordinal), "Cookie/header extraction is forbidden.");
}
Assert(mainCode.Contains("SetTitleBar(AppTitleBar)", StringComparison.Ordinal), "Native title bar must own drag/caption behavior.");
Assert(!mainCode.Contains("RightInset", StringComparison.Ordinal), "Manual title bar inset logic must be absent.");
Assert(!mainCode.Contains("fake", StringComparison.OrdinalIgnoreCase) && !mainXaml.Contains("fake", StringComparison.OrdinalIgnoreCase), "Native shell must not contain fake data.");
Assert(!loginXaml.Contains("MolHub sign in", StringComparison.Ordinal) && !loginXaml.Contains("Sign in to continue", StringComparison.Ordinal), "Login user-facing strings must come from resources.");
Assert(!mainXaml.Contains("Notifications\"", StringComparison.Ordinal) && !mainXaml.Contains("Profile settings\"", StringComparison.Ordinal), "MainWindow user-facing strings must come from resources.");
Assert(!mainXaml.Contains("email", StringComparison.OrdinalIgnoreCase) && !mainXaml.Contains("raw id", StringComparison.OrdinalIgnoreCase) && !mainXaml.Contains("token", StringComparison.OrdinalIgnoreCase), "Account flyout must not expose fake email/id/token data.");
Assert(loginCode.Contains("DeleteAllCookies", StringComparison.Ordinal), "Sign out must clear cookies through the active WebView2 profile.");
Assert(loginCode.Contains("ClearBrowsingDataAsync", StringComparison.Ordinal), "Sign out must clear profile browsing data through WebView2.");
Assert(loginCode.Contains("ShowSessionRecoveryError", StringComparison.Ordinal) && loginCode.Contains("SessionClearError", StringComparison.Ordinal), "Clear failure must remain in a localized retry/close state.");
var closedHandler = loginCode[loginCode.IndexOf("private void LoginWindow_Closed", StringComparison.Ordinal)..];
Assert(closedHandler.IndexOf("_themeService.Dispose()", StringComparison.Ordinal) >= 0
    && closedHandler.IndexOf("if (_core is not { } core) return;", StringComparison.Ordinal) > closedHandler.IndexOf("_themeService.Dispose()", StringComparison.Ordinal)
    && closedHandler.Contains("core.NavigationStarting -= Core_NavigationStarting", StringComparison.Ordinal), "LoginWindow must always dispose its theme service before conditionally detaching WebView events.");
Assert(mainCode.Contains("RollbackSetting", StringComparison.Ordinal)
    && mainCode.Contains("_updatingSettings = true", StringComparison.Ordinal), "Settings save failures must rollback radio selections under a guard.");
var resetSuccess = loginCode.IndexOf("SessionResetSucceeded?.Invoke", StringComparison.Ordinal);
var rootNavigate = loginCode.IndexOf("_core.Navigate(NavigationPolicy.AppUrl)", resetSuccess, StringComparison.Ordinal);
Assert(resetSuccess >= 0 && rootNavigate > resetSuccess, "Root navigation must occur only after profile clearing succeeds.");
Assert(!appCode.Contains("Directory.Delete", StringComparison.Ordinal), "Sign out must not delete the profile directory directly.");
var mainCreation = appCode.IndexOf("new MainWindow", StringComparison.Ordinal);
var oldLoginClose = appCode.IndexOf("login?.Close", StringComparison.Ordinal);
Assert(mainCreation >= 0 && oldLoginClose > mainCreation, "Authenticated MainWindow must activate before LoginWindow closes.");
var signOutStart = appCode.IndexOf("RequestSignOut", StringComparison.Ordinal);
var replacement = appCode.IndexOf("ShowLoginWindow(resetSession: true)", signOutStart, StringComparison.Ordinal);
var resetHandler = appCode.IndexOf("Login_SessionResetSucceeded", StringComparison.Ordinal);
var mainClose = appCode.IndexOf("main.Close()", resetHandler, StringComparison.Ordinal);
Assert(replacement >= 0 && resetHandler >= 0 && mainClose > resetHandler, "Sign out must create the replacement login window before closing MainWindow.");
Assert(File.ReadAllText(Path.Combine(sourceRoot, "FusionLedger.Windows.csproj")).Contains("<Version>0.14.5</Version>", StringComparison.Ordinal), "Version source of truth must be v0.14.5.");
Assert(File.ReadAllText(Path.Combine(sourceRoot, "Package.appxmanifest")).Contains("Version=\"0.14.5.0\"", StringComparison.Ordinal), "Manifest version must be v0.14.5.");
var changelogText = File.ReadAllText(Path.Combine(sourceRoot, "CHANGELOG.md"));
Assert(File.ReadAllText(Path.Combine(sourceRoot, "VERSION.md")).Contains("v0.14.5", StringComparison.Ordinal)
    && changelogText.Contains("## v0.14.5", StringComparison.Ordinal)
    && changelogText.IndexOf("## v0.14.5", StringComparison.Ordinal) < changelogText.IndexOf("## v0.14.4", StringComparison.Ordinal), "Version documentation must be updated, with v0.14.5 above v0.14.4 in the changelog.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    Assert(resource.Contains("NotConnected", StringComparison.Ordinal) && resource.Contains("Page_Dashboard", StringComparison.Ordinal)
        && resource.Contains("AccountMenuForFormat", StringComparison.Ordinal)
        && resource.Contains("SettingsDescription", StringComparison.Ordinal)
        && resource.Contains("VersionInfoDescription", StringComparison.Ordinal), $"Localization resources are incomplete: {locale}");
}
Assert(!File.Exists(Path.Combine(sourceRoot, "Assets", "Fonts", "MonaSans.ttf")), "Native Mona Sans binary must remain absent.");

var manifest = File.ReadAllText(Path.Combine(sourceRoot, "Package.appxmanifest"));
Assert(manifest.Contains("<DisplayName>MolHub for Windows</DisplayName>", StringComparison.Ordinal)
    && manifest.Contains("<PublisherDisplayName>MolHub</PublisherDisplayName>", StringComparison.Ordinal)
    && manifest.Contains("uap:VisualElements AppListEntry=\"default\" DisplayName=\"MolHub for Windows\"", StringComparison.Ordinal), "Package display names must use the MolHub brand.");
Assert(manifest.Contains("<Identity Name=\"FusionLedger.Windows\"", StringComparison.Ordinal), "Package identity must stay stable so upgrades keep the sign-in profile and settings.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    Assert(!resource.Contains("Fusion Ledger", StringComparison.Ordinal) && resource.Contains("<value>MolHub for Windows</value>", StringComparison.Ordinal), $"User-facing strings must use the MolHub brand: {locale}");
}
foreach (var logo in new[] { "Square44x44Logo.scale-100.png", "Square44x44Logo.targetsize-24_altform-unplated.png", "Square44x44Logo.targetsize-24_altform-lightunplated.png",
    "Square150x150Logo.scale-100.png", "Wide310x150Logo.scale-100.png", "SmallTile.scale-100.png", "LargeTile.scale-100.png", "StoreLogo.scale-100.png",
    "AppIconOnDark.png", "AppIconOnLight.png", "AppIconOnDark.ico", "AppIconOnLight.ico" })
{
    Assert(File.Exists(Path.Combine(sourceRoot, "Assets", logo)), $"Icon asset is missing: {logo}");
}
Assert(!File.Exists(Path.Combine(sourceRoot, "Assets", "fusion-ledger_ico.png")) && !manifest.Contains("fusion-ledger_ico", StringComparison.Ordinal), "The retired Fusion Ledger icon must not be referenced.");
Assert(AppIconAssets.TitleBarImageUri(true) == AppIconAssets.OnLightImageUri && AppIconAssets.TitleBarImageUri(false) == AppIconAssets.OnDarkImageUri
    && AppIconAssets.WindowIconFile(true) == AppIconAssets.OnLightIconFile && AppIconAssets.WindowIconFile(false) == AppIconAssets.OnDarkIconFile, "Icon variants must follow surface lightness.");
Assert(mainCode.Contains("ActualThemeChanged", StringComparison.Ordinal) && mainCode.Contains("WindowIcon.Apply(this)", StringComparison.Ordinal)
    && loginCode.Contains("WindowIcon.Apply(this)", StringComparison.Ordinal), "Both windows must set the MolHub icon and the title bar icon must follow the theme.");

Assert(LoginWindowLayoutPolicy.ClientSize(1.0, 2560, 1392) == new LoginWindowLayoutPolicy.PixelSize(481, 683), "Sign-in client area must be 481 x 683 DIP at 100%.");
Assert(LoginWindowLayoutPolicy.ClientSize(1.5, 2560, 1392) == new LoginWindowLayoutPolicy.PixelSize(722, 1025), "Sign-in client area must scale with DPI.");
Assert(LoginWindowLayoutPolicy.ClientSize(1.5, 1280, 672) == new LoginWindowLayoutPolicy.PixelSize(722, 624)
    && LoginWindowLayoutPolicy.ClientSize(2.0, 600, 400) == new LoginWindowLayoutPolicy.PixelSize(536, 336), "Sign-in window must stay inside the work area with a scaled margin.");
Assert(LoginWindowLayoutPolicy.ClientSize(double.NaN, 2560, 1392) == LoginWindowLayoutPolicy.ClientSize(1.0, 2560, 1392), "Invalid DPI scale must fall back to 100%.");
Assert(LoginWindowLayoutPolicy.CenteredPosition(483, 685, 0, 0, 2560, 1392) == new LoginWindowLayoutPolicy.PixelPoint(1038, 353)
    && LoginWindowLayoutPolicy.CenteredPosition(900, 900, 100, 50, 800, 600) == new LoginWindowLayoutPolicy.PixelPoint(100, 50), "Sign-in window must be centered and never start outside the work area.");
Assert(loginXaml.Contains("<RowDefinition Height=\"32\" />", StringComparison.Ordinal)
    && loginXaml.Contains("x:Name=\"LoginTitleBar\"", StringComparison.Ordinal)
    && loginCode.Contains("ExtendsContentIntoTitleBar = true", StringComparison.Ordinal)
    && loginCode.Contains("SetTitleBar(LoginTitleBar)", StringComparison.Ordinal), "Sign-in window must use a 32 DIP TitleBar-owned caption area.");
Assert(loginCode.Contains("LoginWindowLayoutPolicy.ClientSize", StringComparison.Ordinal)
    && loginCode.Contains("GetDpiForWindow", StringComparison.Ordinal)
    && loginCode.Contains("ResizeClient", StringComparison.Ordinal)
    && loginCode.Contains("GetClientRect(hwnd, out var rendered)", StringComparison.Ordinal)
    && loginCode.Contains("IsResizable = false", StringComparison.Ordinal), "Sign-in window must be sized from the DPI-aware compact layout policy.");
Assert(loginXaml.Contains("x:Name=\"LoginOverlay\"", StringComparison.Ordinal)
    && loginCode.Contains("core.NavigationCompleted -= Core_NavigationCompleted", StringComparison.Ordinal), "Loading overlay must hide on navigation completion and detach its handler on close.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    Assert(File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw")).Contains("<data name=\"PleaseWait\">", StringComparison.Ordinal), $"Sign-in loading text must be localized: {locale}");
}

var backdropCode = File.ReadAllText(Path.Combine(sourceRoot, "Shell", "ThinAcrylicBackdrop.cs"));
Assert(backdropCode.Contains("DesktopAcrylicKind.Thin", StringComparison.Ordinal) && backdropCode.Contains("IsInputActive = true", StringComparison.Ordinal)
    && backdropCode.Contains("AccessibilitySettings().HighContrast", StringComparison.Ordinal) && mainCode.Contains("UpdateFlyoutBackdropTheme", StringComparison.Ordinal), "Title-bar flyouts must draw active thin acrylic that follows the app theme and High Contrast.");
Assert(!FlyoutPlacementPolicy.OpenAbove(197, 1000, 50) && FlyoutPlacementPolicy.OpenAbove(139, 117, 1000)
    && !FlyoutPlacementPolicy.OpenAbove(139, 160, 1000) && !FlyoutPlacementPolicy.OpenAbove(440, 100, 60), "Flyouts must open above only when the work area below is too short and there is more room above.");
Assert(mainCode.Contains("ShowHeaderFlyout(NotificationsFlyout, NotificationsButton)", StringComparison.Ordinal)
    && mainCode.Contains("ShowHeaderFlyout(AccountFlyout, ProfileButton)", StringComparison.Ordinal)
    && mainCode.Contains("DisplayAreaFallback.Nearest).WorkArea", StringComparison.Ordinal), "Title-bar flyouts must choose their placement from the monitor work area.");

// Web data bridge: exact document, trusted source, fixed commands, bounded request/result shapes.
Assert(BridgePolicy.BridgeUri.AbsoluteUri == "https://fusion-ledger.desase0175.workers.dev/webview-bridge", "Bridge must load the canonical extensionless document on the production origin.");
Assert(BridgePolicy.IsBridgeDocument(BridgePolicy.BridgeUri)
    && !BridgePolicy.IsBridgeDocument(new Uri("https://fusion-ledger.desase0175.workers.dev/webview-bridge.html"))
    && !BridgePolicy.IsBridgeDocument(new Uri("https://fusion-ledger.desase0175.workers.dev/webview-bridge?x=1"))
    && !BridgePolicy.IsBridgeDocument(new Uri("https://fusion-ledger.desase0175.workers.dev/webview-bridge#x"))
    && !BridgePolicy.IsBridgeDocument(new Uri("http://fusion-ledger.desase0175.workers.dev/webview-bridge"))
    && !BridgePolicy.IsBridgeDocument(new Uri("https://example.com/webview-bridge"))
    && !BridgePolicy.IsBridgeDocument(new Uri("https://fusion-ledger.desase0175.workers.dev/")), "Only the exact bridge document may load.");
Assert(BridgePolicy.IsTrustedSource("https://fusion-ledger.desase0175.workers.dev/webview-bridge")
    && !BridgePolicy.IsTrustedSource("https://fusion-ledger.desase0175.workers.dev/")
    && !BridgePolicy.IsTrustedSource(null) && !BridgePolicy.IsTrustedSource("not a url"), "Only the bridge document may answer.");
Assert(BridgePolicy.IsKnownCommand("session") && BridgePolicy.IsKnownCommand("publishCommit") && !BridgePolicy.IsKnownCommand("deleteMember")
    && !BridgePolicy.IsKnownCommand("fetch") && !BridgePolicy.IsKnownCommand(null)
    && BridgePolicy.IsWrite("logout") && BridgePolicy.IsWrite("publishCommit") && !BridgePolicy.IsWrite("dashboard"), "Bridge commands must come from the fixed read/write allowlist.");
var bridgeId = BridgePolicy.NewRequestId();
Assert(BridgePolicy.IsValidRequestId(bridgeId) && !BridgePolicy.IsValidRequestId("bad id") && !BridgePolicy.IsValidRequestId(new string('a', 81)), "Request ids must match the bridge pattern.");
var built = System.Text.Json.JsonDocument.Parse(BridgePolicy.BuildRequest("project", "r1", new System.Text.Json.Nodes.JsonObject { ["projectId"] = "p1", ["requestId"] = "evil" })!).RootElement;
Assert(built.GetProperty("command").GetString() == "project" && built.GetProperty("requestId").GetString() == "r1"
    && built.GetProperty("payload").GetProperty("projectId").GetString() == "p1" && !built.GetProperty("payload").TryGetProperty("requestId", out _), "Bridge requests must carry command, requestId and a payload without a spoofed requestId.");
Assert(BridgePolicy.BuildRequest("fetch", "r1", null) is null && BridgePolicy.BuildRequest("session", "bad id", null) is null
    && BridgePolicy.BuildRequest("publishCommit", "r1", new System.Text.Json.Nodes.JsonObject { ["changes"] = new string('x', 50_001) }) is null, "Unknown commands, invalid ids and oversized payloads must never be sent.");
var readResult = BridgePolicy.ParseResult("{\"requestId\":\"r1\",\"status\":200,\"ok\":true,\"data\":{\"user\":null},\"meta\":{\"nextOffset\":null},\"retryable\":false}");
Assert(readResult is { Ok: true, Status: 200, Outcome: BridgeOutcome.None, ErrorCode: null } && readResult.Data?.GetProperty("user").ValueKind == System.Text.Json.JsonValueKind.Null && readResult.Meta is not null, "Read results must keep data and pagination meta.");
var timeoutResult = BridgePolicy.ParseResult("{\"requestId\":\"r2\",\"status\":0,\"ok\":false,\"error\":{\"code\":\"TIMEOUT\"},\"retryable\":false,\"outcome\":\"unknown\"}");
Assert(timeoutResult is { Status: 0, Ok: false, ErrorCode: "TIMEOUT", Outcome: BridgeOutcome.Unknown }, "Write timeouts must stay outcome Unknown.");
var conflictResult = BridgePolicy.ParseResult("{\"requestId\":\"r3\",\"status\":409,\"ok\":false,\"error\":{\"code\":\"STALE_VERSION\",\"details\":{\"head\":\"c9\"}},\"outcome\":\"rejected\"}");
Assert(conflictResult is { ErrorCode: "STALE_VERSION", Outcome: BridgeOutcome.Rejected } && conflictResult.ErrorDetails?.GetProperty("head").GetString() == "c9", "Conflict codes and details must reach the host.");
Assert(BridgePolicy.ParseResult("{\"requestId\":\"r4\",\"status\":500,\"ok\":false,\"outcome\":\"surprise\"}") is { ErrorCode: "SERVER_ERROR", Outcome: BridgeOutcome.Unknown }, "Unrecognised outcomes must be treated as Unknown.");
Assert(BridgePolicy.ParseResult("{\"requestId\":null,\"status\":400,\"ok\":false}") is null && BridgePolicy.ParseResult("[]") is null
    && BridgePolicy.ParseResult("not json") is null && BridgePolicy.ParseResult(new string(' ', BridgePolicy.MaxResultChars + 1)) is null, "Malformed, uncorrelated or oversized results must be dropped.");
Assert(BridgeResult.HostFailure("r5", "BRIDGE_UNAVAILABLE", false, BridgeOutcome.Rejected) is { Status: 0, Ok: false, Outcome: BridgeOutcome.Rejected }, "Host failures must report status 0 and an explicit outcome.");
var bridgeCode = File.ReadAllText(Path.Combine(sourceRoot, "Bridge", "WebBridgeClient.cs"));
Assert(bridgeCode.Contains("settings.IsWebMessageEnabled = true", StringComparison.Ordinal)
    && bridgeCode.Contains("settings.AreDevToolsEnabled = false", StringComparison.Ordinal)
    && bridgeCode.Contains("settings.AreHostObjectsAllowed = false", StringComparison.Ordinal)
    && bridgeCode.Contains("_controller.IsVisible = false", StringComparison.Ordinal)
    && bridgeCode.Contains("if (!BridgePolicy.IsTrustedSource(e.Source)) return;", StringComparison.Ordinal)
    && bridgeCode.Contains("if (BridgePolicy.IsBridgeDocument(TryParseUri(e.Uri))) return;", StringComparison.Ordinal)
    && bridgeCode.Contains("Core_FrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) => e.Cancel = true", StringComparison.Ordinal)
    && bridgeCode.Contains("CoreWebView2PermissionState.Deny", StringComparison.Ordinal)
    && bridgeCode.Contains("e.Handled = true", StringComparison.Ordinal), "The hidden bridge must enable WebMessage only for the verified bridge document and deny everything else.");
Assert(!bridgeCode.Contains("ExecuteScriptAsync", StringComparison.Ordinal) && !bridgeCode.Contains("AddScriptToExecuteOnDocumentCreated", StringComparison.Ordinal)
    && !bridgeCode.Contains("AddHostObjectToScript", StringComparison.Ordinal) && !bridgeCode.Contains("CookieManager", StringComparison.Ordinal)
    && !bridgeCode.Contains("GetCookies", StringComparison.Ordinal), "The bridge host must not inject script, expose host objects or touch cookies.");
Assert(loginCode.Contains("IsWebMessageEnabled = false", StringComparison.Ordinal)
    && loginCode.Contains("WebViewProfile.GetEnvironmentAsync()", StringComparison.Ordinal)
    && bridgeCode.Contains("WebViewProfile.GetEnvironmentAsync()", StringComparison.Ordinal), "Sign-in keeps WebMessage disabled and shares the single WebView2 environment with the bridge.");
var signOutHandler = mainCode[mainCode.IndexOf("private async void SignOut_Click", StringComparison.Ordinal)..];
Assert(signOutHandler.IndexOf("RequestAsync(\"logout\")", StringComparison.Ordinal) >= 0
    && signOutHandler.IndexOf("RequestAsync(\"logout\")", StringComparison.Ordinal) < signOutHandler.IndexOf("App.RequestSignOut()", StringComparison.Ordinal)
    && mainCode.Contains("_bridge.Dispose()", StringComparison.Ordinal), "Sign-out must revoke the server session through the bridge before clearing local data, and the bridge must close with MainWindow.");

// Native Dashboard: server DTO parsing, web-equivalent counts and presentation rules.
var dashboardJson = """
{"projects":[
  {"id":"p1","name":"Sonic Nightmare","reservation":{"userId":"u1","username":"Azunel","startedAt":"2026-09-19T14:00:00.000Z"}},
  {"id":"p2","name":"Splats Framework","reservation":{"userId":"u2","username":"kalvin","startedAt":null}},
  {"id":"p3","name":"Scratch Edition","reservation":null},
  {"id":"","name":"Invalid"}],
 "commits":[
  {"id":"74a6ba6e-0000-4000-8000-000000000000","projectName":"Sonic Nightmare","title":"Sonic Nightmare","changes":"small fixes","url":"https://example.com/a","version":"v0.4.785B","createdAt":"2026-09-19T14:24:00.000Z","author":{"id":"u2","username":"kalvin","avatar":"https://example.com/a.png"}},
  {"id":"c2","projectName":"Splats","title":"","changes":"no title is skipped","author":{"username":"x"}}],
 "activity":[{"projectName":"Splats Framework","actor":"azunel","createdAt":"2026-09-18T09:29:00.000Z"}],
 "stats":{"commits":4,"projects":2},
 "pending":{"accounts":1,"requests":2}}
""";
var dashboard = DashboardModel.Parse(System.Text.Json.JsonDocument.Parse(dashboardJson).RootElement);
Assert(dashboard is { ActiveProjects: 3, InProgress: 2, PendingApprovals: 3 } && dashboard.ReservedBy("azunel").Count == 1 && dashboard.ReservedBy("nobody").Count == 0,
    "Dashboard counts must be computed from server data exactly like the web dashboard.");
Assert(dashboard!.Commits.Count == 1 && dashboard.Commits[0].AuthorName == "kalvin" && dashboard.Commits[0].AuthorAvatar is null && dashboard.Commits[0].Version == "v0.4.785B"
    && dashboard.Activity.Count == 1 && dashboard.Activity[0].Actor == "azunel", "Dashboard commits/activity must be parsed with bounded fields and validated avatars.");
Assert(DashboardModel.Parse(System.Text.Json.JsonDocument.Parse("{\"projects\":{}}").RootElement) is null
    && DashboardModel.Parse(System.Text.Json.JsonDocument.Parse("[]").RootElement) is null, "Unexpected dashboard shapes must not render.");
Assert(DashboardModel.Preview(new string('a', 421)) == new string('a', 420) + "…" && DashboardModel.Preview("  short  ") == "short"
    && DashboardModel.ShortId("74a6ba6e-0000") == "74a6ba6" && DashboardModel.ShortId("abc") == "abc", "Commit previews and short IDs must match the web feed.");
Assert(DashboardModel.FormatDate(DateTimeOffset.Parse("2026-09-19T14:24:00Z"), "en-US", TimeZoneInfo.Utc) == "Sep 19, 2026, 02:24 PM"
    && DashboardModel.FormatDate(DateTimeOffset.Parse("2026-09-19T14:24:00Z"), "ja-JP", TimeZoneInfo.Utc) == "2026/09/19 14:24"
    && DashboardModel.FormatDate(null, "en-US") == string.Empty, "Dashboard dates must be localized and never invented.");
Assert(DashboardModel.ShowsPendingApprovals("admin", false) && DashboardModel.ShowsPendingApprovals("member", true) && !DashboardModel.ShowsPendingApprovals("member", false),
    "Pending approvals are only shown to site admins and project owners.");
Assert(DashboardModel.ErrorFor(BridgeResult.HostFailure("r", "BRIDGE_UNAVAILABLE", true, BridgeOutcome.None)) == DashboardError.Connection
    && DashboardModel.ErrorFor(new BridgeResult("r", 401, false, null, null, "UNAUTHORIZED", null, false, BridgeOutcome.None)) == DashboardError.SessionEnded
    && DashboardModel.ErrorFor(new BridgeResult("r", 403, false, null, null, "NOT_APPROVED", null, false, BridgeOutcome.None)) == DashboardError.PendingApproval
    && DashboardModel.ErrorFor(new BridgeResult("r", 503, false, null, null, "MAINTENANCE", null, false, BridgeOutcome.None)) == DashboardError.Maintenance
    && DashboardModel.ErrorFor(new BridgeResult("r", 429, false, null, null, "RATE_LIMIT", null, true, BridgeOutcome.None)) == DashboardError.RateLimited
    && DashboardModel.ErrorFor(new BridgeResult("r", 500, false, null, null, "SERVER_ERROR", null, true, BridgeOutcome.None)) == DashboardError.Unexpected,
    "Dashboard errors must map bridge results to honest states.");
var dashboardCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Dashboard", "DashboardView.cs"));
var usedKeys = System.Text.RegularExpressions.Regex.Matches(dashboardCode, "\"(Dashboard_[A-Za-z]+)\"").Select(m => m.Groups[1].Value)
    .Concat(new[] { "ErrorConnection", "ErrorSession", "ErrorPending", "ErrorMaintenance", "ErrorRateLimit", "ErrorUnexpected" }.Select(k => "Dashboard_" + k + "Title"))
    .Distinct().ToList();
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    foreach (var key in usedKeys) Assert(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"Missing dashboard string {key} in {locale}");
}
Assert(mainCode.Contains("_bridge.RequestAsync(\"dashboard\")", StringComparison.Ordinal) && mainCode.Contains("if (page == NativePage.Dashboard) return CreateDashboardPage();", StringComparison.Ordinal)
    && dashboardCode.Contains("Dashboard_StorageNote", StringComparison.Ordinal)
    && !dashboardCode.Contains("Create project", StringComparison.OrdinalIgnoreCase) && !dashboardCode.Contains("publish:", StringComparison.Ordinal),
    "Dashboard must load through the bridge, keep the storage privacy note and render no actions without a native implementation.");
var avatarCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Common", "AvatarImage.cs"));
Assert(avatarCode.Contains("picture.Loaded +=", StringComparison.Ordinal) && avatarCode.Contains("picture.ActualThemeChanged +=", StringComparison.Ordinal)
    && dashboardCode.Contains("AvatarImage.Attach(picture, avatar, 56)", StringComparison.Ordinal)
    && mainCode.Contains("AvatarImage.Attach(ProfilePicture, avatar, 64)", StringComparison.Ordinal) && mainCode.Contains("AvatarImage.Attach(AccountPicture, avatar, 96)", StringComparison.Ordinal),
    "Avatars must be decoded again whenever a picture is loaded or its theme changes, so a theme switch never leaves only initials.");
var captionCode = File.ReadAllText(Path.Combine(sourceRoot, "Shell", "CaptionButtonTheme.cs"));
Assert(captionCode.Contains("TitleBar.PreferredTheme", StringComparison.Ordinal) && captionCode.Contains("root.ActualThemeChanged +=", StringComparison.Ordinal)
    && captionCode.Contains("TitleBarTheme.UseDefaultAppMode", StringComparison.Ordinal)
    && mainCode.Contains("CaptionButtonTheme.Attach(this, RootGrid)", StringComparison.Ordinal) && loginCode.Contains("CaptionButtonTheme.Attach(this, RootGrid)", StringComparison.Ordinal),
    "System caption buttons must follow the app theme (High Contrast keeps system colors) in both windows.");
Assert(mainCode.Contains("if (language == ReadLanguage()) return;", StringComparison.Ordinal) && mainCode.Contains("if (theme == ReadTheme()) return;", StringComparison.Ordinal),
    "Settings must ignore selections equal to the saved value so opening the page reports no save.");

// ----- Projects (read-only) -----
static System.Text.Json.JsonElement Json(string text) => System.Text.Json.JsonDocument.Parse(text).RootElement;
var projectList = ProjectsModel.ParseProjectList(Json("""
[{"id":"62cbdc9e-1","name":"Sonic Nightmare","description":"","latest":{"id":"74a6ba6e-0","title":"Sonic Nightmare","version":"v0.4.785B","createdAt":"2026-09-19T14:24:00.000Z"},"reservation":null,"updatedAt":"2026-09-19T14:24:00.000Z"},
 {"id":"p2","name":"Splats Framework","description":"A fork","latest":null,"reservation":{"userId":"u1","username":"Azunel","startedAt":"2026-09-19T14:00:00.000Z"}},
 {"id":"bad/id","name":"Invalid id"},{"id":"p4","name":""}]
"""), Json("""{"limit":30,"offset":0,"nextOffset":30,"total":42}"""));
Assert(projectList is { Total: 42, NextOffset: 30 } && projectList.Projects.Count == 2 && projectList.Projects[0].Latest?.Version == "v0.4.785B"
    && projectList.Projects[1].Latest is null && projectList.Projects[1].Description == "A fork",
    "Project lists must be parsed with bounded fields, valid ids and server paging.");
Assert(ProjectsModel.ParseProjectList(Json("{}"), null) is null
    && ProjectsModel.ParseProjectList(Json("[]"), Json("""{"nextOffset":null,"total":0}""")) is { Total: 0, NextOffset: null },
    "Unexpected project list shapes must not render, and the end of a list has no next page.");
Assert(DashboardModel.Parse(Json("""{"projects":[],"commits":[],"pending":{"accounts":null,"requests":"2"}}""")) is { PendingApprovals: 0 },
    "Non-numeric counters must read as zero instead of failing.");
Assert(ProjectsModel.StateFor(null, "azunel") == ReservationState.Available
    && ProjectsModel.StateFor(projectList!.Projects[1].Reservation, "azunel") == ReservationState.Yours
    && ProjectsModel.StateFor(new ProjectReservation("kalvin", null), "azunel") == ReservationState.Other
    && ProjectsModel.StateFor(new ProjectReservation(null, null), "azunel") == ReservationState.Other,
    "Reservation state must come from the server reservation only.");

var projectDetail = ProjectsModel.ParseProjectDetail(Json("""
{"project":{"id":"p1","name":"Sonic Nightmare","description":"","createdAt":null,"latest":{"id":"c1","title":"Newest","version":"v2","createdAt":"2026-09-19T14:24:00.000Z"},"reservation":{"username":"kalvin","startedAt":null}},
 "members":[{"id":"u1","username":"madnessprime","role":"owner","avatar":null},{"id":"u2","username":"azunel","role":"admin"},{"id":"u3","username":"kalvin","role":"member"},{"username":""}],
 "commits":[{"id":"c1","projectId":"p1","projectName":"Sonic Nightmare","title":"Newest","changes":"fixes","url":"https://drive.example.com/f","version":"v2","createdAt":"2026-09-19T14:24:00.000Z","author":{"username":"kalvin"}},
            {"id":"c0","projectId":"p1","title":"Older","changes":"","url":"javascript:alert(1)","version":"","createdAt":"2026-09-18T01:00:00.000Z","author":{"username":"azunel"}}],
 "summary":{"total":5,"projects":1,"contributors":2,"versions":4}}
"""));
Assert(projectDetail is { Members.Count: 3, Commits.Count: 2, Summary: { Total: 5, Contributors: 2, Versions: 4 } } && projectDetail.Head?.Id == "c1"
    && projectDetail.Project.CreatedAt is null && projectDetail.Project.Reservation?.Username == "kalvin",
    "Project overview must use the server summary, members and head commit without inventing values.");
Assert(projectDetail!.Commits[0].ShareUri?.Host == "drive.example.com" && projectDetail.Commits[1].ShareUri is null
    && ProjectsModel.SafeShareUri("https://user:pass@example.com/x") is null && ProjectsModel.SafeShareUri("file:///C:/x.mfa") is null
    && ProjectsModel.SafeShareUri("http://example.com/x") is not null && ProjectsModel.SafeShareUri("https://example.com/" + new string('a', 2100)) is null,
    "Share links must be absolute http(s) without credentials before they can be opened.");
Assert(ProjectsModel.RoleKey("owner") == "Projects_RoleOwner" && ProjectsModel.RoleKey("admin") == "Projects_RoleSiteAdmin" && ProjectsModel.RoleKey("member") == "Projects_RoleMember"
    && ProjectsModel.VersionLabel("", "3f870070-aaaa") == "3f87007" && ProjectsModel.VersionLabel("v0.4.8β", "x") == "v0.4.8β",
    "Member roles and version labels must match the web project page.");

var commitDetail = ProjectsModel.ParseCommitDetail(Json("""
{"id":"c1","projectId":"p1","projectName":"Sonic Nightmare","title":"Newest","changes":"fixes","url":"https://drive.example.com/f","version":"v2","createdAt":"2026-09-19T14:24:00.000Z","author":{"username":"kalvin"},"baseSummary":{"id":"c0","title":"Older","version":"","createdAt":"2026-09-18T01:00:00.000Z"}}
"""));
Assert(commitDetail is { Base.Id: "c0", Commit.ProjectId: "p1" } && ProjectsModel.ParseCommitDetail(Json("""{"id":"c1","title":"x","baseSummary":null}""")) is { Base: null }
    && ProjectsModel.ParseCommitDetail(Json("""{"id":"c1"}""")) is null,
    "Commit details must carry the same-project base summary or report an initial version.");

var commitPayload = ProjectsModel.CommitsPayload("p1", new CommitFilter("  bug  ", 30, true), 60)!;
Assert(commitPayload["projectId"]!.GetValue<string>() == "p1" && commitPayload["q"]!.GetValue<string>() == "bug" && commitPayload["days"]!.GetValue<int>() == 30
    && commitPayload["latest"]!.GetValue<int>() == 1 && commitPayload["offset"]!.GetValue<int>() == 60 && commitPayload["limit"]!.GetValue<int>() == ProjectsModel.PageSize
    && ProjectsModel.CommitsPayload("p1", new CommitFilter("", 5, false), 0) is { } noDays && !noDays.ContainsKey("days") && !noDays.ContainsKey("latest") && !noDays.ContainsKey("q")
    && ProjectsModel.CommitsPayload("../admin", CommitFilter.None, 0) is null && ProjectsModel.ProjectPayload("a b") is null && ProjectsModel.CommitPayload("c1") is not null
    && ProjectsModel.ListPayload(new string('q', 200), 9999)["q"]!.GetValue<string>().Length == 150 && ProjectsModel.ListPayload(null, 9999)["offset"]!.GetValue<int>() == 5000,
    "Bridge payloads must use only valid ids and the documented filter bounds.");
foreach (var command in new[] { "projects", "project", "projectCommits", "commit" })
    Assert(BridgePolicy.IsKnownCommand(command) && !BridgePolicy.IsWrite(command), $"Projects must use only the read command {command}.");

var groups = ProjectsModel.GroupByDay(projectDetail.Commits, TimeZoneInfo.Utc);
Assert(groups.Count == 2 && groups[0].Day == new DateOnly(2026, 9, 19) && groups[1].Commits.Count == 1
    && ProjectsModel.FormatDay(new DateOnly(2026, 9, 19), "en-US") == "September 19, 2026" && ProjectsModel.FormatDay(new DateOnly(2026, 9, 19), "ja-JP") == "2026年9月19日"
    && ProjectsModel.FormatDay(null, "en-US") == string.Empty,
    "Commit lists must be grouped by local day like the web history timeline.");
Assert(ProjectsModel.ErrorFor(new BridgeResult("r", 403, false, null, null, "PROJECT_FORBIDDEN", null, false, BridgeOutcome.None)) == ProjectsError.NoAccess
    && ProjectsModel.ErrorFor(new BridgeResult("r", 404, false, null, null, "NOT_FOUND", null, false, BridgeOutcome.None)) == ProjectsError.NoAccess
    && ProjectsModel.ErrorFor(new BridgeResult("r", 403, false, null, null, "NOT_APPROVED", null, false, BridgeOutcome.None)) == ProjectsError.PendingApproval
    && ProjectsModel.ErrorFor(new BridgeResult("r", 401, false, null, null, "UNAUTHORIZED", null, false, BridgeOutcome.None)) == ProjectsError.SessionEnded
    && ProjectsModel.ErrorFor(new BridgeResult("r", 503, false, null, null, "MAINTENANCE", null, false, BridgeOutcome.None)) == ProjectsError.Maintenance
    && ProjectsModel.ErrorFor(BridgeResult.HostFailure("r", "TIMEOUT", true, BridgeOutcome.None)) == ProjectsError.Connection,
    "Projects errors must map to connection, session, approval, no-access, maintenance and rate-limit states.");

var projectsCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "ProjectsView.cs")) + File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "ProjectsView.Project.cs"));
var projectKeys = System.Text.RegularExpressions.Regex.Matches(projectsCode + File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Common", "PageParts.cs")), "\"(Projects_[A-Za-z0-9]+)\"")
    .Select(m => m.Groups[1].Value).Concat(new[] { "Projects_Last7Days", "Projects_Last30Days", "Projects_Last90Days", "Projects_RoleOwner", "Projects_RoleSiteAdmin", "Projects_RoleMember" })
    .Distinct().ToList();
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    foreach (var key in projectKeys) Assert(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"Missing projects string {key} in {locale}");
}
Assert(!projectsCode.Contains("startReservation", StringComparison.Ordinal) && !projectsCode.Contains("publishCommit", StringComparison.Ordinal)
    && !projectsCode.Contains("cancelReservation", StringComparison.Ordinal)
    && !projectsCode.Contains("manageProject", StringComparison.Ordinal) && !projectsCode.Contains("createProject", StringComparison.Ordinal)
    && projectsCode.Contains("_p.StorageNote()", StringComparison.Ordinal)
    && projectsCode.Contains("ProjectsModel.SafeShareUri(uri.AbsoluteUri) is not { } safe", StringComparison.Ordinal)
    && projectsCode.Contains("Launcher.LaunchUriAsync(safe)", StringComparison.Ordinal),
    "The project list/overview code stays read-only (writes live only in ProjectsView.Work.cs), shows the storage note with share links and opens only validated links in the browser.");
Assert(mainCode.Contains("if (page == NativePage.Projects) return CreateProjectsPage();", StringComparison.Ordinal)
    && mainCode.Contains("if (CurrentStackPage?.TryGoBack() == true) return;", StringComparison.Ordinal)
    && dashboardCode.Contains("_openProject(project.Id, project.Name)", StringComparison.Ordinal),
    "Projects must be the native page, Back must walk its screens and the Dashboard must open projects natively.");
Assert(projectsCode.Contains("var titleRow = new InlineWrapPanel();", StringComparison.Ordinal)
    && projectsCode.Contains("var title = new InlineWrapPanel { HorizontalSpacing = 12 };", StringComparison.Ordinal)
    && projectsCode.Contains("count == 1 ? L(\"Projects_CommitCountOne\")", StringComparison.Ordinal),
    "Long project names must wrap with their label, and one commit must read in the singular.");
Assert(projectsCode.Contains("var meta = new InlineWrapPanel { HorizontalSpacing = 8 };", StringComparison.Ordinal)
    && projectsCode.Contains("var meta = new InlineWrapPanel();", StringComparison.Ordinal)
    && !projectsCode.Contains("var meta = new StackPanel", StringComparison.Ordinal),
    "Project list and commit meta lines must wrap in a narrow window instead of clipping.");

// ----- Commit history (read-only, author-scoped on the server) -----
var historyPage = HistoryModel.ParseHistory(Json("""
{"items":[{"id":"c1","projectId":"p1","projectName":"Sonic Nightmare","title":"Newest","changes":"fixes","url":"https://drive.example.com/f","version":"v2","createdAt":"2026-09-19T14:24:00.000Z","author":{"username":"azunel"}},
          {"id":"bad id","title":"dropped"}],
 "summary":{"total":12,"projects":3,"contributors":1,"versions":5}}
"""), Json("""{"limit":30,"offset":0,"nextOffset":30,"total":12}"""));
Assert(historyPage is { Commits.Count: 1, Total: 12, NextOffset: 30, Summary: { Total: 12, Projects: 3, Versions: 5 } } && historyPage.Commits[0].ProjectName == "Sonic Nightmare",
    "History must use the server items, paging and summary counts.");
Assert(HistoryModel.ParseHistory(Json("""{"items":[],"summary":{"total":1,"projects":1,"versions":"x"}}"""), null) is { Summary: null }
    && HistoryModel.ParseHistory(Json("""{"items":[],"summary":{}}"""), null) is { Summary: null },
    "A partial or malformed history summary must show no counts instead of zeros.");
Assert(HistoryModel.ParseHistory(Json("""{"items":[]}"""), null) is { Summary: null, Total: 0, NextOffset: null }
    && HistoryModel.ParseHistory(Json("[]"), null) is null && HistoryModel.ParseHistory(Json("""{"items":{}}"""), null) is null,
    "History without a summary shows no counts, and unexpected shapes must not render.");
var historyPayload = HistoryModel.HistoryPayload(new CommitFilter(" fix ", 7, true, "p1"), 30);
Assert(historyPayload["project"]!.GetValue<string>() == "p1" && historyPayload["q"]!.GetValue<string>() == "fix" && historyPayload["days"]!.GetValue<int>() == 7
    && historyPayload["latest"]!.GetValue<int>() == 1 && historyPayload["offset"]!.GetValue<int>() == 30 && historyPayload["limit"]!.GetValue<int>() == ProjectsModel.PageSize
    && !HistoryModel.HistoryPayload(new CommitFilter("", 0, false, "../x"), 0).ContainsKey("project") && !HistoryModel.HistoryPayload(CommitFilter.None, 0).ContainsKey("project")
    && !historyPayload.ContainsKey("author") && !historyPayload.ContainsKey("projectId")
    && HistoryModel.ProjectOptionsPayload()["limit"]!.GetValue<int>() == HistoryModel.ProjectOptionLimit,
    "History payloads must send only valid project ids and never an author (the server scopes it).");
Assert(new CommitFilter("", 0, false, "p1").IsActive && !CommitFilter.None.IsActive
    && HistoryModel.ProjectOptions(projectList) is { Count: 2 } historyOptions && historyOptions[0].Id == "62cbdc9e-1" && HistoryModel.ProjectOptions(null).Count == 0,
    "The project filter lists accessible projects and counts as an active filter.");
Assert(BridgePolicy.IsKnownCommand("history") && !BridgePolicy.IsWrite("history"), "Commit history must use only the read command history.");
var historyCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "History", "ProjectsView.History.cs"));
Assert(mainCode.Contains("if (page == NativePage.CommitHistory) return CreateCommitHistoryPage();", StringComparison.Ordinal)
    && mainCode.Contains("ProjectsRoot.History", StringComparison.Ordinal)
    && mainCode.Contains("NativePage.CommitHistory => _commitHistory", StringComparison.Ordinal)
    && historyCode.Contains("\"history\"", StringComparison.Ordinal) && historyCode.Contains("showProject: true", StringComparison.Ordinal)
    && historyCode.Contains("if (page.Summary is not { } summary) return;", StringComparison.Ordinal)
    && historyCode.Contains("onReload: () => stats.Visibility = Visibility.Collapsed", StringComparison.Ordinal)
    && historyCode.Contains("\"\\uE72C\"", StringComparison.Ordinal) && !historyCode.Contains(", \"\", ", StringComparison.Ordinal)
    && projectsCode.Contains("private FrameworkElement BuildCommitTimeline(", StringComparison.Ordinal)
    && !historyCode.Contains("Write", StringComparison.Ordinal) && !historyCode.Contains("startReservation", StringComparison.Ordinal),
    "Commit history must be the native page on the shared commit timeline, show only server counts and stay read-only.");
var historyKeys = System.Text.RegularExpressions.Regex.Matches(historyCode + projectsCode, "\"(History_[A-Za-z0-9]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
Assert(historyKeys.Count >= 10, "History strings must be found in the source.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    foreach (var key in historyKeys) Assert(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"Missing history string {key} in {locale}");
}

// ----- Approval-waiting screen and page access -----
var pendingUser = new AuthenticatedUser("newbie", null, "pending", "member", false);
var approvedMember = pendingUser with { Status = "approved" };
var approvedAdmin = approvedMember with { Role = "admin" };
Assert(NativePageCatalog.StartPage(pendingUser) == NativePage.AwaitingApproval && NativePageCatalog.StartPage(approvedMember) == NativePage.Dashboard,
    "A pending account must start on the approval screen, an approved one on the Dashboard.");
Assert(new[] { NativePage.AwaitingApproval, NativePage.ServerMaintenance, NativePage.AppSettings, NativePage.VersionInfo }.All(p => NativePageCatalog.IsAvailable(p, pendingUser))
    && new[] { NativePage.Dashboard, NativePage.Projects, NativePage.CommitHistory, NativePage.ProfileSettings, NativePage.Administration }.All(p => !NativePageCatalog.IsAvailable(p, pendingUser)),
    "A pending account may open only the approval screen, Server maintenance (announcements), App settings and Version info (like the web).");
Assert(!NativePageCatalog.IsAvailable(NativePage.AwaitingApproval, approvedMember) && NativePageCatalog.IsAvailable(NativePage.Dashboard, approvedMember)
    && NativePageCatalog.IsAvailable(NativePage.ServerMaintenance, approvedMember)
    && !NativePageCatalog.IsAvailable(NativePage.Administration, approvedMember) && NativePageCatalog.IsAvailable(NativePage.Administration, approvedAdmin)
    && !NativePageCatalog.IsAvailable(NativePage.Administration, approvedAdmin with { Status = "pending" })
    && !NativePageCatalog.IsNested(NativePage.ServerMaintenance) && NativePageCatalog.IsNested(NativePage.Administration),
    "Approved accounts get the workspace and Server maintenance; administration stays admin-only and requires approval.");
BridgeResult Session(string json) => new("r", 200, true, Json(json), null, null, null, false, BridgeOutcome.None);
var approvedCheck = ApprovalModel.Check(Session("""{"user":{"id":"u9","username":"Newbie","status":"approved","role":"member","avatar":null,"projectManager":true},"maintenance":false}"""), pendingUser);
Assert(approvedCheck.Check == ApprovalCheck.Approved && approvedCheck.User is { Status: "approved", Role: "member", ProjectManager: true, Username: "newbie" },
    "Refresh must switch to the workspace when the same account is approved (camelCase projectManager from /api/v1/session).");
Assert(ApprovalModel.Check(Session("""{"user":{"username":"newbie","status":"pending","role":"member","projectManager":false}}"""), pendingUser) is { Check: ApprovalCheck.StillWaiting, User.Status: "pending" }
    && ApprovalModel.Check(Session("""{"user":null}"""), pendingUser).Check == ApprovalCheck.SessionEnded
    && ApprovalModel.Check(Session("""{"user":{"username":"someone-else","status":"approved","role":"member"}}"""), pendingUser).Check == ApprovalCheck.SessionEnded
    && ApprovalModel.Check(Session("""{"user":{"username":"newbie","status":"approved","role":"owner"}}"""), pendingUser).Check == ApprovalCheck.Unexpected
    && ApprovalModel.Check(Session("""{"nothing":true}"""), pendingUser).Check == ApprovalCheck.Unexpected,
    "Still pending stays on the screen; a missing or different user is an ended session; unknown shapes never approve.");
Assert(ApprovalModel.Check(new BridgeResult("r", 401, false, null, null, "UNAUTHORIZED", null, false, BridgeOutcome.None), pendingUser).Check == ApprovalCheck.SessionEnded
    && ApprovalModel.Check(BridgeResult.HostFailure("r", "BRIDGE_UNAVAILABLE", true, BridgeOutcome.None), pendingUser).Check == ApprovalCheck.Connection
    && ApprovalModel.Check(new BridgeResult("r", 429, false, null, null, "RATE_LIMIT", null, true, BridgeOutcome.None), pendingUser).Check == ApprovalCheck.RateLimited,
    "Approval check failures must map to session, connection and rate-limit states.");

// ----- Server maintenance: state + announcements (read-only; readable by pending accounts) -----
Assert(MaintenanceModel.ParseState(Json("""{"active":true,"startedAt":"2026-09-27T01:00:00.000Z","available":true}""")) is { Active: true, Available: true, StartedAt: not null }
    && MaintenanceModel.ParseState(Json("""{"active":false,"startedAt":null,"available":false}""")) is { Active: false, Available: false, StartedAt: null }
    && MaintenanceModel.ParseState(Json("""{"active":"yes","available":true}""")) is null && MaintenanceModel.ParseState(Json("""{"active":false}""")) is null
    && MaintenanceModel.ParseState(Json("[]")) is null,
    "The maintenance state must come only from boolean server fields.");
Assert(MaintenanceModel.ShowsLockNote(approvedMember) && MaintenanceModel.ShowsLockNote(pendingUser) && !MaintenanceModel.ShowsLockNote(approvedAdmin)
    && !MaintenanceModel.ShowsLockNote(pendingUser with { Status = "rejected" }) && !MaintenanceModel.ShowsLockNote(pendingUser with { Status = "suspended" }),
    "The member lock note (announcements stay readable) is only for accounts that can read announcements, never for admins.");
var announcementPage = MaintenanceModel.ParseList(Json("""
[{"id":"a1","title":"Maintenance finished","version":"v0.19.0","details":"Line one\r\n\r\n  Line two","startedAt":"2026-09-20T10:00:00.000Z","endedAt":"2026-09-20T11:00:00.000Z","createdAt":"2026-09-20T11:00:01.000Z"},
 {"id":"a2","title":"No end","version":"","details":"","startedAt":null,"endedAt":null,"createdAt":"2026-09-18T09:00:00.000Z"},
 {"id":"bad id","title":"dropped"},{"id":"a4","title":""}]
"""), Json("""{"limit":30,"offset":0,"nextOffset":null,"total":2}"""));
Assert(announcementPage is { Items.Count: 2, Total: 2, NextOffset: null } && announcementPage.Items[0].Details == "Line one\r\n\r\n  Line two"
    && announcementPage.Items[0].DisplayDate == DateTimeOffset.Parse("2026-09-20T11:00:00Z") && announcementPage.Items[1].DisplayDate == DateTimeOffset.Parse("2026-09-18T09:00:00Z"),
    "Announcements must parse the server list, drop invalid rows and date by the maintenance end (creation as fallback).");
Assert(MaintenanceModel.ParseList(Json("{}"), null) is null && MaintenanceModel.ParseList(Json("[]"), null) is { Items.Count: 0, NextOffset: null },
    "Unexpected announcement shapes must not render; an empty list is valid.");
Assert(MaintenanceModel.Preview("- One\r\n- Two  \r\n\r\n\r\n\r\nEnd", 120) == "- One\n- Two\n\nEnd" && MaintenanceModel.Preview(new string('x', 130), 120) == new string('x', 120) + "…",
    "List previews keep the details' line breaks like the web (blank-line runs reduced) and cut with an ellipsis.");
var announcementPayload = MaintenanceModel.ListPayload(30);
Assert(announcementPayload.Count == 2 && announcementPayload["limit"]!.GetValue<int>() == MaintenanceModel.PageSize && announcementPayload["offset"]!.GetValue<int>() == 30
    && MaintenanceModel.ListPayload(99999)["offset"]!.GetValue<int>() == 5000,
    "Announcements send only limit/offset within the server bounds.");
Assert(MaintenanceModel.ErrorFor(new BridgeResult("r", 403, false, null, null, "NOT_APPROVED", null, false, BridgeOutcome.None)) == MaintenanceError.NotApproved
    && MaintenanceModel.ErrorFor(new BridgeResult("r", 401, false, null, null, "UNAUTHORIZED", null, false, BridgeOutcome.None)) == MaintenanceError.SessionEnded
    && MaintenanceModel.ErrorFor(BridgeResult.HostFailure("r", "BRIDGE_TIMEOUT", true, BridgeOutcome.None)) == MaintenanceError.Connection,
    "Maintenance page errors must map to the page states.");
Assert(new[] { "announcements", "maintenance", "session" }.All(c => BridgePolicy.IsKnownCommand(c) && !BridgePolicy.IsWrite(c)),
    "The approval screen and Server maintenance must use only read commands.");
var announcementsCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Maintenance", "MaintenanceView.cs"));
var announcementCardCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Common", "AnnouncementCard.cs"));
var approvalCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Approval", "ApprovalView.cs"));
Assert(mainCode.Contains("NavigateTo(NativePageCatalog.StartPage(_user), false);", StringComparison.Ordinal)
    && mainCode.Contains("if (!NativePageCatalog.IsAvailable(page, _user)) return;", StringComparison.Ordinal)
    && mainCode.Contains("NativePage.ServerMaintenance => _maintenance", StringComparison.Ordinal)
    && mainCode.Contains("if (page == NativePage.ServerMaintenance) return CreateMaintenancePage();", StringComparison.Ordinal)
    && mainCode.Contains("_bridge.RequestAsync(\"session\")", StringComparison.Ordinal)
    && mainCode.Contains("NotificationsEmpty.Text = L(\"NotConnected\");", StringComparison.Ordinal)
    && mainCode.Contains("NotificationsHeader.Text = L(\"Notifications\");", StringComparison.Ordinal)
    && !mainCode.Contains("AnnouncementMenu", StringComparison.Ordinal) && !mainXaml.Contains("Announcement", StringComparison.Ordinal)
    && mainCode.Contains("Navigation.SelectedItem = match;", StringComparison.Ordinal)
    && mainCode.Contains("Cast<NativePage?>()", StringComparison.Ordinal)
    && mainCode[mainCode.IndexOf("private void OnApproved", StringComparison.Ordinal)..].Contains("if (_signingOut) return;", StringComparison.Ordinal),
    "MainWindow must start on the account's start page, refuse unavailable pages (also from search), keep the bell for notifications (not connected), host announcements on Server maintenance and ignore a late approval during sign-out.");
Assert(approvalCode.Contains("ApprovalModel.Check(", StringComparison.Ordinal) && approvalCode.Contains("\"Pending_Heading\"", StringComparison.Ordinal)
    && announcementsCode.Contains("\"announcements\"", StringComparison.Ordinal) && announcementsCode.Contains("\"maintenance\"", StringComparison.Ordinal)
    && !announcementsCode.Contains("startMaintenance", StringComparison.Ordinal) && !announcementsCode.Contains("endMaintenance", StringComparison.Ordinal)
    && !announcementsCode.Contains("Write", StringComparison.Ordinal) && !approvalCode.Contains("Write", StringComparison.Ordinal),
    "The approval screen and Server maintenance must stay read-only (no start/complete maintenance controls).");
Assert(announcementsCode.Contains("AnnouncementCard.Row", StringComparison.Ordinal)
    && announcementsCode.Contains("AnnouncementCard.Detail", StringComparison.Ordinal)
    && announcementCardCode.Contains("MaintenanceModel.Preview", StringComparison.Ordinal),
    "Server maintenance must use the shared announcement card renderer for list and detail views.");
foreach (var source in new[] { announcementsCode, announcementCardCode, approvalCode })
{
    Assert(!System.Text.RegularExpressions.Regex.IsMatch(source, "[\uE000-\uF8FF]") && !source.Contains("GLYPH_", StringComparison.Ordinal),
        "New glyphs must be written as \\u escapes.");
}
var accessKeys = System.Text.RegularExpressions.Regex.Matches(announcementsCode + announcementCardCode + approvalCode + mainCode, "\"((?:Pending|Announcements|Maintenance)_[A-Za-z0-9]+)\"").Select(m => m.Groups[1].Value)
    .Concat(new[] { "Page_Awaiting approval", "Page_Announcements" }).Distinct().ToList();
Assert(accessKeys.Count >= 22, "Approval and maintenance strings must be found in the source.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    foreach (var key in accessKeys) Assert(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"Missing approval/announcement string {key} in {locale}");
}

// ----- Project management (read-only; approved owners and site admins) -----
Assert(NativePageCatalog.IsAvailable(NativePage.ProjectManagement, approvedMember with { ProjectManager = true })
    && NativePageCatalog.IsAvailable(NativePage.ProjectManagement, approvedAdmin)
    && !NativePageCatalog.IsAvailable(NativePage.ProjectManagement, approvedMember)
    && !NativePageCatalog.IsAvailable(NativePage.ProjectManagement, pendingUser with { ProjectManager = true }),
    "Project management is for approved project owners and site admins only, like the web Manage entry.");
var managedList = ManagementModel.ParseList(Json("""
[{"id":"p1","name":"Sonic Nightmare","ownerId":"u1","deletedAt":null,"reservation":null},
 {"id":"p2","name":"Old project","ownerId":"u1","deletedAt":"2026-09-01T00:00:00.000Z"},
 {"id":"bad id","name":"x"}]
"""), Json("""{"limit":100,"offset":0,"nextOffset":null,"total":2}"""));
Assert(managedList is { Items.Count: 2, Total: 2 } && !managedList.Items[0].Deleted && managedList.Items[1].Deleted
    && ManagementModel.Select(managedList.Items, "p2") == "p2" && ManagementModel.Select(managedList.Items, "gone") == "p1"
    && ManagementModel.Select([], null) is null && ManagementModel.ParseList(Json("{}"), null) is null,
    "The managed list keeps deleted projects (labelled) and selects the kept project or the first one, like the web.");
var managedDetail = ManagementModel.ParseDetail(Json("""
{"project":{"id":"p1","name":"Sonic Nightmare","ownerId":"u1","deletedAt":null},
 "members":[{"id":"u2","username":"bea","status":"approved","role":"member","avatar":null},{"id":"u3","username":"cid","status":"suspended","role":"member"}],
 "eligible":[{"id":"u1","username":"azunel","avatar":null},{"id":"u2","username":"bea","avatar":null}],
 "requests":[{"id":"r1","kind":"delete","status":"pending","reason":" No longer needed ","requester":"azunel","createdAt":"2026-09-20T10:00:00.000Z"},{"id":"r2","kind":"weird","status":"odd"}],
 "logs":[{"id":1,"kind":"member_added","actor":"azunel","target":"u2","createdAt":"2026-09-19T10:00:00.000Z"},{"id":2,"kind":"request_delete","actor":"azunel","target":"r1"},{"id":3,"kind":"member_removed","target":"u404"}],
 "links":[{"id":"l1","guildId":"123","channelId":"456","channelName":"general","locale":"en","enabled":true},{"id":"l2","guildId":"1","channelId":"2","channelName":null,"enabled":false}],
 "reservation":{"userId":"u2","username":"bea","avatar":null,"startedAt":"2026-09-21T08:00:00.000Z"},
 "discord":{"configured":false,"approvals":[]}}
"""));
Assert(managedDetail is { Members.Count: 2, Requests.Count: 2, Events.Count: 3, Links.Count: 2, DiscordConfigured: false, Reservation.Username: "bea" }
    && managedDetail.Requests[0].Reason == "No longer needed" && managedDetail.Links[0].Enabled && !managedDetail.Links[1].Enabled && managedDetail.Links[1].ChannelName == "",
    "The manage detail must parse members, requests, activity, Discord links and the reservation from the server DTO.");
Assert(ManagementModel.Owner(managedDetail!) == (ManagedOwnerKind.Account, "azunel")
    && ManagementModel.Owner(managedDetail! with { Project = managedDetail.Project with { OwnerId = null } }).Kind == ManagedOwnerKind.SiteManaged
    && ManagementModel.Owner(managedDetail! with { Project = managedDetail.Project with { OwnerId = "u999" } }).Kind == ManagedOwnerKind.Unknown,
    "The project administrator is resolved from members/approved accounts; no owner means site-managed, an unresolvable one is unknown.");
Assert(ManagementModel.TargetName(managedDetail!.Events[0], managedDetail) == "bea" && ManagementModel.TargetName(managedDetail.Events[1], managedDetail) == ""
    && ManagementModel.TargetName(managedDetail.Events[2], managedDetail) == "",
    "Only member events name their target account; request ids and unknown accounts are not shown.");
Assert(ManagementModel.ParseDetail(Json("""{"project":{"id":"p1","name":"P"},"reservation":{"userId":"u2","username":null,"startedAt":"2026-09-21T08:00:00.000Z"}}""")) is { Reservation: null, Members.Count: 0 },
    "A reservation without a holder name must not render a nameless holder; missing sections parse as empty.");
var manyAccounts = "[" + string.Join(",", Enumerable.Range(0, 300).Select(i => $$"""{"id":"a{{i}}","username":"user{{i}}"}""")) + "]";
Assert(ManagementModel.Owner(ManagementModel.ParseDetail(Json($$"""{"project":{"id":"p1","name":"P","ownerId":"a299"},"eligible":{{manyAccounts}}}"""))!) == (ManagedOwnerKind.Account, "user299"),
    "The owner must stay resolvable on sites with more than 200 approved accounts.");
Assert(ManagementModel.StatusKey("cancelled") == "Manage_Status_cancelled" && ManagementModel.StatusKey("odd") is null
    && ManagementModel.KindKey("discord") == "Manage_Kind_discord" && ManagementModel.KindKey("weird") is null
    && ManagementModel.EventKey("project_restored") == "Manage_Event_project_restored" && ManagementModel.EventKey("x") is null
    && ManagementModel.DetailPayload("../x") is null && ManagementModel.DetailPayload("p1")!["projectId"]!.GetValue<string>() == "p1"
    && ManagementModel.ListPayload()["limit"]!.GetValue<int>() == ManagementModel.ProjectLimit,
    "Labels exist only for known kinds/statuses (others show as sent); payloads send only valid ids.");
Assert(new[] { "manageProjects", "manageProject" }.All(c => BridgePolicy.IsKnownCommand(c) && !BridgePolicy.IsWrite(c)),
    "Project management must use only the manage read commands.");
var managementCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Management", "ManagementView.cs"));
Assert(mainCode.Contains("if (page == NativePage.ProjectManagement) return CreateManagementPage();", StringComparison.Ordinal)
    && mainXaml.Contains("Tag=\"ProjectManagement\"", StringComparison.Ordinal)
    && managementCode.Contains("\"manageProjects\"", StringComparison.Ordinal) && managementCode.Contains("\"manageProject\"", StringComparison.Ordinal)
    && new[] { "addMember", "removeMember", "releaseReservation", "requestProjectChange", "linkProjectDiscord", "disableProjectDiscord", "assignProjectOwner" }.All(w => managementCode.Contains($"\"{w}\"", StringComparison.Ordinal))
    && !managementCode.Contains("Manage_ReadOnlyNote", StringComparison.Ordinal)
    && !System.Text.RegularExpressions.Regex.IsMatch(managementCode, "[\uE000-\uF8FF]") && !managementCode.Contains("GLYPH_", StringComparison.Ordinal),
    "Project management must be the native page with the web's management writes and escaped glyphs.");
Assert(managementCode.Contains("if (!_gate.TryEnter())", StringComparison.Ordinal) && managementCode.Contains("ManageWriteModel.Explain(result, createsRecord)", StringComparison.Ordinal)
    && managementCode.Contains("LoadDetailAsync(++_generation, keepContent: true)", StringComparison.Ordinal)
    && managementCode.Contains("DefaultButton = ContentDialogButton.Close", StringComparison.Ordinal)
    && System.Text.RegularExpressions.Regex.Matches(managementCode, "await ConfirmAsync\\(").Count == 3
    && System.Text.RegularExpressions.Regex.IsMatch(mainCode, @"new ManagementView\([^;]*_writeGate,\s*OnWorkChanged\);"),
    "Management writes share the app-wide write gate, confirm remove/release/stop (Cancel default), reload the project and explain the answer.");
var projectsStackCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "ProjectsView.cs"))
    + File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "ProjectsView.Work.cs"))
    + File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "ProjectsView.Project.cs"));
Assert(projectsStackCode.Contains("foreach (var screen in _stack.Where(s => s.Kind == ScreenKind.Project)) screen.WorkStale = true;", StringComparison.Ordinal)
    && projectsStackCode.Contains("if (Current is { Kind: ScreenKind.Project, WorkStale: true, Content: not null } project) return ReloadProjectAsync(project);", StringComparison.Ordinal)
    && projectsStackCode.Contains("screen.WorkStale = false;", StringComparison.Ordinal),
    "An open project reloads when shown again after a write on another page (e.g. a reservation released in Project management).");
var manageKeys = System.Text.RegularExpressions.Regex.Matches(managementCode + File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Management", "ManagementModel.cs"))
        + File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Management", "ManageWriteModel.cs")), "\"(Manage_[A-Za-z0-9_]+)\"").Select(m => m.Groups[1].Value)
    .Where(k => !k.EndsWith("_", StringComparison.Ordinal))
    .Concat(new[] { "Page_Project management" })
    .Concat(new[] { "pending", "approved", "rejected", "cancelled", "suspended" }.Select(s => "Manage_Status_" + s))
    .Concat(new[] { "delete", "restore", "discord" }.Select(s => "Manage_Kind_" + s))
    .Concat(new[] { "member_added", "member_removed", "owner_assigned", "reservation_released", "request_delete", "request_restore", "request_discord",
        "request_approved", "request_rejected", "discord_linked", "discord_disabled", "project_deleted", "project_restored" }.Select(s => "Manage_Event_" + s))
    .Distinct().ToList();
Assert(manageKeys.Count >= 90, "Project management strings must be found in the source.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    foreach (var key in manageKeys) Assert(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"Missing project management string {key} in {locale}");
}

// ----- Project management writes (v0.14.1) -----
var managedWithAdmin = ManagementModel.ParseDetail(Json("""
{"project":{"id":"p1","name":"Sonic Nightmare","ownerId":"u1","deletedAt":null},
 "members":[{"id":"u1","username":"azunel","status":"approved","role":"member"},{"id":"u2","username":"bea","status":"approved","role":"member"},{"id":"u5","username":"root","status":"approved","role":"admin"}],
 "eligible":[{"id":"u1","username":"azunel"},{"id":"u2","username":"bea"},{"id":"u4","username":"dan"},{"id":"bad id","username":"x"}],
 "reservation":{"userId":"u2","username":"bea","startedAt":"2026-09-21T08:00:00.000Z"}}
"""))!;
var deletedManaged = managedWithAdmin with { Project = managedWithAdmin.Project with { Deleted = true } };
Assert(managedWithAdmin.Members[2].Role == "admin" && managedWithAdmin.Eligible!.Count == 3
    && ManageWriteModel.AddCandidates(managedWithAdmin).Select(a => a.Username).SequenceEqual(new[] { "dan" })
    && ManageWriteModel.AddCandidates(deletedManaged).Count == 0,
    "Add member offers approved accounts that are not members yet (valid ids only), and nothing on a deleted project, like the web.");
Assert(!ManageWriteModel.CanRemove(managedWithAdmin.Members[0], managedWithAdmin) && ManageWriteModel.CanRemove(managedWithAdmin.Members[1], managedWithAdmin)
    && !ManageWriteModel.CanRemove(managedWithAdmin.Members[2], managedWithAdmin) && !ManageWriteModel.CanRemove(managedWithAdmin.Members[1], deletedManaged)
    && ManageWriteModel.CanRelease(managedWithAdmin) && !ManageWriteModel.CanRelease(deletedManaged) && !ManageWriteModel.CanRelease(managedWithAdmin with { Reservation = null }),
    "Remove is offered except for the project administrator and site admins (OWNER_PROTECTED); release only for an active reservation on an active project.");
Assert(ManageWriteModel.CanRequest(managedWithAdmin, "Azunel") && !ManageWriteModel.CanRequest(managedWithAdmin, "bea")
    && !ManageWriteModel.CanRequest(managedWithAdmin with { Project = managedWithAdmin.Project with { OwnerId = null } }, "azunel")
    && ManageWriteModel.RequestKind(managedWithAdmin) == "delete" && ManageWriteModel.RequestKind(deletedManaged) == "restore",
    "Deletion/restoration requests are only for the project's own administrator (OWNER_ONLY) and match the deleted state (REQUEST_CHANGED).");
var requestPayload = ManageWriteModel.RequestPayload("p1", "delete", "  Done with it  ")!;
Assert(requestPayload["reason"]!.GetValue<string>() == "Done with it" && requestPayload["kind"]!.GetValue<string>() == "delete"
    && ManageWriteModel.RequestPayload("p1", "delete", "   ") is null && ManageWriteModel.RequestPayload("p1", "delete", new string('r', 1001)) is null
    && ManageWriteModel.RequestPayload("p1", "discord", "x") is null && ManageWriteModel.RequestPayload("../p", "delete", "x") is null,
    "Requests send a trimmed reason of 1–1000 characters and only delete/restore.");
Assert(ManageWriteModel.AddMemberPayload("p1", "u4")!["user"]!.GetValue<string>() == "u4" && ManageWriteModel.AddMemberPayload("p1", "bad id") is null
    && ManageWriteModel.RemoveMemberPayload("p1", "u2")!["userId"]!.GetValue<string>() == "u2" && ManageWriteModel.RemoveMemberPayload("p1", null) is null
    && ManageWriteModel.ReleasePayload("p1")!.Count == 1 && ManageWriteModel.DisableDiscordPayload("p1", "l1")!["linkId"]!.GetValue<string>() == "l1"
    && ManageWriteModel.AssignOwnerPayload("p1", null) is { } siteManagedPayload && siteManagedPayload.ContainsKey("user") && siteManagedPayload["user"] is null
    && ManageWriteModel.AssignOwnerPayload("p1", "u4")!["user"]!.GetValue<string>() == "u4" && ManageWriteModel.AssignOwnerPayload("p1", "../u") is null,
    "Management payloads carry only valid ids; a null owner makes the project site-managed.");
var discordDraft = new DiscordDraft(" 123456789012345678 ", "223456789012345678", "ja", " Team channel ", true);
var discordPayload = ManageWriteModel.DiscordLinkPayload("p1", discordDraft)!;
Assert(ManageWriteModel.Validate(discordDraft) == DiscordFieldError.None && discordPayload["guild"]!.GetValue<string>() == "123456789012345678"
    && discordPayload["consent"]!.GetValue<bool>() && discordPayload["reason"]!.GetValue<string>() == "Team channel" && discordPayload["locale"]!.GetValue<string>() == "ja"
    && ManageWriteModel.Validate(new DiscordDraft("123", "abc", "en", "", false)) == (DiscordFieldError.GuildInvalid | DiscordFieldError.ChannelInvalid | DiscordFieldError.ReasonMissing | DiscordFieldError.ConsentMissing)
    && ManageWriteModel.DiscordLinkPayload("p1", discordDraft with { Consent = false }) is null && ManageWriteModel.DiscordLinkPayload("p1", discordDraft with { Locale = "fr" }) is null
    && ManageWriteModel.DefaultLocale("ja-JP") == "ja" && ManageWriteModel.DefaultLocale("en-US") == "en",
    "Discord destinations need 17–20 digit ids, a reason, consent and en/ja, as the server requires.");
Assert(ManageWriteModel.CanStopDiscord(new ManagedLink("l1", "general", "1", "2", true)) && !ManageWriteModel.CanStopDiscord(new ManagedLink("l2", "", "1", "2", false))
    && ManageWriteModel.CanLinkDiscord(managedWithAdmin) && !ManageWriteModel.CanLinkDiscord(deletedManaged),
    "Stop integration is offered for active destinations; new destinations only on active projects.");
Assert(ManageWriteModel.IsPending(new BridgeResult("r", 200, true, Json("""{"ok":true,"pending":true}"""), null, null, null, false, BridgeOutcome.Applied))
    && !ManageWriteModel.IsPending(new BridgeResult("r", 200, true, Json("""{"ok":true}"""), null, null, null, false, BridgeOutcome.Applied)),
    "A pending answer is explained as waiting for a site administrator.");
Assert(ManageWriteModel.Explain(Write(403, false, "OWNER_PROTECTED", BridgeOutcome.Rejected)) is { Kind: WriteOutcomeKind.Rejected, MessageKey: "Manage_ErrorOwnerProtected" }
    && ManageWriteModel.Explain(Write(403, false, "MEMBER_NOT_APPROVED", BridgeOutcome.Rejected)).MessageKey == "Manage_ErrorMemberNotApproved"
    && ManageWriteModel.Explain(Write(403, false, "OWNER_ONLY", BridgeOutcome.Rejected)).MessageKey == "Manage_ErrorOwnerOnly"
    && ManageWriteModel.Explain(Write(409, false, "REQUEST_CHANGED", BridgeOutcome.Rejected)).MessageKey == "Manage_ErrorRequestChanged"
    && ManageWriteModel.Explain(Write(409, false, "DISCORD_LINK_CONFLICT", BridgeOutcome.Rejected)).MessageKey == "Manage_ErrorDiscordLinkConflict"
    && ManageWriteModel.Explain(Write(403, false, "PROJECT_FORBIDDEN", BridgeOutcome.Rejected)).MessageKey == "Manage_ErrorNoAccess"
    && ManageWriteModel.Explain(Write(409, false, "PROJECT_DELETED", BridgeOutcome.Rejected)).MessageKey == "Work_ErrorProjectDeleted"
    && ManageWriteModel.Explain(Write(401, false, "UNAUTHORIZED", BridgeOutcome.Rejected)) is { SessionEnded: true, MessageKey: "Work_ErrorSession" }
    && ManageWriteModel.Explain(Write(502, false, "DISCORD_UNAVAILABLE", BridgeOutcome.Unknown), createsRecord: true) is { Kind: WriteOutcomeKind.Unknown, MessageKey: "Manage_UnknownRequest" }
    && ManageWriteModel.Explain(BridgeResult.HostFailure("r", "TIMEOUT", false, BridgeOutcome.Unknown)).MessageKey == "Manage_Unknown"
    && ManageWriteModel.Explain(Write(200, true, null, BridgeOutcome.Applied)).Kind == WriteOutcomeKind.Applied,
    "Management answers name the management codes; an unknown request/Discord write warns about a duplicate request.");
Assert(new[] { "addMember", "removeMember", "releaseReservation", "requestProjectChange", "linkProjectDiscord", "disableProjectDiscord", "assignProjectOwner" }
    .All(c => BridgePolicy.IsKnownCommand(c) && BridgePolicy.IsWrite(c)), "The management writes are allowlisted bridge writes.");

// ----- Writes: start work, cancel work, publish version -----
var reservedProject = ProjectsModel.ParseProjectList(Json("""
[{"id":"p1","name":"P","head":"c9","latest":{"id":"c9","title":"t","version":"v1"},"reservation":{"userId":"u1","username":"Azunel","base":"c9","startedAt":"2026-09-27T01:00:00.000Z"}},
 {"id":"p2","name":"Q","head":null,"latest":null,"reservation":{"userId":"u2","username":"kalvin","base":"bad id"}}]
"""), null)!.Projects;
Assert(reservedProject[0] is { HeadId: "c9", Reservation.Base: "c9" } && reservedProject[1] is { HeadId: null, Reservation.Base: null },
    "The project head and the reservation base must be parsed (invalid ids dropped) for the writes.");
var startPayload = WorkModel.StartPayload("p1", "c9")!;
Assert(startPayload["projectId"]!.GetValue<string>() == "p1" && startPayload["base"]!.GetValue<string>() == "c9"
    && WorkModel.StartPayload("p2", null)!.ContainsKey("base") && WorkModel.StartPayload("p2", null)!["base"] is null
    && WorkModel.StartPayload("../x", "c9") is null && WorkModel.CancelPayload("p1")!.Count == 1 && WorkModel.CancelPayload("bad id") is null,
    "Start work sends the current head (null for a project without versions); payloads carry only valid ids.");
var goodDraft = new PublishDraft(" Fix jumps ", " v1.2 ", " - fixed\r\n", " https://drive.example.com/f ");
var publishPayload = WorkModel.PublishPayload("p1", "c9", goodDraft)!;
Assert(WorkModel.Validate(goodDraft) == PublishFieldError.None && publishPayload["title"]!.GetValue<string>() == "Fix jumps"
    && publishPayload["version"]!.GetValue<string>() == "v1.2" && publishPayload["changes"]!.GetValue<string>() == "- fixed"
    && publishPayload["url"]!.GetValue<string>() == "https://drive.example.com/f" && publishPayload["base"]!.GetValue<string>() == "c9"
    && WorkModel.PublishPayload("p1", "c9", goodDraft with { Title = " " }) is null,
    "Publish sends the trimmed form with the reservation base, and nothing when the form is invalid.");
Assert(WorkModel.Validate(new PublishDraft("", "a<b", "", "")) == (PublishFieldError.TitleMissing | PublishFieldError.VersionInvalid | PublishFieldError.ChangesMissing | PublishFieldError.UrlMissing)
    && WorkModel.Validate(goodDraft with { Url = "ftp://x.example/f" }) == PublishFieldError.UrlInvalid
    && WorkModel.Validate(goodDraft with { Url = "https://user:pw@x.example/f" }) == PublishFieldError.UrlInvalid
    && WorkModel.Validate(goodDraft with { Title = new string('t', 151) }) == PublishFieldError.TitleTooLong
    && WorkModel.Validate(goodDraft with { Version = new string('v', 41) }) == PublishFieldError.VersionInvalid
    && WorkModel.Validate(goodDraft with { Version = "" }) == PublishFieldError.None,
    "The publish form uses the server limits: required title/changes/URL, http(s) without credentials, version ≤ 40 without < >.");
var flagged = PublishFieldError.TitleMissing | PublishFieldError.UrlInvalid;
Assert(WorkModel.StillShown(flagged, PublishFieldError.None) == PublishFieldError.None
    && WorkModel.StillShown(flagged, PublishFieldError.UrlInvalid) == PublishFieldError.UrlInvalid
    && WorkModel.StillShown(flagged, PublishFieldError.TitleTooLong | PublishFieldError.UrlMissing) == (PublishFieldError.TitleTooLong | PublishFieldError.UrlMissing)
    && WorkModel.StillShown(flagged, PublishFieldError.ChangesMissing | PublishFieldError.VersionInvalid) == PublishFieldError.None
    && WorkModel.StillShown(PublishFieldError.TooLarge, PublishFieldError.TooLarge) == PublishFieldError.TooLarge
    && WorkModel.StillShown(PublishFieldError.None, PublishFieldError.TitleMissing) == PublishFieldError.None,
    "After a failed publish, flagged fields clear once valid and fields that were fine get no new error until the next Publish.");
var workViewSource = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "ProjectsView.Work.cs"));
Assert(workViewSource.Contains("box.TextChanged += (_, _) => Recheck();", StringComparison.Ordinal)
    && workViewSource.Contains("if (shownErrors == PublishFieldError.None) CloseFormNotice();", StringComparison.Ordinal),
    "The publish form re-checks flagged fields while editing and closes the form notice once the form is valid.");
BridgeResult Write(int status, bool ok, string? code, BridgeOutcome outcome, string? details = null) =>
    new("r", status, ok, null, null, code, details is null ? null : Json(details), false, outcome);
Assert(WorkModel.Explain(Write(200, true, null, BridgeOutcome.Applied)).Kind == WriteOutcomeKind.Applied
    && WorkModel.Explain(BridgeResult.HostFailure("r", "TIMEOUT", false, BridgeOutcome.Unknown)).Kind == WriteOutcomeKind.Unknown
    && WorkModel.Explain(Write(500, false, "SERVER_ERROR", BridgeOutcome.Unknown)).Kind == WriteOutcomeKind.Unknown
    && WorkModel.Explain(BridgeResult.HostFailure("r", "BRIDGE_UNAVAILABLE", false, BridgeOutcome.Rejected)).Kind == WriteOutcomeKind.NotSent
    && WorkModel.Explain(Write(409, false, "RESERVATION_EXISTS", BridgeOutcome.Rejected, """{"reservedByYou":true}""")).MessageKey == "Work_ErrorReservedByYou"
    && WorkModel.Explain(Write(409, false, "RESERVATION_EXISTS", BridgeOutcome.Rejected, """{"reservedByYou":false}""")).MessageKey == "Work_ErrorReservedByOther"
    && WorkModel.Explain(Write(409, false, "STALE_VERSION", BridgeOutcome.Rejected, """{"head":"c10"}""")).MessageKey == "Work_ErrorStale"
    && WorkModel.Explain(Write(409, false, "VERSION_CONFLICT", BridgeOutcome.Rejected)).MessageKey == "Work_ErrorVersionConflict"
    && WorkModel.Explain(Write(409, false, "RESERVATION_MISSING", BridgeOutcome.Rejected)).MessageKey == "Work_ErrorReservationMissing"
    && WorkModel.Explain(Write(409, false, "NOT_RESERVATION_OWNER", BridgeOutcome.Rejected)).MessageKey == "Work_ErrorNotReservationOwner"
    && WorkModel.Explain(Write(403, false, "NOT_OWNER", BridgeOutcome.Rejected)).MessageKey == "Work_ErrorNotOwner"
    && WorkModel.Explain(Write(401, false, "UNAUTHORIZED", BridgeOutcome.Rejected)).SessionEnded
    && WorkModel.Explain(Write(409, false, "SOMETHING_NEW", BridgeOutcome.Rejected)).MessageKey == "Work_ErrorRejected",
    "Write results: applied, unknown (timeout/5xx), not sent, and each rejection explained by its code.");
var gate = new WriteGate();
var gateFirst = gate.TryEnter() && gate.InFlight && !gate.TryEnter();
gate.Exit();
Assert(gateFirst && !gate.InFlight && gate.TryEnter(), "The write gate admits one write at a time and opens again when it is released.");
var longJapanese = goodDraft with { Changes = new string('あ', 10_000) };
Assert(WorkModel.Validate(longJapanese) == PublishFieldError.None
    && BridgePolicy.BuildRequest("publishCommit", "r1", WorkModel.PublishPayload("p1", "c9", longJapanese)) is not null
    && WorkModel.Validate(longJapanese with { Title = new string('あ', 150), Url = "https://drive.example.com/" + new string('a', 2000) }) == PublishFieldError.None,
    "Japanese text up to the server's character limits must fit the body limit (measured as the page sends it, not \\u-escaped).");
Assert(WorkModel.Explain(BridgeResult.HostFailure("r", "TOO_LARGE", false, BridgeOutcome.Rejected)) is { Kind: WriteOutcomeKind.NotSent, MessageKey: "Work_ErrorTooLarge" }
    && WorkModel.Explain(Write(400, false, "INVALID_REQUEST", BridgeOutcome.None)).Kind == WriteOutcomeKind.Unknown,
    "An oversized request is 'not sent, too long' (not a connection problem); a result without a write outcome is unknown.");
Assert(WorkModel.StillReservedBy(reservedProject[0], "azunel") && !WorkModel.StillReservedBy(reservedProject[1], "azunel") && !WorkModel.StillReservedBy(null, "azunel"),
    "After an unknown publish, the refreshed reservation tells whether the version was likely published.");
var workCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "ProjectsView.Work.cs"));
Assert(new[] { "startReservation", "cancelReservation", "publishCommit" }.All(c => BridgePolicy.IsWrite(c) && workCode.Contains($"\"{c}\"", StringComparison.Ordinal))
    && workCode.Contains("WorkModel.Explain(await _request(", StringComparison.Ordinal)
    && System.Text.RegularExpressions.Regex.Matches(workCode, "_request\\(\"publishCommit\"").Count == 1
    && workCode.Contains("ConfirmPublishAsync", StringComparison.Ordinal) && workCode.Contains("DefaultButton = ContentDialogButton.Close", StringComparison.Ordinal)
    && workCode.Contains("PublishStorageNote()", StringComparison.Ordinal)
    && System.Text.RegularExpressions.Regex.Matches(workCode, "_gate\\.TryEnter\\(\\)").Count == 2 && System.Text.RegularExpressions.Regex.Matches(workCode, "_gate\\.Exit\\(\\);").Count == 2
    && !workCode.Contains("IsEnabled = !_gate", StringComparison.Ordinal)
    && workCode.Contains("_pendingWriteNotice = (severity, title, message, sessionEnded);", StringComparison.Ordinal)
    && workCode.Contains("_stickyNotice = notice.Severity is InfoBarSeverity.Warning or InfoBarSeverity.Error;", StringComparison.Ordinal)
    && projectsCode.Contains("ShowPendingWriteNotice();", StringComparison.Ordinal) && projectsCode.Contains("if (!_stickyNotice) _statusBar.IsOpen = false;", StringComparison.Ordinal)
    && projectsCode.Contains("if (generation != screen.LoadGeneration) return true;", StringComparison.Ordinal)
    && System.Text.RegularExpressions.Regex.Matches(mainCode, "_writeGate\\);").Count == 2
    && workCode.Contains("Style = PageParts.Res(\"DefaultContentDialogStyle\")", StringComparison.Ordinal)
    && !System.Text.RegularExpressions.Regex.IsMatch(workCode, "[\uE000-\uF8FF]") && !workCode.Contains("GLYPH_", StringComparison.Ordinal)
    && dashboardCode.Contains("_openPublish(project.Id, project.Name)", StringComparison.Ordinal)
    && mainCode.Contains("OnWorkChanged", StringComparison.Ordinal),
    "Writes send each command once (no automatic resend), confirm before publishing (Cancel is the default), show the storage note, and the Dashboard opens the publish form.");
var workKeys = System.Text.RegularExpressions.Regex.Matches(workCode + File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Projects", "WorkModel.cs")) + dashboardCode, "\"(Work_[A-Za-z0-9]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
Assert(workKeys.Count >= 40, "Write strings must be found in the source.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    foreach (var key in workKeys) Assert(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"Missing write string {key} in {locale}");
    Assert(!resource.Contains("publish from MolHub on the web", StringComparison.Ordinal), $"The owner hint must no longer say publishing is web-only: {locale}");
}

// ----- Profile settings (v0.14.2): icon and password writes -----
static byte[] PngHeader(int width, int height, int totalBytes, byte[]? signature = null, uint ihdrLength = 13, string chunkType = "IHDR")
{
    var bytes = new byte[totalBytes];
    ReadOnlySpan<byte> sig = signature ?? new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
    sig.CopyTo(bytes);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), ihdrLength);
    Encoding.ASCII.GetBytes(chunkType).CopyTo(bytes, 12);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
    return bytes;
}

// Parse: required fields, role default, avatar bounds, createdAt optional.
Assert(ProfileModel.Parse(Json("""{"username":"x","status":"approved"}""")) is null
    && ProfileModel.Parse(Json("""{"id":"u1","status":"approved"}""")) is null
    && ProfileModel.Parse(Json("""{"id":"u1","username":"x"}""")) is null
    && ProfileModel.Parse(Json("[]")) is null,
    "Profile parsing must require id, username and status; unexpected shapes must not render.");
Assert(ProfileModel.Parse(Json("""{"id":"u1","username":"x","status":"approved"}""")) is { Role: "member", CreatedAt: null, Avatar: null },
    "A missing role must default to member, like the server DTO, and createdAt/avatar are optional.");
var validSmallPng = PngHeader(1, 1, 33);
var validLargePng = PngHeader(128, 128, 100);
Assert(ProfileModel.IsAcceptableAvatar(validSmallPng) && ProfileModel.IsAcceptableAvatar(validLargePng),
    "A 1x1 and a 128x128 PNG header must both be acceptable icon sizes.");
var profileJson = Json($$"""{"id":"u1","username":"  Azunel  ","status":"Approved","role":"Admin","avatar":"{{ProfileModel.ToDataUrl(validSmallPng)}}","createdAt":"2026-09-01T00:00:00.000Z"}""");
var profileInfo = ProfileModel.Parse(profileJson);
Assert(profileInfo is { Id: "u1", Username: "Azunel", Status: "approved", Role: "admin" } && profileInfo.CreatedAt is not null
    && profileInfo.Avatar is not null && profileInfo.Avatar.SequenceEqual(validSmallPng),
    "Username must be trimmed, status/role lower-cased, and a valid avatar/createdAt parsed from the DTO.");
Assert(ProfileModel.Parse(Json("""{"id":"u1","username":"x","status":"approved","avatar":"data:image/png;base64,AAAA"}""")) is { Avatar: null }
    && ProfileModel.Parse(Json("""{"id":"u1","username":"x","status":"approved","avatar":null}""")) is { Avatar: null }
    && ProfileModel.Parse(Json("""{"id":"u1","username":"x","status":"approved","createdAt":"not-a-date"}""")) is { CreatedAt: null },
    "An unusable avatar or date must be dropped instead of failing the whole profile.");

// IsAcceptableAvatar: signature, IHDR, dimensions and byte-length bounds.
Assert(!ProfileModel.IsAcceptableAvatar(PngHeader(0, 10, 40)) && !ProfileModel.IsAcceptableAvatar(PngHeader(129, 10, 40))
    && !ProfileModel.IsAcceptableAvatar(PngHeader(10, 0, 40)) && !ProfileModel.IsAcceptableAvatar(PngHeader(10, 129, 40)),
    "Icon width/height of 0 or over 128 must be rejected.");
Assert(!ProfileModel.IsAcceptableAvatar(PngHeader(10, 10, 40, signature: new byte[] { 0, 80, 78, 71, 13, 10, 26, 10 }))
    && !ProfileModel.IsAcceptableAvatar(PngHeader(10, 10, 40, ihdrLength: 10))
    && !ProfileModel.IsAcceptableAvatar(PngHeader(10, 10, 40, chunkType: "IDAT")),
    "A wrong PNG signature, IHDR length or chunk type must be rejected.");
Assert(!ProfileModel.IsAcceptableAvatar(PngHeader(10, 10, 32)) && !ProfileModel.IsAcceptableAvatar(PngHeader(10, 10, 32_769)) && !ProfileModel.IsAcceptableAvatar(null),
    "32 bytes is below the 33-byte minimum and 32,769 is above the 32,768-byte maximum.");
Assert(ProfileModel.IsAcceptableDataUrl(ProfileModel.ToDataUrl(validSmallPng)) && !ProfileModel.IsAcceptableDataUrl("data:image/png;base64," + new string('A', 43_692))
    && !ProfileModel.IsAcceptableDataUrl(ProfileModel.PngDataPrefix + "not*base64!")
    && !ProfileModel.IsAcceptableDataUrl("data:image/jpeg;base64,QQ==") && !ProfileModel.IsAcceptableDataUrl(null),
    "The data URL prefix, character set and the 43,714-character length must all be checked before decoding.");
var nullAvatarPayload = ProfileModel.AvatarPayload(null)!;
Assert(nullAvatarPayload.ContainsKey("avatar") && nullAvatarPayload["avatar"] is null && nullAvatarPayload.ToJsonString().Contains("\"avatar\":null", StringComparison.Ordinal)
    && ProfileModel.AvatarPayload(ProfileModel.ToDataUrl(validSmallPng)) is not null && ProfileModel.AvatarPayload("data:image/png;base64,not-valid") is null,
    "Removing the icon must send an explicit avatar:null; only an acceptable data URL is otherwise sent.");

// Password validation and payload shape.
Assert(ProfileModel.ValidatePasswords(new string('a', 11), new string('b', 12)) == PasswordFieldError.CurrentLength
    && ProfileModel.ValidatePasswords(new string('a', 12), new string('b', 11)) == PasswordFieldError.NewLength
    && ProfileModel.ValidatePasswords(new string('a', 12), new string('b', 128)) == PasswordFieldError.None
    && ProfileModel.ValidatePasswords(new string('a', 128), new string('b', 129)) == PasswordFieldError.NewLength
    && ProfileModel.ValidatePasswords(new string('a', 129), new string('b', 5)) == (PasswordFieldError.CurrentLength | PasswordFieldError.NewLength),
    "Passwords under 12 or over 128 characters must be flagged per field, at both boundaries.");
var pwFlagged = PasswordFieldError.CurrentLength | PasswordFieldError.NewLength;
Assert(ProfileModel.StillShown(pwFlagged, PasswordFieldError.None) == PasswordFieldError.None
    && ProfileModel.StillShown(pwFlagged, PasswordFieldError.CurrentLength) == PasswordFieldError.CurrentLength
    && ProfileModel.StillShown(PasswordFieldError.None, PasswordFieldError.CurrentLength) == PasswordFieldError.None,
    "After a failed password save, a flagged field keeps its error until valid; an unflagged field gets no new error.");
Assert(ProfileModel.PasswordPayload(new string('a', 12), new string('b', 128)) is { } minMaxPayload
    && minMaxPayload["current"]!.GetValue<string>().Length == 12 && minMaxPayload["password"]!.GetValue<string>().Length == 128
    && ProfileModel.PasswordPayload(new string('a', 11), new string('b', 12)) is null
    && ProfileModel.PasswordPayload(new string('a', 12), new string('b', 129)) is null
    && ProfileModel.PasswordPayload(new string('a', 129), new string('b', 12)) is null,
    "Password payloads are built only within the 12-128 character bounds, at both edges.");
Assert(ProfileModel.PasswordPayload(" abcdefghijkl ", "abcdefghijkl")!["current"]!.GetValue<string>() == " abcdefghijkl "
    && ProfileModel.PasswordPayload(" abcdefghijkl ", "abcdefghijkl")!.Select(p => p.Key).SequenceEqual(new[] { "current", "password" }),
    "Passwords must never be trimmed before sending, and the payload keys must be exactly current/password.");

// Explain: LOGIN_FAILED vs. session-ended, Unknown per write, and each profile-specific error code.
Assert(ProfileModel.Explain(Write(401, false, "LOGIN_FAILED", BridgeOutcome.Rejected), ProfileWrite.Password) is
        { Kind: WriteOutcomeKind.Rejected, SessionEnded: false, MessageKey: "Profile_ErrorCurrentPassword" }
    && ProfileModel.Explain(Write(401, false, "UNAUTHORIZED", BridgeOutcome.Rejected), ProfileWrite.Password) is { SessionEnded: true, MessageKey: "Work_ErrorSession" },
    "A wrong current password (also a 401) must be told apart from an ended session, which is UNAUTHORIZED.");
Assert(ProfileModel.Explain(BridgeResult.HostFailure("r", "TIMEOUT", false, BridgeOutcome.Unknown), ProfileWrite.Password).MessageKey == "Profile_PasswordUnknown"
    && ProfileModel.Explain(BridgeResult.HostFailure("r", "TIMEOUT", false, BridgeOutcome.Unknown), ProfileWrite.Avatar).MessageKey == "Profile_AvatarUnknown"
    && ProfileModel.Explain(Write(200, true, null, BridgeOutcome.Applied), ProfileWrite.Avatar).Kind == WriteOutcomeKind.Applied,
    "An unknown outcome must warn with the write-specific message; applied results pass through unchanged.");
Assert(ProfileModel.Explain(Write(400, false, "INVALID_AVATAR", BridgeOutcome.Rejected), ProfileWrite.Avatar).MessageKey == "Profile_ErrorInvalidAvatar"
    && ProfileModel.Explain(BridgeResult.HostFailure("r", "TOO_LARGE", false, BridgeOutcome.Rejected), ProfileWrite.Avatar) is
        { Kind: WriteOutcomeKind.NotSent, MessageKey: "Profile_ErrorInvalidAvatar" }
    && ProfileModel.Explain(Write(400, false, "PASSWORD_LENGTH", BridgeOutcome.Rejected), ProfileWrite.Password).MessageKey == "Profile_ErrorPasswordLength"
    && ProfileModel.Explain(Write(409, false, "PASSWORD_CHANGE_CONFLICT", BridgeOutcome.Rejected), ProfileWrite.Password).MessageKey == "Profile_ErrorPasswordConflict"
    && ProfileModel.Explain(Write(429, false, "RATE_LIMIT", BridgeOutcome.Rejected), ProfileWrite.Password).MessageKey == "Profile_ErrorPasswordRateLimit",
    "INVALID_AVATAR, PASSWORD_LENGTH, PASSWORD_CHANGE_CONFLICT and RATE_LIMIT must map to their own Profile messages.");

// Bridge allowlist: profile is a read, updateAvatar/changePassword are writes.
Assert(BridgePolicy.IsKnownCommand("profile") && !BridgePolicy.IsWrite("profile")
    && BridgePolicy.IsWrite("updateAvatar") && BridgePolicy.IsWrite("changePassword"),
    "profile must stay a read command; updateAvatar and changePassword must be allowlisted writes.");

// Gating: unavailable for pending accounts, no longer the generic placeholder.
Assert(!NativePageCatalog.IsAvailable(NativePage.ProfileSettings, pendingUser) && NativePageCatalog.IsAvailable(NativePage.ProfileSettings, approvedMember),
    "Profile settings must stay unavailable to pending accounts and available once approved.");
Assert(mainCode.Contains("if (page == NativePage.ProfileSettings) return CreateProfilePage();", StringComparison.Ordinal)
    && mainCode.Contains("new ProfileView(L,", StringComparison.Ordinal),
    "Profile settings must build the real ProfileView instead of falling through to the generic placeholder.");

// Sign-out is blocked while a write is in flight, and only revokes the session through the bridge after that check.
var signOutAsyncBody = mainCode[mainCode.IndexOf("private async Task SignOutAsync", StringComparison.Ordinal)..];
var inFlightCheck = signOutAsyncBody.IndexOf("_writeGate.InFlight", StringComparison.Ordinal);
var logoutRequest = signOutAsyncBody.IndexOf("RequestAsync(\"logout\")", StringComparison.Ordinal);
Assert(inFlightCheck >= 0 && logoutRequest > inFlightCheck, "Sign-out must check the write gate before asking the bridge to log out.");

// Source-level guarantees for the Profile settings write UI.
var profileCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Profile", "ProfileView.cs"));
var profileModelCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Profile", "ProfileModel.cs"));
var avatarConverterCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Profile", "AvatarConverter.cs"));
Assert(System.Text.RegularExpressions.Regex.Matches(profileCode, "_gate\\.TryEnter\\(\\)").Count == 2,
    "Both the avatar and password writes must go through the shared WriteGate.");
Assert(profileCode.Contains("new PasswordBox", StringComparison.Ordinal) && profileCode.Contains("MaxLength = ProfileModel.MaxPasswordLength", StringComparison.Ordinal)
    && profileCode.Contains("PasswordField(L(\"Profile_CurrentPassword\"))", StringComparison.Ordinal),
    "Password fields must use PasswordBox capped at the server's 128-character maximum.");
Assert(System.Text.RegularExpressions.Regex.Matches(profileCode, "DefaultButton = ContentDialogButton\\.Close").Count >= 2,
    "Confirmation and explanation dialogs must default to Close, not the destructive action.");
var changePasswordBody = profileCode[profileCode.IndexOf("private async Task ChangePasswordAsync", StringComparison.Ordinal)..];
Assert(changePasswordBody.Contains("payload.Clear();", StringComparison.Ordinal) && changePasswordBody.Contains("ClearPasswords();", StringComparison.Ordinal)
    && changePasswordBody.IndexOf("_gate.Exit();", StringComparison.Ordinal) is var pwGateExit && pwGateExit >= 0
    && changePasswordBody.IndexOf("_signOutAfterPasswordChange();", StringComparison.Ordinal) > pwGateExit,
    "Passwords must be cleared right after sending, and sign-out must only follow after the write gate is released.");
Assert(avatarCode.Contains("ConditionalWeakTable<PersonPicture, State>", StringComparison.Ordinal) && avatarCode.Contains("public static void Clear(PersonPicture picture)", StringComparison.Ordinal),
    "AvatarImage must keep one state per picture and support clearing it back to initials.");
Assert(avatarConverterCode.Contains("InitializeWithWindow.Initialize(picker, hwnd);", StringComparison.Ordinal)
    && avatarConverterCode.Contains("properties.Size > MaxFileBytes) return null;", StringComparison.Ordinal),
    "The avatar picker must be initialized with the app window and reject oversized files before decoding.");
var profileKeys = System.Text.RegularExpressions.Regex.Matches(profileCode + profileModelCode, "\"(Profile_[A-Za-z0-9]+)\"").Select(m => m.Groups[1].Value)
    .Concat(new[] { "Page_Profile settings" }).Distinct().ToList();
Assert(profileKeys.Count >= 30, "Profile settings strings must be found in the source.");
foreach (var locale in new[] { "en-US", "ja-JP" })
{
    var resource = File.ReadAllText(Path.Combine(sourceRoot, "Strings", locale, "Resources.resw"));
    foreach (var key in profileKeys) Assert(resource.Contains($"<data name=\"{key}\">", StringComparison.Ordinal), $"Missing profile string {key} in {locale}");
}

// ----- WriteGate.Released (fixed after review) -----
var releaseGate = new WriteGate();
var releasedCount = 0;
var inFlightInsideHandler = true;
releaseGate.Released += () => { releasedCount++; inFlightInsideHandler = releaseGate.InFlight; };
Assert(releaseGate.TryEnter() && !releaseGate.TryEnter() && releasedCount == 0, "A refused TryEnter (gate already busy) must not raise Released.");
releaseGate.Exit();
Assert(releasedCount == 1 && !inFlightInsideHandler, "Exit() must raise Released exactly once, with InFlight already false inside the handler.");
releaseGate.Exit();
Assert(releasedCount == 2, "Released must fire on every Exit(), including one with no waiting write.");

// ----- Sign-out when the session already ended (password change, "Sign in again") waits for the gate instead of showing the blocked dialog -----
var signOutWhenWritesFinishBody = mainCode[mainCode.IndexOf("private void SignOutWhenWritesFinish", StringComparison.Ordinal)..
    mainCode.IndexOf("private void SignOutWhenGateFree", StringComparison.Ordinal)];
Assert(signOutWhenWritesFinishBody.Contains("_writeGate.Released += SignOutWhenGateFree;", StringComparison.Ordinal)
    && !signOutWhenWritesFinishBody.Contains("ShowSignOutBlockedAsync", StringComparison.Ordinal),
    "A sign-out after the session ended must wait for the write gate to free up, never show the 'another change is being sent' dialog.");
Assert(signOutWhenWritesFinishBody.Contains("if (_signOutWhenGateFree) return;", StringComparison.Ordinal)
    && signOutWhenWritesFinishBody.Contains("_ = SignOutAsync();", StringComparison.Ordinal),
    "The deferred sign-out must subscribe only once, and sign out directly when no write is in flight.");
Assert(mainCode.Contains("private void SignOutWhenGateFree", StringComparison.Ordinal)
    && mainCode.Contains("_writeGate.Released -= SignOutWhenGateFree;", StringComparison.Ordinal)
    && !mainCode.Contains("SignOutAfterPasswordChange", StringComparison.Ordinal),
    "The queued sign-out must unsubscribe once it runs; the old SignOutAfterPasswordChange name must be gone.");
var signOutWhenGateFreeStart = mainCode.IndexOf("private void SignOutWhenGateFree", StringComparison.Ordinal);
var signOutWhenGateFreeBody = mainCode[signOutWhenGateFreeStart..mainCode.IndexOf("private async Task ShowSignOutBlockedAsync", signOutWhenGateFreeStart, StringComparison.Ordinal)];
Assert(signOutWhenGateFreeBody.Contains("_writeGate.Released -= SignOutWhenGateFree;", StringComparison.Ordinal)
    && signOutWhenGateFreeBody.Contains("_signOutWhenGateFree = false;", StringComparison.Ordinal)
    && signOutWhenGateFreeBody.Contains("if (!DispatcherQueue.TryEnqueue(SignOutWhenWritesFinish)) SignOutWhenWritesFinish();", StringComparison.Ordinal)
    && !signOutWhenGateFreeBody.Contains("SignOutAsync", StringComparison.Ordinal),
    "Released fires inside the finishing write's finally, so the queued sign-out must unsubscribe, reset its flag and queue SignOutWhenWritesFinish (re-checking the gate, direct call if TryEnqueue fails), never SignOutAsync.");
// Every page's "Sign in again" (session already ended on the server) uses SignOutWhenWritesFinish, never the refusing path.
Assert(!mainCode.Contains("() => _ = SignOutAsync()", StringComparison.Ordinal),
    "No page may be wired with () => _ = SignOutAsync(); 'Sign in again' must use SignOutWhenWritesFinish.");
foreach (var (signInFactory, signInNext) in new[]
{
    ("private FrameworkElement CreateDashboardPage", "private FrameworkElement CreateProjectsPage"),
    ("private FrameworkElement CreateProjectsPage", "private FrameworkElement CreateCommitHistoryPage"),
    ("private FrameworkElement CreateCommitHistoryPage", "private FrameworkElement CreateApprovalPage"),
    ("private FrameworkElement CreateApprovalPage", "private FrameworkElement CreateManagementPage"),
    ("private FrameworkElement CreateManagementPage", "private FrameworkElement CreateProfilePage"),
    ("private FrameworkElement CreateProfilePage", "private FrameworkElement CreateMaintenancePage"),
})
{
    var signInStart = mainCode.IndexOf(signInFactory, StringComparison.Ordinal);
    var signInEnd = mainCode.IndexOf(signInNext, StringComparison.Ordinal);
    Assert(signInStart >= 0 && signInEnd > signInStart && mainCode[signInStart..signInEnd].Contains("SignOutWhenWritesFinish", StringComparison.Ordinal)
        && !mainCode[signInStart..signInEnd].Contains("SignOutAsync", StringComparison.Ordinal),
        $"{signInFactory} must wire 'Sign in again' to SignOutWhenWritesFinish, not SignOutAsync.");
}
var maintenanceFactoryStart = mainCode.IndexOf("private FrameworkElement CreateMaintenancePage", StringComparison.Ordinal);
var maintenanceFactory = mainCode[maintenanceFactoryStart..mainCode.IndexOf("return _maintenance;", maintenanceFactoryStart, StringComparison.Ordinal)];
Assert(maintenanceFactory.Contains("SignOutWhenWritesFinish", StringComparison.Ordinal) && !maintenanceFactory.Contains("SignOutAsync", StringComparison.Ordinal),
    "CreateMaintenancePage must wire 'Sign in again' to SignOutWhenWritesFinish, not SignOutAsync.");
var profileFactoryStart = mainCode.IndexOf("private FrameworkElement CreateProfilePage", StringComparison.Ordinal);
var profileFactory = mainCode[profileFactoryStart..mainCode.IndexOf("private FrameworkElement CreateMaintenancePage", StringComparison.Ordinal)];
Assert(System.Text.RegularExpressions.Regex.Matches(profileFactory, "SignOutWhenWritesFinish").Count == 2,
    "ProfileView must get SignOutWhenWritesFinish for both its sign-in-again and sign-out-after-password-change callbacks.");
// The account menu's own Sign out still refuses (explains) while a write is in flight.
var signOutClickBody = mainCode[mainCode.IndexOf("private async void SignOut_Click", StringComparison.Ordinal)..mainCode.IndexOf("private async Task SignOutAsync", StringComparison.Ordinal)];
Assert(signOutClickBody.Contains("await SignOutAsync();", StringComparison.Ordinal) && !signOutClickBody.Contains("SignOutWhenWritesFinish", StringComparison.Ordinal),
    "The account-menu Sign out must go through SignOutAsync, not the deferred path.");
var signOutAsyncOnly = mainCode[mainCode.IndexOf("private async Task SignOutAsync", StringComparison.Ordinal)..mainCode.IndexOf("private void SignOutWhenWritesFinish", StringComparison.Ordinal)];
Assert(signOutAsyncOnly.Contains("if (_writeGate.InFlight)", StringComparison.Ordinal)
    && signOutAsyncOnly.IndexOf("await ShowSignOutBlockedAsync();", StringComparison.Ordinal) > signOutAsyncOnly.IndexOf("if (_writeGate.InFlight)", StringComparison.Ordinal)
    && signOutAsyncOnly.IndexOf("_signingOut = true;", StringComparison.Ordinal) > signOutAsyncOnly.IndexOf("await ShowSignOutBlockedAsync();", StringComparison.Ordinal),
    "SignOutAsync must still refuse with ShowSignOutBlockedAsync while a write is in flight, before starting the sign-out.");

// ----- ProfileView: a write's own reload is exempt from the write-in-progress guard, and the icon change always reaches MainWindow -----
Assert(profileCode.Contains("if (_writing && !partOfWrite) return false;", StringComparison.Ordinal),
    "A plain load (navigation/Retry) must be refused while a write is sending; only the write's own reload may proceed.");
Assert(System.Text.RegularExpressions.Regex.Matches(profileCode, "LoadAsync\\(partOfWrite: true\\)").Count == 2,
    "Both the Applied and Unknown avatar-write branches must reload with partOfWrite: true.");
Assert(profileCode.Contains("_avatarChanged(shown ? _profile?.Avatar : png);", StringComparison.Ordinal),
    "After an Applied icon write, MainWindow must always learn the resulting icon (the sent PNG when the profile could not be re-read).");

// ----- Centered page column (v0.14.4): width from layout via PageParts.CenteredPage/CenteredColumnPanel, never Stretch + MaxWidth on the page root -----
// Slices [start marker, end marker) and fails the assertion (instead of throwing) when a marker is missing.
static string SliceBetween(string text, string startMarker, string endMarker, string what)
{
    var sliceStart = text.IndexOf(startMarker, StringComparison.Ordinal);
    var sliceEnd = sliceStart < 0 ? -1 : text.IndexOf(endMarker, sliceStart + startMarker.Length, StringComparison.Ordinal);
    Assert(sliceStart >= 0 && sliceEnd >= 0, $"{what}: marker not found ('{(sliceStart < 0 ? startMarker : endMarker)}').");
    return text[sliceStart..sliceEnd];
}
var pagePartsCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", "Common", "PageParts.cs"));
var centeredPageBody = SliceBetween(pagePartsCode, "public static ScrollViewer CenteredPage(FrameworkElement column, double maxWidth)", "\r\n    }", "PageParts.CenteredPage");
Assert(System.Text.RegularExpressions.Regex.IsMatch(centeredPageBody, @"new CenteredColumnPanel\s*\{\s*ColumnMaxWidth = maxWidth\s*\}")
    && centeredPageBody.Contains("Children.Add(column);", StringComparison.Ordinal)
    && centeredPageBody.Contains("new ScrollViewer", StringComparison.Ordinal)
    && centeredPageBody.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", StringComparison.Ordinal)
    && centeredPageBody.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", StringComparison.Ordinal)
    && centeredPageBody.Contains("Content = host", StringComparison.Ordinal),
    "CenteredPage must host the column in a CenteredColumnPanel capped at maxWidth, inside a vertical-only ScrollViewer.");
Assert(!centeredPageBody.Contains("SizeChanged", StringComparison.Ordinal)
    && !centeredPageBody.Contains("column.Width", StringComparison.Ordinal)
    && !centeredPageBody.Contains("HorizontalAlignment.Stretch", StringComparison.Ordinal)
    && !System.Text.RegularExpressions.Regex.IsMatch(centeredPageBody, @"\bMaxWidth\s*="),
    "The column width must come from layout (the viewport), never from a SizeChanged handler or Stretch + MaxWidth.");
var centeredPanelCode = SliceBetween(pagePartsCode, "internal sealed class CenteredColumnPanel : Panel", "internal sealed class InlineWrapPanel", "CenteredColumnPanel");
var centeredMeasure = SliceBetween(centeredPanelCode, "MeasureOverride(", "ArrangeOverride(", "CenteredColumnPanel.MeasureOverride");
var centeredArrangeStart = centeredPanelCode.IndexOf("ArrangeOverride(", StringComparison.Ordinal);
var centeredArrange = centeredArrangeStart < 0 ? "" : centeredPanelCode[centeredArrangeStart..];
Assert(centeredPanelCode.Contains("public double ColumnMaxWidth", StringComparison.Ordinal)
    && System.Text.RegularExpressions.Regex.IsMatch(centeredPanelCode, @"double\.IsInfinity\(available\)\s*\?\s*ColumnMaxWidth\s*:\s*Math\.Min\(ColumnMaxWidth, available\)"),
    "CenteredColumnPanel's column width must be min(ColumnMaxWidth, available), and ColumnMaxWidth when the available width is infinite.");
Assert(centeredMeasure.Contains("ColumnWidth(availableSize.Width)", StringComparison.Ordinal)
    && centeredMeasure.Contains("child.Measure(new global::Windows.Foundation.Size(width,", StringComparison.Ordinal),
    "MeasureOverride must measure each child at the capped column width.");
Assert(centeredArrange.Contains("ColumnWidth(finalSize.Width)", StringComparison.Ordinal)
    && centeredArrange.Contains("(finalSize.Width - width) / 2", StringComparison.Ordinal)
    && System.Text.RegularExpressions.Regex.IsMatch(centeredArrange, @"child\.Arrange\(new global::Windows\.Foundation\.Rect\(\(finalSize\.Width - width\) / 2, 0, width,"),
    "ArrangeOverride must center each child at x = (finalSize.Width - width) / 2 with the capped width.");
foreach (var (centeredView, centeredCall) in new[]
{
    (Path.Combine("Profile", "ProfileView.cs"), "PageParts.CenteredPage(root, 960)"),
    (Path.Combine("Maintenance", "MaintenanceView.cs"), "PageParts.CenteredPage(root, 960)"),
    (Path.Combine("Management", "ManagementView.cs"), "PageParts.CenteredPage(root, 960)"),
    (Path.Combine("Dashboard", "DashboardView.cs"), "PageParts.CenteredPage(root, 1280)"),
    (Path.Combine("Projects", "ProjectsView.cs"), "PageParts.CenteredPage(root, 1280)"),
})
{
    var centeredViewCode = File.ReadAllText(Path.Combine(sourceRoot, "Pages", centeredView));
    Assert(centeredViewCode.Contains(centeredCall, StringComparison.Ordinal),
        $"{centeredView} must build its page column with {centeredCall}.");
}
// No object initializer under Pages/ may combine HorizontalAlignment.Stretch with MaxWidth (single- or multi-line initializer),
// and a panel (StackPanel/Grid) with MaxWidth must say Left or Center explicitly: the default alignment is Stretch.
foreach (var pageSourceFile in Directory.GetFiles(Path.Combine(sourceRoot, "Pages"), "*.cs", SearchOption.AllDirectories))
{
    var pageSource = File.ReadAllText(pageSourceFile);
    foreach (System.Text.RegularExpressions.Match initializer in System.Text.RegularExpressions.Regex.Matches(pageSource, @"new\s+([A-Za-z_][A-Za-z0-9_.<>]*)\s*(\([^()]*\))?\s*\{[^{}]*\}"))
    {
        var hasMaxWidth = System.Text.RegularExpressions.Regex.IsMatch(initializer.Value, @"\bMaxWidth\s*=");
        Assert(!(hasMaxWidth && initializer.Value.Contains("HorizontalAlignment.Stretch", StringComparison.Ordinal)),
            $"{Path.GetFileName(pageSourceFile)} combines HorizontalAlignment.Stretch with MaxWidth; use PageParts.CenteredPage(root, maxWidth) instead.");
        var initializerType = initializer.Groups[1].Value;
        Assert(!(hasMaxWidth && (initializerType == "StackPanel" || initializerType == "Grid")
                && !initializer.Value.Contains("HorizontalAlignment.Left", StringComparison.Ordinal)
                && !initializer.Value.Contains("HorizontalAlignment.Center", StringComparison.Ordinal)),
            $"{Path.GetFileName(pageSourceFile)} has a {initializerType} with MaxWidth and the default (Stretch) alignment; use PageParts.CenteredPage(root, maxWidth) or an explicit Left/Center alignment.");
    }
    Assert(!System.Text.RegularExpressions.Regex.IsMatch(pageSource, @"\broot\.MaxWidth\s*="),
        $"{Path.GetFileName(pageSourceFile)} must not set MaxWidth on the page root after construction; use PageParts.CenteredPage(root, maxWidth).");
    Assert(!pageSource.Contains("MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch", StringComparison.Ordinal),
        $"{Path.GetFileName(pageSourceFile)} must not use the old Stretch + MaxWidth page root.");
}

Console.WriteLine("v0.14.5 native shell, dashboard, work writes, commit history, project management, profile settings, administration, approval screen, server maintenance, settings, version info, title bar, flyout, login boundary, policy, profile, concurrency, docs and localization tests passed.");
