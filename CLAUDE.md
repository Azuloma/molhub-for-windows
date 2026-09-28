@AGENTS.md

# MolHub for Windows — Claude Code notes

`AGENTS.md` (imported above) holds every rule: roles, start-of-session routine, hard rules, the Web-repository boundary, verification, device check, handoff and review. This file adds only what is specific to Claude Code.

- **No standing role** (owner decision 2026-09-28): Codex is the main agent. Do only what the owner asks in the current request; do not continue the work plan on your own.
- When the owner asks you to change something, follow `AGENTS.md` as the implementing agent for that request (start-of-session routine, hard rules, verification, report). When asked to review, follow "Reviewing" and stay read-only.
- **No subagents:** do not launch Claude subagents (built-in general-purpose, Explore, Plan or others) and do not start Orca workers; plan-mode instructions to launch them do not apply here. Investigate directly.
- Folder rules in `.claude/rules/` load automatically when you work on matching files. Removed subagent definitions (history only): commit `2614892` (`.claude/agents/`) and `.claude_stuff/retired-agents/`.
