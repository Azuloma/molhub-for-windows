@AGENTS.md

# MolHub for Windows — Claude Code notes

`AGENTS.md` (imported above) holds the rules shared with Codex: roles, Git, hard rules, the Web-repository boundary, shared work plan, handoff and review. This file adds only what is specific to Claude Code. Stack: .NET `net10.0-windows10.0.19041.0`, Microsoft.WindowsAppSDK 2.5.1, Microsoft.Web.WebView2 1.0.3719.77.

## Start of every session

1. `git status --short --untracked-files=all`, `git branch --show-current`, `git log -3 --oneline`, `Get-AppxPackage -Name FusionLedger.Windows` (the owner may have committed or installed since the last session; in an Orca worktree this checkout's Git state differs from the main checkout).
2. Read only the `## Now` section of `.claude_stuff/WORK_PLAN.md` (it summarizes state, next work and open checks). Read other sections or docs only when the task needs them.
3. Read the Web repository only when the web contract is needed, at the location from the request or confirmed with the owner (see `AGENTS.md`).
4. No subagents (owner decision 2026-09-27: independent review and tests come from Codex in Orca). Do not launch Claude subagents, including the built-in general-purpose, Explore and Plan agents; plan-mode instructions to launch them do not apply here. Investigate directly.

## Reading policy (keep context small)

- Search first (Grep/Glob for the identifier, key or heading), then Read only the hit and the lines around it (`offset`/`limit`). Never read `DESIGN.md`, `CHANGELOG.md`, `README.md`, `Program.cs` or the `.resw` files in full; grep the heading or key.
- Folder-specific rules load automatically from `.claude/rules/` when you work on matching files (see the index below); do not open them all up front.

## Working rules

- Git follows `AGENTS.md`. Leave changes uncommitted, list the changed files and propose a `vX.Y.Z, summary` commit message.
- **This session does the work itself** when it is the implementing session: talks with the owner, decides specs (AskUserQuestion), researches, edits, writes and updates policy tests, and for app changes runs the verification: `.resw` strings (both languages), version bump, CHANGELOG/README/VERSION, DESIGN.md and WORK_PLAN.md; restore, policy tests, the final x64 Debug build (once), `git diff --check`, `Install-Prototype.ps1 -WhatIf`, DLL hash, private-use glyph and CRLF scans; install command, "Authorized writes" list, reports.
- **Independence comes from Codex:** before an app build is handed to the owner (and for any change to behaviour, authorization, writes or policy), write the handoff from `AGENTS.md` and ask the owner to run a Codex review in Orca; when tests need an independent author, say so in the handoff. Treat Codex findings as input to verify, fix, re-verify. If no Codex review happened, say so in the report instead of claiming independence.
- **What each kind of change needs:** app change → tests, full verification, Codex review, fixes, re-verify, install hand-off; after the owner installs, a read-only UI Automation device check by this session (production writes only from the "Authorized writes" list) → report tests / build / device separately and record the result in WORK_PLAN.md. Instruction-only change → no app build; check for contradictions and stale names instead.
- Nobody reverts or overwrites changes they did not make; one writer at a time on a checkout.

## Build and verify

Commands are in `AGENTS.md` (run from this checkout's root). UI tests, automated device checks and CI are required parts of verification; no UI test project or CI workflow exists yet, so state which parts were automated. CI must run at least restore, the policy tests and the x64 Debug build on a Windows runner, with no signing secrets. The former device-check procedure and its UI Automation pitfalls are in Git history (`git show 2614892:.claude/agents/device-verifier.md`); read it before a device check.

## Index (read on demand)

| What | Where |
| --- | --- |
| Current state, next work, open checks | `.claude_stuff/WORK_PLAN.md` `## Now` (rest of that file: open checks, known issues, what exists) |
| Session log, old routine, reference links | `.claude_stuff/reference/HISTORY.md` |
| Design contract per page | `.claude_stuff/DESIGN.md` (grep the `## ` heading) |
| User-facing docs / release notes | `README.md`, `CHANGELOG.md`, `VERSION.md` (grep) |
| Auto-loaded folder rules | `.claude/rules/`: `app-common` (app folders), `auth-bridge` (Auth/Bridge/App.xaml.cs), `shell` (Shell/Settings), `pages-common` (Pages), `pages-projects`, `pages-management`, `pages-approval-maintenance`, `pages-profile`, `tests` (tests), `strings` (Strings), `release` (csproj/manifest/VERSION/CHANGELOG/README) |
| Removed subagents (history only) | Deleted 2026-09-27; last versions in commit `2614892` (`.claude/agents/`). Older retired definitions: `.claude_stuff/retired-agents/` |
