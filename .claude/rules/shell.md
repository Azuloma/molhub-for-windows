---
paths:
  - "Shell/**"
  - "Settings/**"
---

# Shell and settings (moved from CLAUDE.md)

- `Shell/` — `MainWindow.*` (native shell: WinUI `TitleBar`, NavigationView, search, flyouts, bridge connection, sign-out), `NativePage.cs` (pages and role gating), `TitleBarLayoutPolicy`, `FlyoutPlacementPolicy`, `ThinAcrylicBackdrop` (flyouts), `CaptionButtonTheme` (keeps the system caption buttons in the app theme via `AppWindowTitleBar.PreferredTheme` for both windows), `WindowIcon`, `AppIconAssets`.
- `Settings/` — `SettingsPolicy.cs`, `ThemeService.cs`, `RuntimeVersionInfo.cs` (settings, theme, search sizing, version info including the bridge connection state).
- **MainWindow never hosts WebView2 in its XAML.** Pages are built in code and assigned to the `ContentFrame` `ContentControl` (stretched). Do not use `Frame.Navigate` for section changes; v0.5.0/v0.5.1 crashed with an access violation inside it, fixed in v0.5.2 by setting `ContentFrame.Content`.
- Back: MainWindow's `GoBack` asks the current page's stack (`CurrentStackPage`: `_projects` or `_commitHistory`; pages with their own stack implement `IScreenStack`) `TryGoBack()` first; `UpdateBackButton` shows Back while a project/commit is open on that stack.
- Administration appears only for an approved `admin` user (`NativePageCatalog.CanAdminister`); Server maintenance is shown to everyone; non-approved accounts get no workspace pages (`NativePageCatalog.IsAvailable`). Hiding UI is not an authorization boundary.
- The title-bar bell is for notifications and stays "not connected" (announcements belong to Server maintenance, not the bell). Let `TitleBar` own caption buttons and insets.
- Settings persist only the fixed `ui.language` / `ui.theme` keys with validated fallbacks (`en-US`, `System`).
