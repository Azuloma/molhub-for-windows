---
paths:
  - "Auth/**"
  - "Bridge/**"
  - "App.xaml.cs"
---

# Sign-in, data bridge and sign-out

The rules themselves are in `CLAUDE.md` "Hard rules" (login boundary, data bridge, sign-out order). This file adds the file map and the details not stated there.

## Files

- `App.xaml.cs` — window lifecycle: `LoginWindow` first, `MainWindow` after authentication, sign-out sequencing.
- `Auth/` — `LoginWindow.xaml(.cs)` (the only visible WebView2, compact dialog), `LoginWindowLayoutPolicy.cs`, `NavigationPolicy.cs` (pins the production origin), `ProfilePayloadParser.cs` + `ProfileResponseCoordinator.cs` (bounded parsing and ordering of `/api/me` responses).
- `Bridge/` — `WebViewProfile.cs` (the single shared WebView2 environment), `WebBridgeClient.cs` (hidden data-bridge host), `BridgePolicy.cs` (pure rules: document/source checks, command allowlist, request/result JSON, timeouts, `BridgeOutcome`).

## Details

- Login: other http(s)/mailto links open externally; `/api/me` bodies are parsed with bounds.
- Bridge: `BridgePolicy.BridgeUri` is `/webview-bridge` (`/webview-bridge.html` 307-redirects there). The bridge runs a hidden controller in its own invisible window; it and `LoginWindow` share `WebViewProfile.GetEnvironmentAsync()`. No script injection, host objects or cookie access.
