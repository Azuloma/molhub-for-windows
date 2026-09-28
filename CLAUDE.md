@AGENTS.md

# MolHub for Windows — Claude Code notes

`AGENTS.md` (imported above) holds the shared roles, start-of-session routine, hard rules, Web-repository boundary, verification, device check, handoff and review. This file adds only what is specific to Claude Code.

- You are an independent Orca worker when Main or the owner assigns you a task. The assignment names your role: Implementer or Code Reviewer. Confirm the session uses `claude-opus-5-5` with Medium effort before starting; report a mismatch without silently changing models.
- As Implementer, edit only the assigned app/test/configuration scope, run the related policy tests and x64 Debug build, and report results and unverified behavior. Main owns Markdown edits. Stop work before Code Reviewer starts.
- As Code Reviewer, use `AGENTS.md` "Reviewing", inspect the implementer's same checkout for uncommitted work, and stay read-only. Report findings with file and line references and state what you did not verify.
- Do not launch Claude subagents (including built-in Explore and Plan) or other Orca workers. Main handles delegated work and progress tracking; the owner may also manage idle terminals directly.
- Folder rules in `.claude/rules/` load automatically when you work on matching files. Former `.claude/agents/` definitions are history only (commit `2614892`).
