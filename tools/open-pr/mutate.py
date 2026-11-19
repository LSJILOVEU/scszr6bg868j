#!/usr/bin/env python3
"""Mutation check for open_pr.py: each mutant must make test_open_pr.py fail. Restores the file afterwards.

Run from the repository root: python3 -I tools/open-pr/mutate.py
"""
import subprocess, sys
p = "tools/open-pr/open_pr.py"
with open(p) as f:
    orig = f.read()
muts = [
 ('BRANCH_RE = re.compile(r"^(claude|wip)/[A-Za-z0-9._/-]+$")', 'BRANCH_RE = re.compile(r"^(claude|wip)/[A-Za-z0-9._/-]+")'),
 ('BRANCH_RE = re.compile(r"^(claude|wip)/[A-Za-z0-9._/-]+$")', 'BRANCH_RE = re.compile(r"^[A-Za-z0-9._/-]+$")'),
 ('MAX_TITLE = 72', 'MAX_TITLE = 73'),
 ('    if inp["dry"]:', '    if False:'),
 ('        guard("--text", path, "--label", "pull-request")', '        pass'),
 ('        guard("--range", rng)', '        pass'),
 ('parts[0] != "OPEN" or ', ''),
 ('or not branch_ok(parts[2])', ''),
 ('"--head=" + inp["branch"],\n                     "--title=" + inp["title"]', '"--head=" + inp["branch"],\n                     "--title", inp["title"]'),
 ('"--title=" + inp["title"], "--body-file=" + path)\n            url =', '"--title", inp["title"], "--body-file=" + path)\n            url ='),
 ('        if pr:\n            raise Reject("open mode takes branch, not pr")\n', ''),
 ('        if n != "0":', '        if False:'),
 ('if ".." in b or', 'if False or'),
 ('    if not env("GH_TOKEN"):', '    if False:'),
 ('    if len(title) > MAX_TITLE:', '    if False:'),
 ('        if not PR_RE.match(pr):', '        if False:'),
]
killed = 0
for a, _ in muts:
    assert orig.count(a) == 1, a
try:
    for i, (a, b) in enumerate(muts, 1):
        with open(p, "w") as f:
            f.write(orig.replace(a, b))
        r = subprocess.run([sys.executable, "-I", "tools/open-pr/test_open_pr.py"], capture_output=True, text=True)
        k = r.returncode != 0
        killed += k
        print("M%02d %s" % (i, "killed" if k else "SURVIVED"), a.strip()[:60].replace("\n", " "))
finally:
    with open(p, "w") as f:
        f.write(orig)
print("%d/%d killed" % (killed, len(muts)))
sys.exit(0 if killed == len(muts) else 1)
