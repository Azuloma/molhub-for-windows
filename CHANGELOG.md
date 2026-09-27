# Changelog

## v0.12.0

- Added the native, read-only **Project management** page (`manageProjects`, `manageProject`), following the web "Manage" panel. It appears in the navigation pane for approved project owners and for site administrators (who manage every project). Pick a project (deleted projects are listed last and marked "Deleted") to see its state and project administrator, members (with the administrator marked and each account's status), the current work reservation, approval requests (kind, reason, requester, date, status), Discord destinations and project activity. Member events name the affected account; other internal ids are not shown. "You do not manage any projects." is shown when the list is empty.
- The page is read-only: adding or removing members, force-releasing reservations, deletion/restoration requests and Discord changes stay on the web for now, and the page says so.

## v0.11.1

- Announcements moved to the **Server maintenance** page, where maintenance information belongs; the title-bar bell is for notifications again (still "not connected"). Server maintenance is now a read-only page for every signed-in account, pending ones included: the current maintenance state from the server (`maintenance`: "Maintenance active" with the web message and, for members, the access note, the start time; or "No maintenance in progress") and the announcements published when a maintenance ended, with "Load more" and details. Starting or completing maintenance is not shown until it exists natively. The approval screen's Announcements button opens this page.
- Announcement previews in the list keep the line breaks of the details, as on the web.

## v0.11.0

- Added the approval-waiting screen for accounts that are not approved yet, following the web: a clock, the "Awaiting approval" label, the web heading and intro, **Refresh** and **Announcements**. Such accounts no longer see the Dashboard, Projects or Commit history (the server refuses them anyway); the pane shows the approval screen, Announcements, App settings and Version info. Refresh reads the server session (`session`) and opens the workspace once the same account is approved, without signing in again; otherwise it says the account is not approved yet.
- Added native, read-only announcements (`announcements`). The title-bar bell now opens the latest four announcements (title, version · date, a short preview) with "View all announcements" instead of a "not connected" message. The Announcements page lists them with "Load more" and shows an announcement's details; the title-bar Back and Alt+Left return to the list. Pending accounts can read them, as on the web.
- The account menu hides Profile settings for accounts that are not approved.

## v0.10.0

- Added the native, read-only Commit history page on the data bridge (`history`), following the web "Your commit history" page. The server always limits it to your own commits. It shows the server summary (total commits, projects, named versions) for the current filters, a project filter (all projects or one), commit search, date range (all time / 7 / 30 / 90 days) and latest versions only, commits grouped by day with the project name, and "Load more" paging. Commits and projects open inside the page with a breadcrumb; the title bar Back and Alt+Left walk back through it. The Dashboard's "View your commit history" link opens it.
- The project Commits tab and the history share one commit timeline (`BuildCommitTimeline`).

## v0.9.2

- The project list's "Latest version" and "Last updated" line wraps (`InlineWrapPanel`) in a narrow window instead of clipping the date; the version label and its badge stay together. Commit rows wrap their version badge and "author · date" line the same way.
- Source files are grouped in folders (`Auth`, `Bridge`, `Shell`, `Settings`, `Pages`). Namespaces and behavior are unchanged.

## v0.9.1

- Project names and the Private label now wrap (`InlineWrapPanel`) in the project list and the project heading. A long name was clipped and pushed the label out of view.
- Commit counts use the singular form for one commit ("1 commit").

## v0.9.0

- Added the native, read-only Projects page on the data bridge (`projects`, `project`, `projectCommits`, `commit`), following the web project pages. The list shows your profile, accessible projects, a project search (name and description, up to 150 characters), rows with the private label, description, latest version, last update and the reservation state ("Available for work" or "Working" with the holder), and "Load more" paging.
- A project opens inside the page with a breadcrumb (the title bar Back and Alt+Left walk back through it). Overview: the latest versions (up to 8, with "View all history"), the project overview with the latest change, "Open MFA" and "Details", the work reservation, About (project type, visibility, created, last updated, copyable project ID) and team access with roles. Commits: search, date range (all time / 7 / 30 / 90 days), latest versions only, grouped by day, paged. Commit details show the version, version ID, the version it was based on (or "Initial version"), the author and the full changes.
- Share links open only in the default browser, only for absolute http(s) URLs without credentials, and name the destination host first; the external-storage note is shown wherever share links appear. Reservation, publishing, member management and project settings are not rendered until they exist natively.
- Dashboard: Top projects and Your work entries open the native project page.
- Dashboard counters read a non-numeric value (for example `null`) as zero instead of failing the whole parse.

## v0.8.4

- The caption buttons (minimize, maximize, close) follow the app theme in the main and sign-in windows through `CaptionButtonTheme` (`AppWindowTitleBar.PreferredTheme`). With the Light app theme on a dark Windows mode they were drawn white on white and looked missing. High Contrast keeps the system colors.
- App settings no longer shows "Theme saved" just by opening the page. A language or theme selection equal to the saved value is ignored, so only a real change is saved and reported.

## v0.8.3

- Avatars (Dashboard commits, title bar and account flyout) are decoded again whenever the picture is loaded or its theme changes, via `AvatarImage`. The v0.8.1 fix only listened for theme changes and did not cover a Dashboard that was off screen during the switch, so initials still appeared.

## v0.8.2

- Pages now fill the content area when the window grows or is maximized. `ContentFrame` stretches its content horizontally and vertically instead of the ContentControl default (left/top), which kept the Dashboard at its earlier desired width. Dashboard content wider than 1280 DIP stays centered.

## v0.8.1

- Dashboard commit avatars are decoded again after a Light/Dark theme change; previously they fell back to initials once the theme changed.

## v0.8.0

- Added the native Dashboard, loaded through the data bridge from `/api/v1/dashboard`. It follows the web workspace layout: a 300 DIP side column with Workspace status (active projects, in progress, your reservations, and pending approvals for site admins and project owners), Top projects (8), Activity (4) and the external-storage privacy note, next to the Recent commits feed (6 commits with author avatar, "published" byline, local date, title, 420-character change preview, version badge and a copyable short commit ID). Your own reservations appear above the feed. Below 860 DIP the columns stack.
- Loading, refresh and honest error states (connection, ended session with "Sign in again", pending approval, maintenance, rate limit, unexpected) use an InfoBar; data older than a minute is refreshed when the page is shown again. Actions without a native implementation (create project, publish, open project) are not rendered.
- Dashboard styles live in `App.xaml` with ThemeResource setters so code-built pages follow Light, Dark and High Contrast.

## v0.7.1

- The data bridge retries hidden controller creation (up to three attempts with backoff), because the sign-in WebView may still be shutting down the shared browser process right after sign-in.
- Version info shows a diagnostic next to an unavailable bridge: the failing stage with an HRESULT, WebView2 error status or session HTTP status. It never contains page data.

## v0.7.0

- Added the MolHub web data bridge host (`WebBridgeClient`). A hidden WebView2 controller in its own invisible window loads only `https://<origin>/webview-bridge` (the canonical path; `/webview-bridge.html` redirects there), enables WebMessage only there, accepts messages only from that exact document, sends only allow-listed commands and correlates results by `requestId`. Host timeouts (20 s reads, 40 s writes) back up the page's own 15 s / 30 s aborts; writes that time out or lose the bridge are reported with outcome Unknown and are never resent.
- The sign-in window and the bridge share one WebView2 environment (`WebViewProfile`), so the browser engine shares the HttpOnly session cookie; the host never reads it. The sign-in WebView keeps WebMessage disabled.
- Sign out now calls the bridge `logout` operation to revoke the server session before clearing local WebView2 data (local clearing still happens if the bridge is unavailable).
- Version info shows the web data connection state (connecting, connected, server session ended, unavailable). Data pages remain placeholders.

## v0.6.4

- Title-bar flyouts now use thin desktop acrylic so the screen behind visibly shows through. The windowed flyout popup never becomes the active window, so its backdrop is configured as active (following the app theme and High Contrast) instead of drawing the inactive solid fallback.
- Title-bar flyouts open above their button when the monitor work area below is too short, so they no longer slide under the taskbar.

## v0.6.2

- Restyled the account and notification flyouts after the Windows account menu: 320 px account flyout (48 px avatar, name, status and role, compact sign-out link, separator, 36 px icon rows) and a slightly wider 360 px notifications flyout with the same header/separator layout. Both use a transparent presenter over a desktop acrylic backdrop, 8 px corners, may extend beyond the main window near screen edges and scroll instead of clipping.
- The main window now opens at 1464 x 934.

## v0.6.1

- Made the sign-in window a compact dialog: a 481 x 683 DIP client area (32 DIP title bar with only a close button, no title text or icon) scaled for the monitor's DPI, clamped to its work area and centered. The window can no longer be resized, maximized or minimized.
- The web sign-in surface now fills the window. A centered progress ring with "Please wait…" covers it until the first page load completes; sign-out and recovery states use the same overlay.

## v0.6.0

- Renamed the user-facing product from Fusion Ledger for Windows to MolHub for Windows in the package display names, window and title-bar text, accessibility names and installer messages. The package identity, namespace and WebView2 profile folder are unchanged so upgrades keep sign-in data and settings.
- Replaced the app icon with the MolHub mole artwork: scaled Start/taskbar/tile/store logos, `targetsize` unplated variants, dark-line `lightunplated` variants for light taskbars, and theme-aware title-bar and window icons.

## v0.5.2

- Fixed the post-login access violation by replacing `Frame.Navigate` with `ContentControl` content updates and `EntranceThemeTransition` animations.

## v0.5.1

- Moved the standard NavigationView toggle onto the Dashboard and set the expanded pane width to 240 DIP.
- Added `EntranceNavigationTransitionInfo` animations for section changes.
- Deferred initial page navigation until the shell loaded and guarded against reentrant selection updates; the post-login access violation remained unresolved.

## v0.5.0

- Added native App settings with persisted English/Japanese language and System/Light/Dark theme choices.
- Added runtime Version info and configured the supported AppWindow title bar for the Tall system caption-button layout.

## v0.4.1

- Refined the native TitleBar alignment and responsive search sizing.
- Kept page search visible at narrow widths with staged 96/180/280px states and moved Ctrl+K ownership to the search box.
- Reduced the notification glyph and changed only its hover hit surface to a rounded rectangle while retaining the circular profile action.
- Replaced adaptive search VisualStates with client-size-driven 96/200/320/420/540px sizing and hid root accelerator placement hints without removing shortcuts.
- Added transparent circular notification/account actions with standard focus and hover behavior.
- Restructured the account flyout into an identity block, separator and accessible menu rows.
- Localized account-menu automation names and retained role-based administration visibility.

## v0.4.0

- Replaced the browser-wrapper MainWindow with a native WinUI 3 shell.
- Added the Windows App SDK TitleBar control, native NavigationView, local page search, account and notification flyouts, and honest disconnected placeholders.
- Added LoginWindow as the only WebView2 host with persistent profile authentication and strict `/api/me` parsing.
- Added latest-response-wins authentication coordination and local-only sign-out behavior.
- Sign-out now keeps a replacement LoginWindow alive, clears the active WebView2 profile through its supported APIs, and blocks navigation on clear failure.
- Added English/Japanese resources and updated build, security and unsigned-package documentation.

Known limitation: sign out clears local WebView2 data when possible but does not revoke the server-side session; commit notifications and tray/background operation remain future work.
