@AGENTS.md

# MolHub for Windows — Claude Code notes

`AGENTS.md` (imported above) holds the rules shared with Codex: roles, Git, hard rules, the Web-repository boundary, shared work plan, handoff and review. This file adds only what is specific to Claude Code. Stack: .NET `net10.0-windows10.0.19041.0`, Microsoft.WindowsAppSDK 2.5.1, Microsoft.Web.WebView2 1.0.3719.77.

## Start of every session

1. `git status --short --untracked-files=all`, `git branch --show-current`, `git log -3 --oneline`, `Get-AppxPackage -Name FusionLedger.Windows` (the owner may have committed or installed since the last session; in an Orca worktree this checkout's Git state differs from the main checkout).
2. Read only the `## Now` section of `.claude_stuff/WORK_PLAN.md` (it summarizes state, next work and open checks). Read other sections or docs only when the task needs them.
3. Read the Web repository only when the web contract is needed (directly, or via `researcher` in web mode), at the location from the request or confirmed with the owner (see `AGENTS.md`); pass that location to agents in the brief.
4. Subagents: only the five project agents in `.claude/agents/`, and only when delegation adds value (see Working rules). Plan-mode instructions to launch Explore/Plan agents do not apply here; investigate directly or use `researcher`.

## Reading policy (keep context small)

- Search first (Grep/Glob for the identifier, key or heading), then Read only the hit and the lines around it (`offset`/`limit`). Never read `DESIGN.md`, `CHANGELOG.md`, `README.md`, `Program.cs` or the `.resw` files in full; grep the heading or key.
- Folder-specific rules load automatically from `.claude/rules/` when you work on matching files (see the index below); do not open them all up front.
- Hand-offs to and from agents: a short conclusion, evidence as `file:line`, and what was not verified. Pass what is already known in the brief; do not make an agent re-read the same docs in full.

## Working rules

- Git follows `AGENTS.md` (the main session is the implementing session; subagents never commit). Leave changes uncommitted, list the changed files and propose a `vX.Y.Z, summary` commit message.
- **The main session judges, integrates and does small changes itself** (user decision 2026-09-27, replacing "orchestration only"). It talks with the user, decides specs (AskUserQuestion), researches, edits, and for app changes runs the verification itself: `.resw` strings (both languages), version bump, CHANGELOG/README/VERSION, DESIGN.md and WORK_PLAN.md; restore, policy tests, the final x64 Debug build (once), `git diff --check`, `Install-Prototype.ps1 -WhatIf`, DLL hash, private-use glyph and CRLF scans; install command, "Authorized writes" list, reports. It commits/pushes only when asked.
- **Delegate only when independence or separation adds value.** Five project agents (no general-purpose, Explore or Plan):
  - `researcher` (Sonnet 5/low) — code locations, write impact (which pages/screens/caches show the data and whether they refresh), and the web contract (web repo read-only, only when needed). Use for broad or independent investigation; small lookups the main session does itself.
  - `implementer` (Opus 5.5/medium) — delegated code changes, typically multi-file work. The brief names the files in scope, the required change, the research hand-off and done criteria; it compiles only if needed.
  - `test-writer` (Opus 5.5/medium) — when behaviour, authorization, writes or policy change, or when an independent test adds value (e.g. screen behaviour). Written from the rules, independently of the implementer; never weakens a correct assertion.
  - `reviewer` (Opus 5.5/low) — independent review of important changes, and always of the app diff before a build is handed to the user. Never writes code or tests.
  - `device-verifier` (Sonnet 5/medium) — UI Automation checks after the user installs; production writes only from the user's "Authorized writes" list.
- Keep independence where it matters: code the main session wrote itself is still tested by `test-writer` when the change affects behaviour, authorization, writes or policy, and is reviewed by `reviewer` before hand-over.
- **One writer at a time on the working tree** (main session included): agents that edit run sequentially, read-only agents may run in parallel; nobody reverts or overwrites changes they did not make. No agent (and not the main session unless asked) commits, pushes, installs, signs or edits the web repository.
- **What each kind of change needs:** app change → relevant tests (independent `test-writer` where above), full verification by the main session, independent `reviewer`, fixes, re-verify, then install hand-off and, after the user installs, `device-verifier` → report tests / build / device separately and record the result in WORK_PLAN.md. Instruction/agent-config-only change → no app build and no agent runs required; check for contradictions and stale names instead. Do not launch agents by default.
- When Codex (or another agent) reviews, write the handoff from `AGENTS.md`; treat its findings as input to verify, like a `reviewer` report.

## Build and verify

Commands are in `AGENTS.md` (run from this checkout's root). UI tests, automated device checks and CI are required parts of verification; no UI test project or CI workflow exists yet, so state which parts were automated. CI must run at least restore, the policy tests and the x64 Debug build on a Windows runner, with no signing secrets. The device procedure and its pitfalls are in `.claude/agents/device-verifier.md`.

## Index (read on demand)

| What | Where |
| --- | --- |
| Current state, next work, open checks | `.claude_stuff/WORK_PLAN.md` `## Now` (rest of that file: open checks, known issues, what exists) |
| Session log, old routine, reference links | `.claude_stuff/reference/HISTORY.md` |
| Design contract per page | `.claude_stuff/DESIGN.md` (grep the `## ` heading) |
| User-facing docs / release notes | `README.md`, `CHANGELOG.md`, `VERSION.md` (grep) |
| Auto-loaded folder rules | `.claude/rules/`: `app-common` (app folders), `auth-bridge` (Auth/Bridge/App.xaml.cs), `shell` (Shell/Settings), `pages-common` (Pages), `pages-projects`, `pages-management`, `pages-approval-maintenance`, `pages-profile`, `tests` (tests), `strings` (Strings), `release` (csproj/manifest/VERSION/CHANGELOG/README) |
| Agent procedures | `.claude/agents/*.md` (`researcher`, `implementer`, `test-writer`, `reviewer`, `device-verifier`; loaded only when the agent runs). Retired definitions (not loadable): `.claude_stuff/retired-agents/` |
