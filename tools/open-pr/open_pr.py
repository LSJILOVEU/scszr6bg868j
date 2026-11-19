#!/usr/bin/env python3
"""Open or edit a pull request into main from workflow inputs (.github/workflows/open-pr.yml).

Every input arrives as an environment variable and is passed to git and gh as a separate argument; nothing is
ever run through a shell. Exit 0 ok, 1 rejected or failed.

  open_pr.py check   validate inputs, run pii-guard on title + body, and (open) on origin/main..origin/<branch>
  open_pr.py apply   validate again, read-only checks with gh, then gh pr create / gh pr edit unless DRY_RUN=true

Environment:
  MODE      open | edit
  BRANCH    head branch (open only), ^(claude|wip)/[A-Za-z0-9._/-]+$
  PR        pull request number (edit only)
  TITLE     one line, 1..72 characters
  BODY      1..60000 characters, used verbatim
  DRY_RUN   true | false
  GH_TOKEN  token for gh (apply only); GH_REPO owner/name (apply only)
  GUARD     path to pii_guard.py (default tools/pii-guard/pii_guard.py)
  GUARD_REQUIRE_TERMS  1 (default) fails when no private term list is available
  BASE, REMOTE         default main, origin
"""
import os, re, subprocess, sys, tempfile

BRANCH_RE = re.compile(r"^(claude|wip)/[A-Za-z0-9._/-]+$")
PR_RE = re.compile(r"^[1-9][0-9]{0,8}$")
MAX_TITLE = 72
MAX_BODY = 60000
MAX_BRANCH = 200


class Reject(Exception):
    pass


def env(name, default=""):
    return os.environ.get(name, default)


def branch_ok(b):
    if not BRANCH_RE.match(b) or len(b) > MAX_BRANCH:
        return False
    # git ref rules the pattern does not already exclude
    if ".." in b or "//" in b or b.endswith("/") or b.endswith(".") or "@{" in b:
        return False
    return not any(p.startswith(".") or p.endswith(".lock") for p in b.split("/"))


def validate():
    """Return the cleaned inputs as a dict, or raise Reject with a message that never echoes input text."""
    mode, branch, pr = env("MODE"), env("BRANCH"), env("PR")
    title, body, dry = env("TITLE"), env("BODY"), env("DRY_RUN")
    if mode not in ("open", "edit"):
        raise Reject("mode must be open or edit")
    if dry not in ("true", "false"):
        raise Reject("dry_run must be true or false")
    if mode == "open":
        if pr:
            raise Reject("open mode takes branch, not pr")
        if not branch_ok(branch):
            raise Reject("branch must match ^(claude|wip)/[A-Za-z0-9._/-]+$ and be a valid ref name")
    else:
        if branch:
            raise Reject("edit mode takes pr, not branch")
        if not PR_RE.match(pr):
            raise Reject("edit mode needs pr as a positive number")
    if not title.strip():
        raise Reject("title is empty")
    if len(title) > MAX_TITLE:
        raise Reject("title has %d characters, limit %d" % (len(title), MAX_TITLE))
    if any(ord(c) < 32 or ord(c) == 127 for c in title):
        raise Reject("title must be one line without control characters")
    if not body.strip():
        raise Reject("body is empty")
    if len(body) > MAX_BODY:
        raise Reject("body has %d characters, limit %d" % (len(body), MAX_BODY))
    if "\0" in body:
        raise Reject("body contains a NUL character")
    return {"mode": mode, "branch": branch, "pr": pr, "title": title, "body": body, "dry": dry == "true"}


def run(argv, **kw):
    return subprocess.run(argv, capture_output=True, text=True, **kw)


def guard_argv(*args):
    argv = [sys.executable, "-I", env("GUARD", "tools/pii-guard/pii_guard.py")] + list(args)
    if env("GUARD_REQUIRE_TERMS", "1") == "1":
        argv.append("--require-terms")
    return argv


def guard(*args):
    p = run(guard_argv(*args))
    # pii_guard prints rule ids and locations only, never matched text
    sys.stdout.write(p.stdout)
    if p.returncode != 0:
        raise Reject("pii-guard rejected %s (exit %d); fix the text or commits, nothing was changed"
                     % (args[0], p.returncode))


def check(inp):
    with tempfile.TemporaryDirectory(dir=env("RUNNER_TEMP") or None) as d:
        path = os.path.join(d, "pr.txt")
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(inp["title"] + "\n" + inp["body"] + "\n")
        guard("--text", path, "--label", "pull-request")
    if inp["mode"] == "open":
        remote, base, b = env("REMOTE", "origin"), env("BASE", "main"), inp["branch"]
        p = run(["git", "fetch", "--no-tags", "--quiet", remote,
                 "+refs/heads/%s:refs/remotes/%s/%s" % (base, remote, base),
                 "+refs/heads/%s:refs/remotes/%s/%s" % (b, remote, b)])
        if p.returncode != 0:
            raise Reject("could not fetch %s and the branch from %s" % (base, remote))
        rng = "%s/%s..%s/%s" % (remote, base, remote, b)
        p = run(["git", "rev-list", "--count", rng])
        if p.returncode != 0 or int(p.stdout.strip() or "0") == 0:
            raise Reject("branch has no commits ahead of %s" % base)
        guard("--range", rng)
    print("check passed")


def gh(*args):
    p = run(["gh"] + list(args))
    if p.returncode != 0:
        sys.stdout.write(p.stderr)
        raise Reject("gh %s %s failed (exit %d)" % (args[0], args[1], p.returncode))
    return p.stdout.strip()


def summary(line):
    print(line)
    path = env("GITHUB_STEP_SUMMARY")
    if path:
        with open(path, "a", encoding="utf-8") as f:
            f.write(line + "\n")


def apply(inp):
    if not env("GH_TOKEN"):
        raise Reject("GH_TOKEN is empty: the OPEN_PR_TOKEN secret is missing")
    repo, base = env("GH_REPO"), env("BASE", "main")
    if not re.match(r"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", repo):
        raise Reject("GH_REPO must be owner/name")
    if inp["mode"] == "open":
        n = gh("pr", "list", "--repo", repo, "--head", inp["branch"], "--state", "open",
               "--json", "number", "--jq", "length")
        if n != "0":
            raise Reject("an open pull request already exists for this branch; use mode edit")
    else:
        info = gh("pr", "view", inp["pr"], "--repo", repo, "--json",
                  "state,baseRefName,headRefName,isCrossRepository",
                  "--jq", '[.state, .baseRefName, .headRefName, (.isCrossRepository|tostring)] | join("\\t")')
        parts = info.split("\t")
        if len(parts) != 4 or parts[0] != "OPEN" or parts[1] != base or parts[3] != "false" or not branch_ok(parts[2]):
            raise Reject("pull request must be open, into %s, from a claude/ or wip/ branch of this repository" % base)
    if inp["dry"]:
        summary("dry run: checks passed, would %s; nothing was changed" %
                ("open a pull request" if inp["mode"] == "open" else "edit pull request #" + inp["pr"]))
        return
    with tempfile.TemporaryDirectory(dir=env("RUNNER_TEMP") or None) as d:
        path = os.path.join(d, "body.md")
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(inp["body"])
        if inp["mode"] == "open":
            url = gh("pr", "create", "--repo", repo, "--base=" + base, "--head=" + inp["branch"],
                     "--title=" + inp["title"], "--body-file=" + path)
            verb = "opened"
        else:
            gh("pr", "edit", inp["pr"], "--repo", repo, "--title=" + inp["title"], "--body-file=" + path)
            url = "https://github.com/%s/pull/%s" % (repo, inp["pr"])
            verb = "edited"
    m = re.search(r"/pull/([0-9]+)\s*$", url)
    summary("%s #%s %s" % (verb, m.group(1) if m else "?", url.splitlines()[-1] if url else ""))


def main(argv):
    if len(argv) != 1 or argv[0] not in ("check", "apply"):
        print(__doc__)
        return 1
    try:
        inp = validate()
        check(inp) if argv[0] == "check" else apply(inp)
    except Reject as e:
        print("open-pr: rejected: %s" % e)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
