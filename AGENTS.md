# MolHub for Windows — rules for every coding agent

Native-first WinUI 3 client (packaged x64, .NET 10) for the MolHub web app. Internal identifiers keep the `FusionLedger` name (package identity, namespace, WebView2 profile folder, production host). Stack: .NET `net10.0-windows10.0.19041.0`, Microsoft.WindowsAppSDK 2.5.1, Microsoft.Web.WebView2 1.0.3719.77. Paths here are relative to this repository's root. Codex reads this file directly; Claude Code reads it through `CLAUDE.md`.

## Roles

- **Main — Codex, GPT-6 Sol / Medium:** works with the owner, plans, decides specs (asking when a decision is the owner's), assigns and monitors Codex subagents and independent Orca workers, integrates results, and edits Markdown documents including release records, `DESIGN.md` and `WORK_PLAN.md`. It may make read-only checks needed for coordination. It does not edit app or Web code, run tests or builds, or operate the app on the device.
- **Code Explorer — Codex subagent, GPT-5.6 Luna / Medium:** start only when a bounded read-only investigation of Windows code or the Web API contract is needed. Return a short conclusion, file and line references, and what remains unverified. Use the project definition `.codex/agents/code_explorer.toml`; if this Codex surface cannot select that role, explicitly set `model=gpt-5.6-luna` and `reasoning_effort=medium` when spawning and include its read-only instructions. Never silently substitute a model.
- **Quick Implementer — independent Orca Codex worker, GPT-5.6 Luna / High:** owns small, well-scoped implementation and fixes, related policy tests and the x64 Debug build. It is not a Codex subagent. Main owns Markdown edits. Launch it in the assigned checkout with `codex --model gpt-5.6-luna -c model_reasoning_effort=high`; confirm the active session settings before assigning work and never silently substitute a model.
- **Implementer — independent Orca Claude Code worker, Claude Opus 5.5 / Medium:** owns larger implementation and complex fixes, related policy tests and the x64 Debug build.
- **Code Reviewer — independent Orca Claude Code worker, Claude Opus 5.5 / Medium:** reviews the completed implementation independently and read-only, after the implementer stops. For uncommitted changes, review in the implementer's same worktree.
- At most one worker or Codex subagent may actively work alongside Main. Idle Orca terminals for Quick Implementer, Implementer and Code Reviewer may remain open for the owner to manage; opening them does not assign work. Main or the owner may assign a task directly to an idle independent-worker terminal; it then counts as the active worker. Workers and subagents never delegate or launch their own subagents. Start only the roles needed for a task; skip Code Explorer when the implementation scope is already clear. Give each assignment a concise purpose, scope, completion criteria and checks before it starts work. One writer at a time on a checkout; never revert or overwrite changes you did not make.
- Main confirms each independent worker's requested model and effort at launch. Orca's `worktree create --agent` has no per-call model/effort flags; use an explicit terminal command when those settings are needed (`codex --model gpt-5.6-luna -c model_reasoning_effort=high` or `claude --model claude-opus-5-5 --effort medium`). Check the active session's model before work; report an unavailable model instead of substituting one. Follow the version-matched Orca CLI guide before using Orca.

## Start of every session

1. `git status --short --untracked-files=all`, `git branch --show-current`, `git log -3 --oneline`, `Get-AppxPackage -Name FusionLedger.Windows` (the owner may have committed or installed since the last session).
2. Read only the `## Now` section of `.claude_stuff/WORK_PLAN.md` (state, next work, open checks). Read other sections or docs only when the task needs them.
3. Read the Web repository only when the web contract is needed (see "Other repository").

## Reading policy (keep context small)

- Search first (grep for the identifier, key or heading), then read only the hit and the lines around it. Never read `.claude_stuff/DESIGN.md`, `CHANGELOG.md`, `README.md`, `tests/.../Program.cs` or the `.resw` files in full; grep the heading or key.
- Open the matching `.claude/rules/*.md` files (table below) for the folders you change or review; do not open them all up front.

## Hard rules (every agent)

- **Git:** never run `git commit`, `git push` or any other history-changing command (amend, merge, rebase, reset, tag, stash, checkout of other branches) unless the owner explicitly asks for that action in chat. The owner normally commits and pushes. Leave changes uncommitted, list the changed files and propose a `vX.Y.Z, summary` commit message.
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

## What each kind of change needs

- **App change:** Main records the spec (larger work gets `.claude_stuff/specs/<name>.md`) and edits Markdown release/design records; the assigned implementer changes app code and policy tests, updates non-Markdown version fields and runs full verification ("Verify"). If Main edits docs after verification, the implementer reruns affected checks before ending work. Once the implementer stops, Code Reviewer reviews. Main integrates the reports, hands over the install command and "Authorized writes" list, and prepares a concise manual test checklist for the owner. After the owner installs, Main checks the installed package version and DLL hash read-only. Main records the owner's reported manual test results and keeps tests / build / device results separate in `WORK_PLAN.md`. Until the owner reports a result, device behavior is not performed.
- **Instruction-only change:** no app build; check for contradictions and stale names instead.

## Other repository (MolHub Web)

The Web repository (MolHub) owns `/api/v1`, `docs/WINDOWS_API.md`, `docs/openapi.yaml` and the bridge page; it is read-only from here unless the owner asks. Its location differs per checkout (Orca worktrees live under Orca's workspace folder, not next to this repo): take it from the request or confirm it (ask) before reading; never assume a sibling path. In the owner's main checkout it is `..\MolHub.Web` (`D:\.Developments\MolHub Client\MolHub.Web`).

## Where things are (read on demand)

| What | Where |
| --- | --- |
| Current state, next work, open checks | `.claude_stuff/WORK_PLAN.md` `## Now` (rest of that file: open checks, known issues, what exists) |
| Specs for larger work | `.claude_stuff/specs/` (for example `admin-v0.14.5.md`) |
| Design contract per page | `.claude_stuff/DESIGN.md` (grep the `## ` heading) |
| Session log, old routine, reference links | `.claude_stuff/reference/HISTORY.md` |
| User-facing docs / release notes | `README.md`, `CHANGELOG.md`, `VERSION.md` (grep) |
| Former device-check procedure | `git show 2614892:.claude/agents/device-verifier.md` |

- `.claude_stuff/` is not in Git. In the main checkout it is a real folder; in an Orca worktree it is a junction to the main checkout's copy. If it is missing, say so instead of guessing. Its Git line describes only the main checkout's `main`; always read your own Git state.
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

- Remove Orca worktrees only through Orca (`orca worktree rm` or the UI), never by deleting the folder yourself: its `.claude_stuff` is a junction to the main checkout, and a recursive delete could follow it. Orca 1.4.215's normal removal was checked on 2026-09-27 and left the main checkout's `.claude_stuff` intact; re-check after an Orca upgrade.

## Verify

```powershell
dotnet restore
dotnet run --project tests\FusionLedger.Windows.PolicyTests\FusionLedger.Windows.PolicyTests.csproj
dotnet build FusionLedger.Windows.csproj -p:Platform=x64 -p:Configuration=Debug
.\Install-Prototype.ps1 -WhatIf -PackageDirectory ".\AppPackages\FusionLedger.Windows_<version>_x64_Debug_Test"
git diff --check
```

The assigned implementer runs these commands from its checkout root, with the final x64 Debug build once. It also reports the DLL SHA-256 of the build (`bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\FusionLedger.Windows.dll`) and the private-use glyph and line-ending scans. Main reports tests, build and device behavior separately; a passing build is not proof of runtime behavior. CI (`.github/workflows/windows-build.yml`) repeats restore, policy tests and the x64 Debug build on push and pull request. No UI test project exists yet, so state which parts were automated.

## Device check

The owner performs manual tests of the installed app. Main supplies a short checklist based on the change and known open checks, confirms the installed package version and DLL hash read-only after the owner reports installation, and records the owner's pass / fail / not performed observations as owner-reported results. Main does not operate the app. There is no standing runtime-verification agent. If the owner explicitly requests an agent-operated device check, agree on its scope first; that agent reads the former procedure and its UI Automation pitfalls (`git show 2614892:.claude/agents/device-verifier.md`), follows the "Authorized writes" rule, never types the owner's password, and restores window size/theme/page.

## Handoff to Code Reviewer

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

The implementer stops before Code Reviewer starts. Uncommitted and untracked changes exist only in the author's checkout: review them in that same checkout, not in a new worktree. Committed work can be reviewed in any checkout at the given commits.

## Reviewing

1. Confirm the checkout: `git rev-parse --show-toplevel`, branch, HEAD, and for uncommitted work `git status --short --untracked-files=all` plus the fingerprint. On any mismatch with the handoff, stop and report.
2. Derive what the change must do from the owner's request, the hard rules above, the matching `.claude/rules/*.md`, `.claude_stuff/DESIGN.md` and (when a contract is involved) the Web repository's contract. Do not adopt the author's conclusions or test claims without checking them.
3. Read the diff in full; re-run the policy tests or read the assertions where you can.
4. Report findings as blocking / should-fix / nit with `file:line`, then what you did not verify. A reviewer does not edit files, the work plan or Git unless the request says so.
