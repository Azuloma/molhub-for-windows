---
paths:
  - "Pages/Management/**"
---

# Project management

- Files: `ManagementView.cs`, `ManagementModel.cs` (pure; reads `manageProjects` / `manageProject`; approved owners and site admins), `ManageWriteModel.cs` (pure write rules).
- Writes: add/remove member, release reservation, owner-only delete/restore request, Discord connect/request with typed IDs and stop, site-admin owner assignment — each shown only where the web shows it; remove/release/stop confirmed first. Discord server/channel discovery and App setup stay on the web.
