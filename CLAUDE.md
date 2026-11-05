# Rules for this public repository

Everything here is public. Write technical content only, as little as possible.

## Never write
Names, nicknames, account names, email addresses, schedules, time zones, places, machine or hardware details, local paths, billing or account details, links to chat or agent sessions, session ids. This applies to commit messages, pull request titles and descriptions, comments, code comments, documents and file names. Do not add signature, co-author, session or attribution lines. Do not mention who asked for a change or why in personal terms.

## Commits
- One short line, imperative, at most 72 characters. A body only if needed, at most 8 lines.
- Identity must be `warcore <noreply@users.noreply.github.com>`; dates in UTC. Commit with `TZ=UTC git commit`.
- Set up once after cloning: `sh tools/pii-guard/install.sh` (installs pre-commit, commit-msg and pre-push hooks).
- Before every commit and push, run `python3 tools/pii-guard/pii_guard.py --staged` (the hook does this too). Fix findings; never bypass with `--no-verify` and never edit the guard to make a finding pass.
- Do not open pull requests, issues or comments on this repository unless the owner asks. Changes arrive as snapshots from the private source repository.

## The guard
- `tools/pii-guard/pii_guard.py` checks staged changes, commit messages, author and committer identity and dates, every commit in a push or pull request, pull request text, and the whole tree. CI runs it (`.github/workflows/pii-guard.yml`) and a failure blocks merging.
- Generic rules are in the script (email addresses, home paths, session links, non-UTC offsets, key formats, image/video files).
- The private term list is never stored in the repository. CI reads it from the repository secret `PII_TERMS`; locally put it in `~/.warcore-pii-terms`. Do not paste terms into files, commits, issues, or chat logs that end up here.
- Findings print rule ids and locations only. Do not copy matched text into messages.

## Privacy review before merge
The script cannot see meaning, so a Claude review must do it. Every pull request needs this review before it is merged:
- Scope: the diff (including deleted lines, because history is public), every commit message, author and committer identity, the PR title and description, and all review and issue comments on it.
- Look for leaks the script misses: names or nicknames in any form (translations, initials, transliterations), places, institutions, schedules or habits ("late at night", "after work"), devices, hardware, operating-system versions, billing or account trouble, who asked for or approved the change, hints about other repositories or projects, anything that narrows down who the author is.
- Run `python3 tools/pii-guard/pii_guard.py --range <base>..<head>` as part of the review.
- The review must contain one line: `Privacy: PASS <first 7 characters of the head commit SHA>`, or `Privacy: FAIL <sha7> <rule and location only>`. Never copy the leaked text into the review.
- No PASS line for the current head commit means no `reviewed` label and no merge. The `privacy-gate` check enforces this: it needs the `reviewed` label and a PASS line naming the current head commit, and a new push removes the label.
