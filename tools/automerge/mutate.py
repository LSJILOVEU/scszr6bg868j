#!/usr/bin/env python3
"""Flip each decision in decide.py once and check that test_decide.py catches it.

Run: python3 -I tools/automerge/mutate.py   (prints a Markdown table; exit 1 if any mutant survives)
"""
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))

MUTANTS = [
    ("all met -> no merge", 'return MERGE, f"#{n}: all checks', 'return WAIT, f"#{n}: all checks'),
    ("reviewed not required", 'if "reviewed" not in labels:', 'if False:'),
    ("hold ignored", 'if "hold" in labels:', 'if False:'),
    ("required check failure accepted", 'if c != "success":', 'if c == "missing":'),
    ("extra check failure accepted", 'if c not in OTHER_OK:', 'if False:'),
    ("extra check skipped rejected", 'OTHER_OK = ("success", "skipped")', 'OTHER_OK = ("success",)'),
    ("head moved ignored", 'if pr.get("headRefOid") != sha:', 'if False:'),
    ("protected paths ignored", 'if path and is_protected(path):', 'if False:'),
    ("old path of rename ignored", 'for path in (f.get("filename"), f.get("previous_filename")):',
     'for path in (f.get("filename"),):'),
    ("draft accepted", 'if pr.get("isDraft"):', 'if False:'),
    ("any base accepted", 'if pr.get("baseRefName") != BASE_BRANCH:', 'if False:'),
    ("tools/automerge/ unprotected", ', "tools/automerge/", ', ', '),
    ("tools/open-pr/ unprotected", ', "tools/open-pr/")', ')'),
    ("CLAUDE.md only at root", 'if parts[-1].lower() in PROTECTED_NAMES:', 'if path in ("CLAUDE.md", "CLAUDE.local.md"):'),
    (".claude/ only at root", 'for seg in parts[:-1]):', 'for seg in parts[:1]):'),
    ("truncated file list ignored", 'if pr.get("changedFiles") != len(files):', 'if False:'),
]


def main():
    src = open(os.path.join(HERE, "decide.py"), encoding="utf-8").read()
    survived = 0
    print("| # | mutant | result |")
    print("|---|---|---|")
    with tempfile.TemporaryDirectory() as d:
        for i, (name, old, new) in enumerate(MUTANTS, 1):
            if src.count(old) != 1:
                print(f"| {i} | {name} | ERROR: pattern found {src.count(old)} times |")
                survived += 1
                continue
            path = os.path.join(d, f"decide_m{i}.py")
            with open(path, "w", encoding="utf-8") as fh:
                fh.write(src.replace(old, new))
            env = dict(os.environ, DECIDE_PATH=path)
            r = subprocess.run([sys.executable, "-I", os.path.join(HERE, "test_decide.py")],
                               env=env, capture_output=True, text=True)
            killed = r.returncode != 0
            failed = sorted({ln.split()[1] for ln in r.stderr.splitlines() if ln.startswith(("FAIL: ", "ERROR: "))})
            print(f"| {i} | {name} | {'killed by ' + ', '.join(failed) if killed else 'SURVIVED'} |")
            survived += 0 if killed else 1
    print(f"\n{len(MUTANTS) - survived}/{len(MUTANTS)} killed")
    return 1 if survived else 0


if __name__ == "__main__":
    sys.exit(main())
