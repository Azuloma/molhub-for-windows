---
paths:
  - "Pages/Dashboard/**"
  - "Pages/Projects/**"
  - "Pages/History/**"
---

# Dashboard, Projects, Commit history (moved from CLAUDE.md)

- `DashboardView.cs` (UI) + `DashboardModel.cs` (pure parsing/counts/format/error mapping). The Dashboard shows only `/api/v1/dashboard` data and renders no actions that lack a native implementation.
- `ProjectsView.cs` (list, screen stack, breadcrumb, errors) + `ProjectsView.Project.cs` (project overview, commits tab, commit detail) + `ProjectsModel.cs` (pure). The Commits tab and Commit history share `BuildCommitTimeline` (filters, day groups, Load more).
- Projects has only the v0.13.0 work writes (Start work / Cancel work / Publish version in `ProjectsView.Work.cs`, rules in `WorkModel`: each write sent once, `Unknown` → refresh and explain, never resend; publish confirmed first). An open project screen reloads after a write elsewhere via `Screen.WorkStale`. No management or settings controls here; share links open only via `ProjectsModel.SafeShareUri` in the browser.
- Commit history is a second `ProjectsView` with `ProjectsRoot.History` (`Pages/History/ProjectsView.History.cs` + pure `HistoryModel.cs`), so its commits/projects open on its own stack. It is read-only (`history`, author-scoped on the server, summary counts only from the server).
