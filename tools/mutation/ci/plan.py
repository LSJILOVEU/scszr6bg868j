#!/usr/bin/env python3
"""Plan the CI mutation shards for the lists a change adds, changes or renames.

plan.py --base REV [--head REV] [--repo DIR] [--github-output FILE]

Reads `git diff --name-status -M BASE HEAD` for files directly in tools/mutation/lists/ named *.txt (deleted files
, lists only renamed with identical content and other paths are ignored), reads each list as of HEAD, takes its entry ids in order and cuts every list into
ceil(n/10) shards whose sizes differ by at most one. Lists are never mixed in a shard.

Prints {"include": [{"list", "ids", "label"}, ...]} on stdout (a GitHub matrix); with --github-output also appends
`matrix=<that JSON>` and `count=<shards>` to FILE. Exit 0 on success (an empty table included), 2 on error with the
error id on the first line of stderr: bad-name, bad-id, empty-list, duplicate-id, too-many-shards, git, bad-args.
Standard library only; the full list format is checked later by mutate.py.
"""
import argparse
import json
import re
import subprocess
import sys

LISTS_DIR = "tools/mutation/lists/"
SHARD_SIZE = 10
MAX_SHARDS = 24
NAME_RE = re.compile(r"^[A-Za-z0-9._-]+$")
ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")  # same as tools/mutation/mutate.py
ENTRY_RE = re.compile(r"^\[(.+)\]$")


class Fatal(Exception):
    def __init__(self, code, msg):
        super().__init__(msg)
        self.code = code


def git(repo, *args):
    r = subprocess.run(["git", "-C", repo, *args], capture_output=True)
    if r.returncode != 0:
        raise Fatal("git", "git %s failed: %s" % (" ".join(args), r.stderr.decode("utf-8", "replace").strip()))
    return r.stdout


def changed_lists(repo, base, head):
    """Paths of lists added, modified or renamed/copied (new path) between base and head, in diff order."""
    out = git(repo, "diff", "--name-status", "-z", "-M", "--no-ext-diff", base, head, "--").decode("utf-8")
    fields = out.split("\0")
    if fields and fields[-1] == "":
        fields.pop()
    paths = []
    i = 0
    while i < len(fields):
        status = fields[i]
        if status[:1] in ("R", "C"):
            path = fields[i + 2]
            i += 3
        else:
            path = fields[i + 1]
            i += 2
        if status[:1] == "D" or status == "R100":
            continue
        if not path.startswith(LISTS_DIR) or not path.endswith(".txt"):
            continue
        name = path[len(LISTS_DIR):]
        if "/" in name:
            continue
        if not NAME_RE.match(name):
            raise Fatal("bad-name", "list file name has characters outside [A-Za-z0-9._-]: %r" % name)
        paths.append(path)
    return paths


def entry_ids(path, text):
    ids = []
    for n, raw in enumerate(text.split("\n"), 1):
        m = ENTRY_RE.match(raw.rstrip("\r"))
        if not m:
            continue
        mid = m.group(1)
        if not ID_RE.match(mid):
            raise Fatal("bad-id", "%s:%d: id has characters outside [A-Za-z0-9._-] or a bad first character" % (path, n))
        if mid in ids:
            raise Fatal("duplicate-id", "%s:%d: duplicate id %s" % (path, n, mid))
        ids.append(mid)
    if not ids:
        raise Fatal("empty-list", "%s: no entries" % path)
    return ids


def split(items):
    """ceil(n/SHARD_SIZE) consecutive chunks, sizes differing by at most one, larger chunks first."""
    k = -(-len(items) // SHARD_SIZE)
    q, r = divmod(len(items), k)
    chunks, start = [], 0
    for i in range(k):
        size = q + 1 if i < r else q
        chunks.append(items[start:start + size])
        start += size
    return chunks


def make_plan(repo, base, head):
    shards = []
    for path in changed_lists(repo, base, head):
        text = git(repo, "show", "%s:%s" % (head, path)).decode("utf-8", "replace")
        chunks = split(entry_ids(path, text))
        stem = path[len(LISTS_DIR):-len(".txt")]
        for i, chunk in enumerate(chunks, 1):
            shards.append({"list": path, "ids": ",".join(chunk), "label": "%s %d/%d" % (stem, i, len(chunks))})
    if len(shards) > MAX_SHARDS:
        raise Fatal("too-many-shards", "%d shards, at most %d: too many list changes in one pull request; "
                    "split it or ask the coordinator" % (len(shards), MAX_SHARDS))
    return {"include": shards}


class Parser(argparse.ArgumentParser):
    def error(self, message):
        raise Fatal("bad-args", message)


def main(argv=None):
    ap = Parser(description=__doc__.split("\n")[0])
    ap.add_argument("--base", required=True)
    ap.add_argument("--head", default="HEAD")
    ap.add_argument("--repo", default=".")
    ap.add_argument("--github-output")
    try:
        a = ap.parse_args(argv)
        result = make_plan(a.repo, a.base, a.head)
    except Fatal as ex:
        print(ex.code, file=sys.stderr)
        print(str(ex), file=sys.stderr)
        return 2
    text = json.dumps(result, separators=(",", ":"))
    if a.github_output:
        with open(a.github_output, "a", encoding="utf-8") as fh:
            fh.write("matrix=%s\ncount=%d\n" % (text, len(result["include"])))
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
