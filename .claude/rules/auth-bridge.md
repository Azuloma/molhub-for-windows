---
paths:
  - "Auth/**"
  - "Bridge/**"
  - "App.xaml.cs"
---

# Sign-in, data bridge and sign-out — details (moved from CLAUDE.md)

The short form of these rules is in `AGENTS.md` ("Hard rules", imported by `CLAUDE.md`); this file holds the file map and the full wording.

- `App.xaml.cs` — window lifecycle: `LoginWindow` first, `MainWindow` after authentication, sign-out sequencing.
- `Auth/` — `LoginWindow.*` (the visible sign-in WebView2, compact dialog, `LoginWindowLayoutPolicy.cs`), `NavigationPolicy.cs` pins the production origin; `ProfilePayloadParser.cs` and `ProfileResponseCoordinator.cs` parse and order `/api/me` responses.
- `Bridge/` — `WebViewProfile.cs` (the single shared WebView2 environment), `WebBridgeClient.cs` (hidden data-bridge host), `BridgePolicy.cs` (pure bridge rules: document/source checks, command allowlist, request/result JSON, timeouts, `BridgeOutcome`).

## Login boundary

Allow only the HTTPS production origin in-app; open other http(s)/mailto externally; deny permissions; keep DevTools, host objects and WebMessage disabled; observe only exact GET `/api/me` with bounded parsing. Never inject script/CSS, read/log cookies or headers, or persist secrets.

## Data bridge

WebMessage is enabled only in `WebBridgeClient`, which may load only `BridgePolicy.BridgeUri` (`/webview-bridge`; `/webview-bridge.html` 307-redirects there), must check `IsTrustedSource(e.Source)` on every message, sends only the `BridgePolicy` allowlist and correlates by `requestId`. Treat `BridgeOutcome.Unknown` writes as "refresh and let the user decide", never resend. No script injection, host objects or cookie access. `LoginWindow` has the only visible WebView2; the bridge uses a hidden controller in its own invisible window; both share `WebViewProfile.GetEnvironmentAsync()`.

## Sign-out order

Create the replacement `LoginWindow`, clear cookies and site data through the WebView2 profile APIs, navigate only after the clear succeeds, then close `MainWindow`. Never delete the profile directory directly. Before that, `MainWindow` calls the bridge `logout` (server revocation) when the bridge is connected.
