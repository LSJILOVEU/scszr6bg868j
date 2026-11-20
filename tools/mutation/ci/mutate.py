#!/usr/bin/env python3
"""Flip each decision in plan.py once and check that test_plan.py catches it.

Run: python3 -I tools/mutation/ci/mutate.py   (prints a Markdown table; exit 1 if any mutant survives)
"""
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))

MUTANTS = [
    ("shard size 11", "SHARD_SIZE = 10", "SHARD_SIZE = 11"),
    ("shard size 9", "SHARD_SIZE = 10", "SHARD_SIZE = 9"),
    ("remainder in last shard", "size = q + 1 if i < r else q", "size = q if i < k - 1 else len(items) - start"),
    ("order not kept", "chunks = split(entry_ids(path, text))", "chunks = split(sorted(entry_ids(path, text), reverse=True))"),
    ("lists mixed", 'shards.append({"list": path,', 'shards.append({"list": shards[0]["list"] if shards else path,'),
    ("any .txt accepted", 'if not path.startswith(LISTS_DIR) or not path.endswith(".txt"):', 'if not path.endswith(".txt"):'),
    ("any file in lists/ accepted", 'if not path.startswith(LISTS_DIR) or not path.endswith(".txt"):', "if not path.startswith(LISTS_DIR):"),
    ("subdirectories accepted", 'if "/" in name:', "if False:"),
    ("deleted list planned", 'if status[:1] == "D":', "if False:"),
    ("rename planned at old path", "path = fields[i + 2]", "path = fields[i + 1]"),
    ("empty list accepted", "if not ids:", "if False:"),
    ("duplicate id accepted", "if mid in ids:", "if False:"),
    ("any id accepted", "if not ID_RE.match(mid):", "if False:"),
    ("any list name accepted", "if not NAME_RE.match(name):", "if False:"),
    ("shard limit 25", "MAX_SHARDS = 24", "MAX_SHARDS = 25"),
    ("no shard limit", "if len(shards) > MAX_SHARDS:", "if False:"),
    ("empty table exit 1", "    print(text)\n    return 0", "    print(text)\n    return 1 if not result['include'] else 0"),
    ("count output wrong", '"matrix=%s\\ncount=%d\\n" % (text, len(result["include"]))', '"matrix=%s\\ncount=%d\\n" % (text, 1)'),
    ("git error ignored", "    if r.returncode != 0:\n        raise Fatal(\"git\"", "    if False:\n        raise Fatal(\"git\""),
]


def main():
    src = open(os.path.join(HERE, "plan.py"), encoding="utf-8").read()
    survived = 0
    print("| # | mutant | result |")
    print("|---|---|---|")
    with tempfile.TemporaryDirectory() as d:
        for i, (name, old, new) in enumerate(MUTANTS, 1):
            if src.count(old) != 1:
                print(f"| {i} | {name} | ERROR: pattern found {src.count(old)} times |")
                survived += 1
                continue
            path = os.path.join(d, f"plan_m{i}.py")
            with open(path, "w", encoding="utf-8") as fh:
                fh.write(src.replace(old, new))
            env = dict(os.environ, PLAN_PATH=path)
            r = subprocess.run([sys.executable, "-I", os.path.join(HERE, "test_plan.py")],
                               env=env, capture_output=True, text=True)
            killed = r.returncode != 0
            failed = sorted({ln.split()[1] for ln in r.stderr.splitlines() if ln.startswith(("FAIL: ", "ERROR: "))})
            print(f"| {i} | {name} | {'killed by ' + ', '.join(failed[:3]) if killed else 'SURVIVED'} |")
            survived += 0 if killed else 1
    print(f"\n{len(MUTANTS) - survived}/{len(MUTANTS)} killed")
    return 1 if survived else 0


if __name__ == "__main__":
    sys.exit(main())
