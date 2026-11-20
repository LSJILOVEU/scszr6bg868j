#!/usr/bin/env python3
"""Decide whether a pull request may be squash-merged by the automerge workflow.

Inputs are JSON files fetched by run.sh:
  --pr      output of `gh pr view --json number,title,state,isDraft,baseRefName,headRefOid,labels,changedFiles`
  --files   list of {"filename", "previous_filename"} from the pull request files API
  --checks  list of {"id", "name", "status", "conclusion"} check runs on the head commit
  --sha     the head commit the triggering event was about

Exit codes: 0 merge, 10 not now (waiting or not eligible), 11 needs a human merge.
One line explaining the decision is printed to stdout.
"""
import argparse
import json
import sys

MERGE, WAIT, MANUAL = 0, 10, 11

BASE_BRANCH = "main"
REQUIRED_CHECKS = ("test (ubuntu-latest)", "test (windows-latest)", "guard", "gate")
# Check runs of this workflow itself; they are running while it decides.
SELF_CHECKS = ("automerge",)
OTHER_OK = ("success", "skipped")
# Rule files, the merge logic itself and code that runs with secrets stay a human decision.
# Agent instruction files count at any depth.
# The mutation tools that judge a pull request in CI (mutation lists in tools/mutation/lists/ stay automatic).
MUTATION_TOOLS = ("tools/mutation/mutate.py", "tools/mutation/hygiene.py", "tools/mutation/ci/")
PROTECTED_PREFIXES = (".github/", "tools/pii-guard/", "tools/automerge/", "tools/open-pr/") + MUTATION_TOOLS
PROTECTED_DIRS = (".claude",)  # any path segment, case-insensitive
PROTECTED_NAMES = ("claude.md", "claude.local.md")  # file name at any depth, case-insensitive


def is_protected(path):
    parts = path.split("/")
    if parts[-1].lower() in PROTECTED_NAMES:
        return True
    if any(seg.lower() in PROTECTED_DIRS for seg in parts[:-1]):
        return True
    return any(path.startswith(p) for p in PROTECTED_PREFIXES)


def latest_by_name(checks):
    latest = {}
    for c in checks:
        name = c.get("name")
        if name not in latest or c.get("id", 0) > latest[name].get("id", 0):
            latest[name] = c
    return latest


def conclusion(check):
    if check is None:
        return "missing"
    if check.get("status") != "completed":
        return check.get("status") or "pending"
    return check.get("conclusion") or "none"


def decide(pr, files, checks, sha):
    n = pr.get("number")
    if pr.get("state") != "OPEN":
        return WAIT, f"#{n}: {pr.get('state')}, skip"
    if pr.get("isDraft"):
        return WAIT, f"#{n}: draft, skip"
    if pr.get("baseRefName") != BASE_BRANCH:
        return WAIT, f"#{n}: base is {pr.get('baseRefName')}, not {BASE_BRANCH}, skip"
    if pr.get("headRefOid") != sha:
        return WAIT, f"#{n}: head moved past {sha[:7]}, skip"
    labels = {lab.get("name") for lab in pr.get("labels", [])}
    if "hold" in labels:
        return WAIT, f"#{n}: labelled hold, skip"
    if "reviewed" not in labels:
        return WAIT, f"#{n}: no reviewed label, skip"
    # The files API stops at 3000 entries; a short list could hide a rule file.
    if pr.get("changedFiles") != len(files):
        return MANUAL, f"#{n}: file list has {len(files)} of {pr.get('changedFiles')} files: needs a human merge"
    for f in files:
        for path in (f.get("filename"), f.get("previous_filename")):
            if path and is_protected(path):
                return MANUAL, f"#{n}: touches rule files ({path}): needs a human merge"
    latest = latest_by_name(checks)
    for need in REQUIRED_CHECKS:
        c = conclusion(latest.get(need))
        if c != "success":
            return WAIT, f"#{n}: required check '{need}' is '{c}', waiting"
    for name in sorted(latest):
        if name in REQUIRED_CHECKS or name in SELF_CHECKS:
            continue
        c = conclusion(latest[name])
        if c not in OTHER_OK:
            return WAIT, f"#{n}: check '{name}' is '{c}', waiting"
    return MERGE, f"#{n}: all checks green on {sha[:7]}, squash-merging"


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--pr", required=True)
    ap.add_argument("--files", required=True)
    ap.add_argument("--checks", required=True)
    ap.add_argument("--sha", required=True)
    a = ap.parse_args(argv)
    code, why = decide(load(a.pr), load(a.files), load(a.checks), a.sha)
    print(why)
    return code


if __name__ == "__main__":
    sys.exit(main())
