---
paths:
  - "Shell/**"
  - "Settings/**"
---

# Shell and settings

## Files

- `Shell/` — `MainWindow.xaml(.cs)` (native shell: WinUI `TitleBar`, NavigationView, search, flyouts, bridge connection, sign-out), `NativePage.cs` (page catalog and role gating: `NativePageCatalog.IsAvailable` / `CanAdminister`), `TitleBarLayoutPolicy.cs`, `FlyoutPlacementPolicy.cs`, `ThinAcrylicBackdrop.cs` (flyouts), `CaptionButtonTheme.cs` (system caption buttons follow the app theme via `AppWindowTitleBar.PreferredTheme`, both windows), `WindowIcon.cs`, `AppIconAssets.cs`.
- `Settings/` — `SettingsPolicy.cs` (settings keys, search sizing), `ThemeService.cs`, `RuntimeVersionInfo.cs` (version info including the bridge connection state).

## Rules beyond `CLAUDE.md` "Hard rules"

- Pages are built in code and assigned to the stretched `ContentFrame` `ContentControl`. (`Frame.Navigate` crashed with an access violation in v0.5.0/v0.5.1; v0.5.2 switched to `ContentFrame.Content`.)
- Back: `GoBack` first asks the current page's stack (`CurrentStackPage`; pages with their own stack implement `IScreenStack`) `TryGoBack()`; `UpdateBackButton` shows Back while that stack has an open screen.
- Administration appears only for an approved `admin` (`CanAdminister`); Server maintenance is shown to everyone; non-approved accounts get no workspace pages (`IsAvailable`).
- The title-bar bell is for notifications and stays "not connected" (announcements belong to Server maintenance). Let `TitleBar` own caption buttons and insets.
- Settings fallbacks: `ui.language` → `en-US`, `ui.theme` → `System`.
