# WarCore

A reskinnable top-down shooter/melee game foundation. Unity 6000.6, C#.

- Deterministic simulation core: `Assets/WarCore/Sim`
- Unity-free tests: `tools/headless` (.NET 8, NUnit). Run `cd tools/headless && dotnet test -c Release`.
- Test sources: `Assets/WarCore/Tests`

## Rules for this public repository

Commit messages, pull request descriptions, comments, code comments and documents must contain technical content only. Never include personal information: names, nicknames, account names, email addresses, schedules or time zones, machine or hardware details, local paths, billing or account details, or links to chat or agent sessions.

All rights reserved. No license is granted.

## Merging

Pull requests into `main` are squash-merged by `.github/workflows/automerge.yml` once they carry the `reviewed` label, have no `hold` label, and every check on the head commit is green. Pull requests that touch rule files (`.github/`, `CLAUDE.md`, `.claude/`, `tools/pii-guard/`, `tools/automerge/`, `tools/open-pr/`) are left for a manual merge. Decision logic: `tools/automerge/decide.py`.
