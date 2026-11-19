# Open a pull request from workflow_dispatch

`.github/workflows/open-pr.yml` opens a pull request into `main`, or edits the title and description of an open one,
without a local `gh`. Only accounts with write access can dispatch it, and it runs only when dispatched on `main`.

Steps (script: `open_pr.py`). Inputs are read from the event file (`GITHUB_EVENT_PATH`), never put in step `env:`
(GitHub prints step env values in the public log), `run:` or `run-name`; values go to git and gh as separate arguments:

1. `check` (no token): validates inputs, runs `pii_guard.py --text` on title + body with the private term list
   (`--require-terms`), and in open mode fetches the branch and runs `pii_guard.py --range origin/main..origin/<branch>`.
   Any finding fails the run; nothing is rewritten.
2. `apply` (token `OPEN_PR_TOKEN`): open mode refuses when an open pull request already exists for the branch; edit
   mode refuses unless the pull request is open, into `main`, from a `claude/` or `wip/` branch of this repository.
   Then `gh pr create --body-file` or `gh pr edit --body-file`, unless `dry_run` is true. The run summary shows the
   pull request number and link.

A personal fine-grained token is needed because pull requests opened with `GITHUB_TOKEN` do not trigger other
workflows (headless-tests, pii-guard, privacy-gate).

## Inputs

| input | open | edit | rule |
|---|---|---|---|
| `mode` | `open` | `edit` | |
| `branch` | required | empty | `^(claude\|wip)/[A-Za-z0-9._/-]+$`, valid ref name, at most 200 characters, ahead of `main` |
| `pr` | empty | required | positive number |
| `title` | required | required | one line, 1 to 72 characters |
| `body` | required | required | 1 to 60000 characters, used verbatim (newlines allowed through the API) |
| `dry_run` | | | boolean, default `true`: checks only, no create or edit |

GitHub limits the whole `inputs` object of one dispatch to 65,535 characters.

## Dispatching from a GitHub API client

Run the guard locally on the text first (`python3 tools/pii-guard/pii_guard.py --text body.txt`): a rejected input
is not printed in the log, but it stays in the run's event data.

REST: `POST /repos/{owner}/{repo}/actions/workflows/open-pr.yml/dispatches` with

```json
{"ref": "main",
 "inputs": {"mode": "open", "branch": "claude/example", "title": "Add example",
            "body": "Summary line.\n\n- detail\n", "dry_run": "true"}}
```

With a GitHub tool that wraps it (`actions_run_trigger`, method `run_workflow`): `owner`, `repo`,
`workflow_id: "open-pr.yml"`, `ref: "main"`, and the same `inputs` object. Boolean inputs may be sent as the strings
`"true"` / `"false"`. Edit: `{"mode": "edit", "pr": "123", "title": "...", "body": "...", "dry_run": "false"}`.

Find the result with `actions_list` (`list_workflow_runs`, resource `open-pr.yml`, event `workflow_dispatch`); the
run summary has the pull request number. A dispatch returns no run id, so pick the newest run.
A dry run also makes read-only `gh` calls, so it needs `OPEN_PR_TOKEN` too.

## One-time setup (repository owner)

1. Create a fine-grained personal access token limited to this repository: Pull requests read and write, Contents
   read, Metadata read. Pick an expiry and renew before it.
2. Save it as the repository secret `OPEN_PR_TOKEN` (Settings, Secrets and variables, Actions).
3. `PII_TERMS` must already exist (pii-guard uses it); without it the check step fails.

## Tests

```sh
python3 -I tools/open-pr/test_open_pr.py
python3 -I tools/open-pr/mutate.py      # each mutant of open_pr.py must fail the tests
```

Offline: a throwaway git remote, the real `pii_guard.py`, and a fake `gh` that records calls.
