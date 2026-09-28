@AGENTS.md

# MolHub for Windows — Claude Code notes

`AGENTS.md` (imported above) holds the rules shared with Codex: roles, Git, hard rules, the Web-repository boundary, shared work plan, handoff and review. This file adds only what is specific to Claude Code. Stack: .NET `net10.0-windows10.0.19041.0`, Microsoft.WindowsAppSDK 2.5.1, Microsoft.Web.WebView2 1.0.3719.77.

## Start of every session

1. `git status --short --untracked-files=all`, `git branch --show-current`, `git log -3 --oneline`, `Get-AppxPackage -Name FusionLedger.Windows` (the owner may have committed or installed since the last session; in an Orca worktree this checkout's Git state differs from the main checkout).
2. Read only the `## Now` section of `.claude_stuff/WORK_PLAN.md` (it summarizes state, next work and open checks). Read other sections or docs only when the task needs them.
3. Read the Web repository only when the web contract is needed, at the location from the request or confirmed with the owner (see `AGENTS.md`).
4. No Claude subagents (owner decisions 2026-09-27/28: implementation, structure review, review/tests and device checks come from Codex workers in Orca). Do not launch Claude subagents, including the built-in general-purpose, Explore and Plan agents, and never `orca orchestration worker-start --agent claude`; plan-mode instructions to launch them do not apply here. Investigate directly.
5. Check the Codex workers: `orca terminal list --worktree current` (role titles: Implementer, Structure, Review-Test) and, when a Run is active, `orca orchestration run-current` / `orca orchestration check`.

## Reading policy (keep context small)

- Search first (Grep/Glob for the identifier, key or heading), then Read only the hit and the lines around it (`offset`/`limit`). Never read `DESIGN.md`, `CHANGELOG.md`, `README.md`, `Program.cs` or the `.resw` files in full; grep the heading or key.
- Folder-specific rules load automatically from `.claude/rules/` when you work on matching files (see the index below); do not open them all up front.

## Working rules

- Git follows `AGENTS.md`. Leave changes uncommitted, list the changed files and propose a `vX.Y.Z, summary` commit message.
- **This session is the coordinator** (roles in `AGENTS.md`): talks with the owner, decides specs (AskUserQuestion), researches (including the Web contract), writes specs in `.claude_stuff/specs/`, `DESIGN.md` and `WORK_PLAN.md`, keeps the instruction files and release docs (CHANGELOG/README/VERSION.md), writes Orca Task specs and the review handoff, hands over the install command and the "Authorized writes" list, and reports. It does not edit app code, `.resw` files, tests, csproj or manifest.
- **Codex workers do the rest** through `orca orchestration worker-start --terminal <handle> --worktree current`: the implementer writes code, strings and policy tests and runs the full verification (restore, policy tests, one x64 Debug build, `git diff --check`, `-WhatIf`, DLL hash, glyph and line-ending scans); the structure reviewer reports layout proposals (read-only); the reviewer/tester reviews and reruns the tests independently and does the device check. Verify worker claims you rely on (for example re-run `git status` and the fingerprint) instead of adopting them.
- **What each kind of change needs:** app change → spec → (structure review when layout changes) → implementer Task → release docs → reviewer Task → fixes by the implementer → re-review until no blocking/should-fix → owner installs → device check by the reviewer/tester (production writes only from the "Authorized writes" list) → report tests / build / device separately and record the result in WORK_PLAN.md. Instruction-only change → no app build; check for contradictions and stale names instead.
- Nobody reverts or overwrites changes they did not make; one writer at a time on a checkout (edit only while no implementer Task is running).

## Build and verify

Commands are in `AGENTS.md` (run from this checkout's root). UI tests, automated device checks and CI are required parts of verification; the CI workflow runs restore, the policy tests and the x64 Debug build on a Windows runner without signing secrets. No UI test project exists yet, so state which parts were automated. The device check is the reviewer/tester's Task (`AGENTS.md` "Device check"); include the former procedure's pointer (`git show 2614892:.claude/agents/device-verifier.md`) in that Task spec.

## Index (read on demand)

| What | Where |
| --- | --- |
| Current state, next work, open checks | `.claude_stuff/WORK_PLAN.md` `## Now` (rest of that file: open checks, known issues, what exists) |
| Session log, old routine, reference links | `.claude_stuff/reference/HISTORY.md` |
| Design contract per page | `.claude_stuff/DESIGN.md` (grep the `## ` heading) |
| User-facing docs / release notes | `README.md`, `CHANGELOG.md`, `VERSION.md` (grep) |
| Auto-loaded folder rules | `.claude/rules/`: `app-common` (app folders), `auth-bridge` (Auth/Bridge/App.xaml.cs), `shell` (Shell/Settings), `pages-common` (Pages), `pages-projects`, `pages-management`, `pages-approval-maintenance`, `pages-profile`, `tests` (tests), `strings` (Strings), `release` (csproj/manifest/VERSION/CHANGELOG/README) |
| Removed subagents (history only) | Deleted 2026-09-27; last versions in commit `2614892` (`.claude/agents/`). Older retired definitions: `.claude_stuff/retired-agents/` |
