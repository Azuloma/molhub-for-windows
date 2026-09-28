![image 01](<https://github.com/Azuloma/molhub-for-windows/blob/main/Assets/for%20Github/image01.png>)

A native-first Windows client for MolHub (formerly Fusion Ledger), built with WinUI 3. Current version: **v0.14.8** (beta prototype).

The app opens a sign-in window that hosts the existing MolHub web login in WebView2. Once the production `/api/me` response confirms the signed-in user, the sign-in window closes and a native main window opens. The main window is a WinUI shell, not a wrapper around the web application.

## Status

| Area               | State                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sign-in (WebView2) | Working. A saved session signs in automatically on the next launch.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Native shell       | Title bar, page search, navigation pane, account and notification flyouts.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| App settings       | Working. Language (English / Japanese) and theme (System / Light / Dark).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| Version info       | Working. Reports the app, Windows App SDK, WebView2 runtime, package details and the web data connection state.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Web data bridge    | Connected. Confirms the server session after sign-in, loads the Dashboard and revokes the session on sign out.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Dashboard          | Working. Workspace status, top projects, activity and recent commits from the web data bridge, with refresh and error states. Top projects open the native project page.                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Projects           | Working. Project list with search and paging, project overview (latest versions, latest change, work reservation, about, team), the project's commits with search / date range / latest-only filters, and commit details. Start work, Cancel work and Publish version (with confirmation) work natively; project settings stay on the web for now.                                                                                                                                                                                                                                                        |
| Commit history     | Working, read-only. Your own commits (the server limits the list to you) with the summary counts, project / search / date range / latest-only filters, day groups and paging; commits and projects open inside the page.                                                                                                                                                                                                                                                                                                                                                                                  |
| Project management | Working, for approved project owners and site administrators. Pick a project to see its state, administrator, members, work reservation, approval requests, Discord destinations and activity, and manage it as on the web: add or remove members, release the work reservation, request deletion or restoration (the project's own administrator), connect a Discord destination or request its approval (server and channel IDs typed in), stop an integration, and, for site administrators, appoint or remove the project administrator. Removing, releasing and stopping ask for confirmation first. |
| Approval waiting   | Working. An account that is not approved yet sees the web's approval-waiting screen instead of the workspace (plus Server maintenance, App settings and Version info). Refresh checks the server session and opens the workspace once the account is approved, without signing in again.                                                                                                                                                                                                                                                                                                                  |
| Server maintenance | Working, read-only, for every signed-in account. The current maintenance state and the announcements published after each maintenance, with paging and details. Starting or completing maintenance stays on the web.                                                                                                                                                                                                                                                                                                                                                                                      |
| Profile settings   | Working. Choose or remove your icon (any supported image is converted to a PNG of up to 128 × 128 pixels and 32 KB, with a preview before saving) and change your password (signs out every device, including this one, once confirmed).                                                                                                                                                                                                                                                                                                                                                                 |
| Administration     | Appears only for approved admins (account menu). Requests, Projects, Members, Reservations, Discord status, Maintenance history and Audit, as on the web admin panel. Members can approve, reject or suspend member accounts and issue a one-time reset code (confirmed, sent once); the other sections are read-only and their changes stay on the web.                                                                                                                                                                                                                                                  |
| Notifications      | Not connected. The bell flyout says so; no notifications are generated.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |

Placeholder pages show a heading and a "not connected" message only. They never show sample counts, records or controls that do nothing.

## Requirements

- Windows 10 version 2004 (build 19041) or later, x64
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime
- Developer Mode, to install the unsigned development package

## Build and test

Run these from the repository root:

```powershell
dotnet restore
dotnet run --project tests\FusionLedger.Windows.PolicyTests\FusionLedger.Windows.PolicyTests.csproj
dotnet build FusionLedger.Windows.csproj -p:Platform=x64 -p:Configuration=Debug
```

- The policy tests check navigation and sign-in rules, settings validation, `/api/me` parsing, and required structure in the source, XAML, version files and resources. A passing run prints a single summary line.
- The build creates an unsigned MSIX in `AppPackages\FusionLedger.Windows_<version>_x64_Debug_Test\`.

## Install a development build

Use Windows PowerShell 5.1 or later. First enable **Settings > System > For developers > Developer Mode**. Then check and install the package you built:

```powershell
.\Install-Prototype.ps1 -WhatIf -PackageDirectory ".\AppPackages\FusionLedger.Windows_0.14.8.0_x64_Debug_Test"
.\Install-Prototype.ps1 -PackageDirectory ".\AppPackages\FusionLedger.Windows_0.14.8.0_x64_Debug_Test"
```

- `-WhatIf` only checks that the package and its x64 dependency packages are present; nothing is installed.
- `-PackageDirectory` can be omitted only when `AppPackages` contains exactly one `*_x64_Debug_Test` directory.
- The script installs for the current user with `Add-AppxPackage -AllowUnsigned`, including the packages in `Dependencies\x64`. It does not need administrator rights or a certificate.
- If the execution policy blocks the script, allow it for the current PowerShell session only: `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`.

This package is unsigned and meant for development only. Windows SmartScreen may warn about it. Signed distribution is not set up yet.

## How it works

- **Packaging:** packaged WinUI 3 desktop app for x64, using Microsoft.WindowsAppSDK 2.5.1 and Microsoft.Web.WebView2.
- **Sign-in window:** a compact, fixed-size dialog with a 481 × 683 DIP client area (32 DIP title bar with only a close button). It scales with the monitor's DPI, stays inside the work area and opens centered; a progress ring covers the web view until the first page load.
- **Windows:** `LoginWindow` hosts the only visible WebView2. `MainWindow` is fully native (no WebView2 in its XAML) and changes pages by replacing the content of `ContentFrame`.
- **Data bridge:** `WebBridgeClient` runs a hidden WebView2 controller in its own invisible window. It loads only `https://fusion-ledger.desase0175.workers.dev/webview-bridge` (the canonical path of the web app's bridge page) and exchanges `{ command, requestId, payload }` messages with it; the page calls `/api/v1` with the session cookie. Both WebViews share one environment (`WebViewProfile`), so the engine shares the HttpOnly cookie without the app reading it. Writes that time out or lose the bridge are reported as "outcome unknown" and never resent automatically.
- **Sign-in data:** the WebView2 profile is stored at `ApplicationData.Current.LocalFolder\FusionLedger.WebView2`. For an installed package this is normally `%LOCALAPPDATA%\Packages\FusionLedger.Windows_*\LocalState\FusionLedger.WebView2`.
- **User details:** the app reads only an exact HTTPS `GET /api/me` response. It accepts bounded JSON with a username, a known status (`pending`, `approved`, `rejected`, `suspended`) and role (`member`, `admin`), plus an optional small PNG avatar.
- **Settings:** only the `ui.language` and `ui.theme` keys are stored in `LocalSettings`. Invalid values fall back to English and System. Theme changes apply at once; a language change applies after restarting the app.
- **Name and icon:** the user-facing name is MolHub for Windows. Internal identifiers (the `FusionLedger.Windows` package identity, namespace, WebView2 profile folder and production host name) are unchanged so upgrades keep the sign-in profile and settings. The icon is a white line drawing; light surfaces (light taskbar and Start, the Light app theme) use a dark-line variant generated from the same artwork.
- **Accessibility:** English and Japanese resources, Light, Dark and High Contrast themes, keyboard shortcuts (Ctrl+K search, Ctrl+, settings, Alt+N notifications, Alt+Left back) and localized accessibility names.

## Security boundary

- The sign-in view allows only `https://fusion-ledger.desase0175.workers.dev` inside the app. Other HTTP(S) and `mailto:` links open in the default Windows app.
- Pop-ups are controlled and permission requests are denied. DevTools and host objects are disabled in both WebViews. WebMessage is disabled in the sign-in WebView and enabled only in the hidden bridge, which accepts messages only from the exact bridge document and sends only a fixed list of commands. Certificate errors are never bypassed.
- The app injects no scripts or CSS into any page. It never reads or logs cookies, request headers or other secrets; bridge results are passed through the page's own filtering of sensitive keys.

## Known limitations

- Sign out revokes the server session through the bridge when it is connected, then clears the WebView2 cookies and site data. If the bridge is unavailable, only the local data is cleared and the server session expires on its own.
- Commit history and Server maintenance are read-only; Administration has only the Members account writes (approve / reject / suspend, reset code) and its other changes stay on the web; Projects has the work reservation and publish writes only; Project management has the web's management writes (Discord server/channel discovery and App setup stay on the web); Profile settings has icon change/removal and password change. There are no notifications, tray icon or background activity.

## Development

This application is vibe-coded using Claude Opus 5.5 and OpenAI GPT-5.6 Sol and Luna. Hands-on application testing is performed by me, the project owner.

## Versioning

The `<Version>` element in `FusionLedger.Windows.csproj` defines the app version. `Package.appxmanifest` uses the matching four-part package version (for example `0.14.8.0`). See [VERSION.md](VERSION.md) for the policy and [CHANGELOG.md](CHANGELOG.md) for release notes.

## License

The source code is licensed under the [MIT License](LICENSE).

The icons and logos in `Assets/` and the MolHub name and logo are not covered by the MIT License; all rights reserved. Replace them in forks and redistributed builds.

Third-party packages (Microsoft.WindowsAppSDK, Microsoft.Web.WebView2, Microsoft.Windows.SDK.BuildTools) are used under their own licenses.
