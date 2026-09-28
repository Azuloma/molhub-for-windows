# MolHub for Windows — rules for every coding agent

Native-first WinUI 3 client (packaged x64, .NET 10) for the MolHub web app. Internal identifiers keep the `FusionLedger` name (package identity, namespace, WebView2 profile folder, production host). Paths here are relative to this repository's root. Claude Code reads this file through `CLAUDE.md`; Codex reads it directly.

## Roles

Default split (owner decision 2026-09-28). The owner's request may change roles; follow the role the request or the Orca Task gives you.

| Role | Agent | May edit | Never |
| --- | --- | --- | --- |
| Coordinator | Claude Code | `AGENTS.md`, `CLAUDE.md`, `.claude/rules/*.md`, `.claude_stuff/` (only writer of `WORK_PLAN.md`, `DESIGN.md`, specs), `CHANGELOG.md`, `README.md`, `VERSION.md` | app code, tests, csproj/manifest |
| Implementer | Codex (GPT-6-Luna high) | app code, `Strings/*.resw`, `tests/`, csproj/manifest version | `.claude_stuff/`, instruction files, release docs |
| Structure reviewer | Codex (GPT-6-Luna high) | nothing: reports folder/file-layout proposals; the coordinator decides, the implementer moves files | any edit |
| Reviewer / tester | Codex (GPT-6-Sol medium) | nothing: reviews, runs restore/tests/build, does the read-only device check | any edit, install, password, sign-out, writes not on the "Authorized writes" list |

- The coordinator writes specs and Task specs, orders the work and records results; it does not edit app code or tests.
- The implementer writes the code and the policy tests and runs the full verification (see "Verify"); the reviewer/tester checks both independently.
- One writer per checkout at a time: the coordinator edits only while no implementer Task is running. No Claude subagents; Orca workers are Codex only.

## Orchestration (Orca)

- The coordinator binds one Orca Run per piece of work (`orca orchestration run-create`) and starts each Task on the role's existing terminal (`orca orchestration worker-start --terminal <handle> --worktree current`).
- Every Task spec names Target, Change, Constraints, Ownership (what the worker may edit) and Observable acceptance; review Tasks carry the handoff below.
- A worker does only its Task, asks blocking questions with the preamble's `ask` command, and ends with exactly one `worker_done` (`--outcome succeeded|failed`, `--files-modified` when it edited files).

## Hard rules (every agent, every role)

- **Git:** never run `git commit`, `git push` or any other history-changing command (amend, merge, rebase, reset, tag, stash, checkout of other branches). The owner commits and pushes. Only the coordinator may commit/push, and only when the owner explicitly asks in chat; never a Codex worker or subagent. Never revert or overwrite changes you did not make.
- **No signing or distribution work.** No certificates, signing or release distribution; the unsigned Debug MSIX is for local development only.
- **The owner installs packages.** Never run `Install-Prototype.ps1` without `-WhatIf`, `Add-AppxPackage` or `Remove-AppxPackage`. Build, run `-WhatIf`, hand over the install command and wait. After "installed", confirm `Get-AppxPackage` shows the new version and the DLL hash matches the build.
- **Never type the owner's password.** Sign-in, sign-out and password changes need the owner.
- **Production writes only with the owner's explicit authorization** (the "Authorized writes" list, per action, per session). A safety-check refusal is reported, never worked around.
- **MainWindow never hosts WebView2** in its XAML; no `Frame.Navigate` for section changes (use `ContentFrame.Content`).
- **Login boundary:** only the HTTPS production origin in-app; other links open externally; deny permissions; DevTools, host objects and WebMessage disabled in `LoginWindow`; observe only exact GET `/api/me`. Never inject script/CSS, read/log cookies or headers, or persist secrets.
- **Data bridge:** WebMessage only in `WebBridgeClient`, which loads only `BridgePolicy.BridgeUri`, checks `IsTrustedSource(e.Source)` on every message, sends only the `BridgePolicy` allowlist and correlates by `requestId`. `BridgeOutcome.Unknown` writes → refresh and let the owner decide, never resend.
- **Sign-out order:** bridge `logout` when connected → replacement `LoginWindow` → clear cookies/site data via WebView2 profile APIs → navigate only after the clear → close `MainWindow`. Never delete the profile directory directly.
- **No fake data:** no fabricated counts, records, notifications or non-functional controls; each write is sent once through the app-wide `WriteGate`, destructive writes are confirmed first. Hiding UI is not an authorization boundary (Administration only for an approved `admin`).
- Settings persist only `ui.language` / `ui.theme` with validated fallbacks. New strings go into both `Strings/*/Resources.resw` files.

## Other repository (MolHub Web)

The Web repository (MolHub) owns `/api/v1`, `docs/WINDOWS_API.md`, `docs/openapi.yaml` and the bridge page; it is read-only from here unless the owner asks. Its location differs per checkout (Orca worktrees live under Orca's workspace folder, not next to this repo): take it from the request or confirm it (ask) before reading; never assume a sibling path. In the owner's main checkout it happens to be `..\MolHub.Web`.

## Where things are (read on demand)

- Current state, next work: `.claude_stuff/WORK_PLAN.md` `## Now`. Page design contract: `.claude_stuff/DESIGN.md` (grep the `## ` heading). `.claude_stuff/` is not in Git; in an Orca worktree it is a link to the owner's main checkout. If it is missing, say so instead of guessing.
- Folder rules in `.claude/rules/` apply to every agent. Claude Code loads them by path; **Codex must open the matching file(s) itself** for the folders it changes or reviews:

  | Folder / files | Rule files |
  | --- | --- |
  | `App.xaml(.cs)`, `Auth/`, `Bridge/`, `Shell/`, `Settings/`, `Pages/` | `app-common.md` |
  | `Auth/`, `Bridge/`, `App.xaml.cs` | `auth-bridge.md` |
  | `Shell/`, `Settings/` | `shell.md` |
  | `Pages/` | `pages-common.md`, plus `pages-projects.md` (Dashboard/Projects/History), `pages-management.md`, `pages-approval-maintenance.md`, `pages-profile.md` |
  | `Strings/` | `strings.md` |
  | `tests/` | `tests.md` |
  | `*.csproj`, `Package.appxmanifest`, `VERSION.md`, `CHANGELOG.md`, `README.md` | `release.md` |

- **Shared work plan:** in Orca worktrees `.claude_stuff/` is one live copy shared by all checkouts. Only the coordinator updates `WORK_PLAN.md`, one writer at a time; workers never edit it and put results in their `worker_done` report. It never replaces a checkout's own Git state: always read your own `git status --short --untracked-files=all`, branch and HEAD; the plan's Git line describes only the main checkout's `main`.
- Remove Orca worktrees only through Orca (`orca worktree rm` or the UI), never by deleting the folder yourself: its `.claude_stuff` is a junction to the main checkout, and a recursive delete could follow it. Orca 1.4.215's normal removal was checked on 2026-09-27 and left the main checkout's `.claude_stuff` intact; re-check after an Orca upgrade.

## Verify (implementer)

```powershell
dotnet restore
dotnet run --project tests\FusionLedger.Windows.PolicyTests\FusionLedger.Windows.PolicyTests.csproj
dotnet build FusionLedger.Windows.csproj -p:Platform=x64 -p:Configuration=Debug
.\Install-Prototype.ps1 -WhatIf -PackageDirectory ".\AppPackages\FusionLedger.Windows_<version>_x64_Debug_Test"
git diff --check
```

Run from this checkout's root. Also report the DLL SHA-256 of the build and the private-use glyph and line-ending scans. Report tests, build and device behavior separately; a passing build is not proof of runtime behavior. CI (`.github/workflows/windows-build.yml`) repeats restore, policy tests and the x64 Debug build on push and pull request.

## Device check (reviewer / tester)

Only after the owner has installed the build. Read the former procedure and its UI Automation pitfalls first: `git show 2614892:.claude/agents/device-verifier.md`. Confirm `Get-AppxPackage` and the DLL hash against the build, use only read-only UI Automation unless the Task lists "Authorized writes", never sign out or type a password, restore window size/theme/page, and report pass / fail / not performed per check.

## Handoff to a reviewer

```text
Repo / checkout: MolHub.Windows at <worktree path>, branch <name>
Range: <base>..<head>   (or: uncommitted on top of <HEAD>)
Uncommitted: `git status --short --untracked-files=all` output
Fingerprint (uncommitted only): git diff HEAD --binary --output=<scratch>\review.patch; git hash-object <scratch>\review.patch
  and for each untracked file from `git ls-files -o --exclude-standard`: <path> <git hash-object path>
Ignored files that matter (e.g. .claude_stuff edits): <list or none>
Purpose: <one or two lines>
Contract / safety notes: <rules touched: bridge, login, writes, sign-out, strings ...>
Tests run: <command → result>; build: <result>; device: <result or not run>
Not verified: <list>
Reviewer focus: <optional>
```

Uncommitted and untracked changes exist only in the implementer's checkout: review them in that same worktree (open a terminal there), not in a new worktree. Committed work can be reviewed in any checkout at the given commits.

## Reviewing

1. Confirm the checkout: `git rev-parse --show-toplevel`, branch, HEAD, and for uncommitted work `git status --short --untracked-files=all` plus the fingerprint. On any mismatch with the handoff, stop and report.
2. Derive what the change must do from the owner's request, the hard rules above, the matching `.claude/rules/*.md`, `.claude_stuff/DESIGN.md` and (when a contract is involved) the Web repository's contract. Do not adopt the implementer's conclusions or test claims without checking them.
3. Read the diff in full; re-run the policy tests or read the assertions where you can.
4. Report findings as blocking / should-fix / nit with `file:line`, then what you did not verify. Do not edit files, the work plan or Git unless the request says so. The coordinator routes fixes to the implementer; the reviewer re-reviews until no blocking or should-fix finding remains.
