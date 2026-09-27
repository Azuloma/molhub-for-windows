---
paths:
  - "Pages/Approval/**"
  - "Pages/Maintenance/**"
---

# Approval screen and Server maintenance (moved from CLAUDE.md)

- `Pages/Approval/` (`ApprovalView`/`ApprovalModel`, v0.11.0): the approval-waiting screen has only Refresh (`session` read) and Announcements (opens Server maintenance); non-approved accounts get no workspace pages (`NativePageCatalog.IsAvailable`).
- `Pages/Maintenance/` (`MaintenanceView`/`MaintenanceModel`, v0.11.1): Server maintenance is read-only for every account (`maintenance` state + `announcements`); no start/complete controls until the admin writes exist.
