#!/usr/bin/env python3
"""Test hygiene check: fails when lines added between two commits contain a weak assertion form.

Only added ('+') lines of Assets/WarCore/Tests/** and tools/headless/*.cs count; deleted lines never offset them.
Exit 0 = clean, 1 = findings (file and line in the new file), 2 = git error.
"""
import argparse
import re
import subprocess
import sys

PATHSPECS = [":(glob)Assets/WarCore/Tests/**", ":(glob)tools/headless/*.cs"]
RULES = [
    ("or-not-equal", re.compile(r"\.Or\.Not\.EqualTo")),
    ("count-at-least-zero", re.compile(r"GreaterThanOrEqualTo\(\s*0[lLuU]*\s*\)")),
    ("assert-pass", re.compile(r"Assert\.Pass\s*\(")),
]
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")


def added_lines(repo, base, head):
    cmd = ["git", "-C", repo, "diff", "--no-color", "--no-ext-diff", "--no-renames", "-U0",
           "%s..%s" % (base, head), "--"] + PATHSPECS
    r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if r.returncode != 0:
        raise RuntimeError(r.stderr.decode("utf-8", "replace").strip())
    path = None
    line = 0
    in_header = False
    for raw in r.stdout.decode("utf-8", "replace").split("\n"):
        if raw.startswith("diff --git "):
            in_header = True
            path = None
            continue
        if in_header:
            if raw.startswith("+++ "):
                target = raw[4:]
                path = None if target == "/dev/null" else target[2:] if target.startswith("b/") else target
                continue
            elif HUNK.match(raw):
                in_header = False
            else:
                continue
        m = HUNK.match(raw)
        if m:
            line = int(m.group(1))
            continue
        if raw.startswith("+") and path is not None:
            yield path, line, raw[1:]
            line += 1


def main(argv=None):
    ap = argparse.ArgumentParser(description="Check added test lines for weak assertion forms.")
    ap.add_argument("base")
    ap.add_argument("head")
    ap.add_argument("--repo", default=".")
    a = ap.parse_args(argv)
    try:
        findings = [(p, n, rule) for p, n, text in added_lines(a.repo, a.base, a.head)
                    for rule, rx in RULES if rx.search(text)]
    except RuntimeError as ex:
        sys.stderr.write("git\n%s\n" % ex)
        return 2
    for p, n, rule in findings:
        sys.stdout.write("%s:%d: %s\n" % (p, n, rule))
    if findings:
        sys.stdout.write("hygiene: FAIL (%d)\n" % len(findings))
        return 1
    sys.stdout.write("hygiene: PASS\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
