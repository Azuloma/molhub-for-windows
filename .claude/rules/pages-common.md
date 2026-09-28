---
paths:
  - "Pages/**"
---

# Data pages — common rules

- `Pages/Common/`: `PageParts.cs` (cards, ID chip, storage note, avatar, `TwoColumnLayout`, `InlineWrapPanel`, `CenteredPage`), `AvatarImage.cs` (avatar decode that survives theme changes), `AnnouncementCard.cs`, `IScreenStack.cs` (pages with their own Back stack). Every other folder is one page; its rule file is listed in the `CLAUDE.md` repository map.
- Pattern (Dashboard): pure `*Model` (parse the `/api/v1` DTO with bounds, compute counts, map `BridgeResult` errors) linked into the policy tests, plus a code-built `*View` with loading / InfoBar error states (connection, session ended → sign in again, pending approval, maintenance, rate limit, unexpected) and the external-storage privacy note wherever share links appear.
- Match the web wording (the Web repository's `public\*.js`). Search boxes filter on submit (Enter / search icon), not while typing.
- Center a page column with `PageParts.CenteredPage(root, maxWidth)`; never put `MaxWidth` with Stretch alignment on the page root (v0.14.4: a page with only short text was placed off-center and clipped).
- Writes (beyond `CLAUDE.md` "No fake data"): confirm destructive writes with a ContentDialog whose default is Cancel; after a write, other pages are marked stale via `MainWindow.OnWorkChanged`.
