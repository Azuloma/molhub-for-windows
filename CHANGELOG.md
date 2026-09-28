# Changelog

## v0.14.7

- Administration → Members can now change member accounts, as on the web admin panel: Approve, Reject and Suspend, and Issue reset code. The actions offered follow the account's status (awaiting approval: Approve, Reject, Suspend, Issue reset code; approved: Issue reset code, Suspend; rejected: Approve, Suspend; suspended: Approve). Administrator accounts, including your own, have no actions.
- Every action is confirmed first (Cancel is the default; Reject and Suspend warn that normal access is removed), sent once, and followed by a Members refresh. If the result can't be confirmed, the page says so and nothing is sent again.
- A reset code is shown once in its own dialog with its expiry and a Copy button; it replaces any earlier code, is never kept or logged, and can't be displayed again after the dialog closes.
- The other Administration sections remain read-only. No Web API or bridge contract changed (`setUserStatus`, `issueResetCode`).

## v0.14.6

- Fixed Administration Members staying on the loading indicator and crashing when reopened after switching sections. Some member statuses referenced a missing XAML style; the page now uses the defined style. Read and rendering failures also settle into the existing localized error state.
- Administration remains read-only. No Web API or bridge contract changed.

## v0.14.5

- Added the native, read-only Administration page for approved site administrators (account menu → Administration). A section list on the left (above the content in narrow windows) switches between Requests, Projects, Members, Reservations, Discord, Maintenance and Audit, following the web admin panel; only the chosen section is loaded.
- Requests shows each deletion, restoration or Discord request with its project, reason, requester, date and status. Projects lists active projects with "Working · holder" or "Available for work", and deleted projects with their deletion date. Members shows each account's avatar, role and status. Reservations lists the active work reservations with holder and start time. Audit lists administrative actions with actor, date and target. Long lists offer "Load more".
- Discord shows whether the Discord App is configured (the four setup checks, never secret values), the linked channels with copyable server and channel IDs, and the recent deliveries with status, attempts and the last error. Maintenance shows the current state and the completed history as announcement cards, shared with Server maintenance.
- Nothing can be changed from this page yet: approving accounts, reset codes, creating, deleting or restoring projects, releasing reservations, reviewing requests, starting or completing maintenance and Discord actions stay on the web for now.

## v0.14.4

- Fixed Profile settings being pushed to the right and cut off in wide windows: the page column is now centered at up to 960 px. Server maintenance and Project management (up to 960 px) and Dashboard, Projects and Commit history (up to 1280 px) use the same centering, so they no longer shift when their content is short.

## v0.14.3

- "Sign in again" on any page (after the session ended) is no longer refused while a change is still being sent; it waits for that change to finish and then signs out, like the sign-out after a password change. Sign out from the account menu is still blocked until the change finishes.

## v0.14.2

- Added native **Profile settings**: your username and role, and two cards. **Profile** lets you choose an image for your icon — any PNG, JPEG, BMP, GIF, TIFF or WebP up to 20 MB is automatically converted to a PNG of up to 128 × 128 pixels and 32 KB — shows a preview that is not saved yet, and Save or Cancel it; **Remove icon** asks for confirmation first. **Change password** takes your current and a new password (12–128 characters), warns that every device including this one will be signed out, and asks for confirmation before sending.
- Saving a new icon updates it everywhere it is shown (title bar, account menu, Projects) without reloading the app. Changing your password signs the app out once the change is confirmed by the server; if the result can't be confirmed, the app still signs out and explains that your previous password may still be valid, and the change is never sent twice.
- Sign out is now blocked while any change (icon, password or elsewhere) is still being sent, so it can't interrupt a write in flight.
- Nothing beyond username, role and the icon is shown; there is still no session/device list, and Administration remains a placeholder.

## v0.14.1

- Projects: a project that is already open reloads when you return to it after a change on another page (for example a work reservation released in Project management), instead of showing the old reservation until Refresh.

## v0.14.0

- **Project management** now has the web's management actions, shown where the web shows them: **Add member** (approved accounts that are not members yet) and **Remove from project** (not for the project administrator or site administrators), **Release reservation**, **Request deletion** / **Request restoration** with a reason (only for the project's own administrator; a site administrator approves), **Connect / request approval** for a Discord destination (server and channel IDs typed in, notification language, reason and the sharing consent; an approved destination connects at once, a new one waits for approval) and **Stop integration**, and for site administrators **Assign / transfer owner** (including "Managed by site administrators").
- Removing, releasing and stopping ask for confirmation first ("Cancel" is the default). Every action is sent once, the project is reloaded and the result is explained in the web wording (account not approved, project administrator protected, owner only, request already handled, Discord access or conflicts, no access, session ended). When the result can't be confirmed nothing is resent; for requests the message warns that sending again could create a duplicate.
- The read-only note on the page was removed. Discord server/channel discovery and Discord App setup stay on the web.
## v0.13.1

- Publish version form: after a failed check, a flagged field's error now clears as soon as the field is fixed, and the "Check the form" notice closes once every flagged field is valid. Fields that were fine get no new error while typing; they are checked again on the next Publish.

## v0.13.0

- First native writes, on the project page's Work reservation card: **Start work** when the project is free (`startReservation`, from the current head), and **Publish version** and **Cancel work** when the reservation is yours (`cancelReservation`). The card also shows the web note that reservations do not lock local files.
- **Publish version** opens a form on the project's own screen stack (also from the Dashboard's "Your work" rows): based-on version, title (150), version label (optional, 40, unique in the project), changes (10,000) and the MFA share URL (http/https without credentials, 2,048), with the web storage note. The form is checked with the server limits before sending, and publishing asks for confirmation first ("Cancel" is the default). A published version ends the reservation, as on the web.
- Every write is sent once. The project is refreshed afterwards and the result is explained: success (the web wording), a specific reason for each server rejection (already reserved by you or another member, outdated base version, reservation no longer active or held by someone else, version label in use, invalid URL, project deleted, no access, session ended), or "the result couldn't be confirmed" when the connection was lost. An unconfirmed write is never resent automatically; after an unconfirmed publish the refreshed reservation tells whether the version was most likely published.
- The reservation hint no longer says publishing is web-only.

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
