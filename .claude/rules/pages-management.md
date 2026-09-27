---
paths:
  - "Pages/Management/**"
---

# Project management (moved from CLAUDE.md)

- `ManagementView`/`ManagementModel` (v0.12.0, reads `manageProjects`/`manageProject`; approved owners and site admins) and `ManageWriteModel` (v0.14.0 writes).
- Writes: add/remove member, release reservation, owner-only delete/restore request, Discord connect/request with typed IDs and stop, site-admin owner assignment — each shown only where the web shows it, sent once through the app-wide `WriteGate`; remove/release/stop confirmed first. Discord server/channel discovery and App setup stay on the web.
