---
paths:
  - "Pages/**"
---

# Data pages — common rules (moved from CLAUDE.md)

- Layout: `Pages/Common/` (`PageParts` — cards, ID chip, storage note, avatar, `TwoColumnLayout`, `InlineWrapPanel`; `AvatarImage` — avatar decode that survives theme changes; `IScreenStack` — pages with their own Back stack), then one folder per page: `Dashboard/`, `Projects/`, `History/`, `Approval/`, `Maintenance/`, `Management/`, `Profile/`. Folder-specific rules live in `.claude/rules/pages-*.md`.
- Data pages follow the Dashboard pattern: pure `*Model` (parse the `/api/v1` DTO with bounds, compute counts, map `BridgeResult` errors) linked into the policy tests, a code-built `*View` with loading / InfoBar error states (connection, session ended → sign in again, pending approval, maintenance, rate limit, unexpected), and the external-storage privacy note wherever share links appear. Match the web wording (the Web repository's `public\*.js`; its location as described in `CLAUDE.md`). Search boxes filter on submit (Enter / search icon), like the web's submit forms, not while typing.
- A centered page column is built with `PageParts.CenteredPage(root, maxWidth)`; never put `MaxWidth` with Stretch (explicit or default) alignment on the page root (v0.14.4: WinUI placed a page with only short text off-center and clipped it).
- **No fake data:** show only server data; no fabricated counts, records, notifications or non-functional controls. Administration stays an honest "not connected" placeholder.
- **Writes:** each write is sent once through the app-wide `WriteGate`; `Unknown` → refresh and explain, never resend; destructive writes are confirmed first (ContentDialog, Cancel as default); after a write other pages are marked stale (`MainWindow.OnWorkChanged`).
