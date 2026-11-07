#!/usr/bin/env python3
"""Acceptance self-test for mutate.py and hygiene.py (README "Self-test").

Works on a throw-away clone of --repo under the system temp directory; --repo itself is only read.
Prints one line per check and exits 0 only when every selected check passes.
"""
import argparse
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.realpath(__file__))
TOOL = os.path.join(os.path.dirname(HERE), "mutate.py")
HYGIENE = os.path.join(os.path.dirname(HERE), "hygiene.py")
LISTS = os.path.join(os.path.dirname(HERE), "lists")
ST = os.path.join(LISTS, "selftest.txt")
EXTRA = os.path.join(LISTS, "selftest-extra.txt")
ZERO = os.path.join(LISTS, "selftest-zero.txt")

SEVEN = ["killed", "survived", "invalid (compile failed)", "invalid (build not effective)",
         "invalid (build not effective: old assembly loaded)",
         "invalid (expected tests did not fail on an assertion)",
         "invalid (expected tests did not fail on an assertion)"]

failures = []


def check(name, ok, info=""):
    sys.stdout.write("%s %s%s\n" % ("ok  " if ok else "FAIL", name, (" -- " + info) if info and not ok else ""))
    sys.stdout.flush()
    if not ok:
        failures.append(name)


def real(p):
    return os.path.realpath(p)


def git(repo, *a):
    return subprocess.run(["git", "-C", repo] + list(a), stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          check=True).stdout.decode()


def worktrees(repo):
    out = git(repo, "worktree", "list", "--porcelain")
    return {real(l[9:]) for l in out.split("\n") if l.startswith("worktree ")}


def common_dir(repo):
    c = git(repo, "rev-parse", "--git-common-dir").strip()
    return real(c if os.path.isabs(c) else os.path.join(repo, c))


class Ctx(object):
    def __init__(self, repo, work):
        self.repo = repo
        self.work = work
        self.n = 0

    def files(self):
        self.n += 1
        base = os.path.join(self.work, "run%02d" % self.n)
        return base + ".out", base + ".err", base + ".md"

    def start(self, lst, env_hooks="", extra=()):
        out, err, md = self.files()
        env = dict(os.environ)
        env.pop("WARCORE_MUTATION_SELFTEST", None)
        if env_hooks:
            env["WARCORE_MUTATION_SELFTEST"] = env_hooks
        p = subprocess.Popen([sys.executable, TOOL, "--repo", self.repo, "--out", md, lst] + list(extra),
                             stdout=open(out, "wb"), stderr=open(err, "wb"), env=env)
        return p, out, err, md

    def run(self, lst, env_hooks="", extra=()):
        p, out, err, md = self.start(lst, env_hooks, extra)
        rc = p.wait()
        return rc, read(out), read(err), md

    def clean(self, name, expected_worktrees):
        st = git(self.repo, "status", "--porcelain")
        wt = worktrees(self.repo)
        check(name + ": repository clean", st == "", st)
        check(name + ": worktrees unchanged", wt == expected_worktrees, str(sorted(wt)))


def read(p):
    try:
        with open(p, "r", encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


def verdicts(stdout):
    rows = []
    for line in stdout.split("\n"):
        cells = [c.strip() for c in line.split("|")]
        if len(cells) >= 7 and cells[1] and cells[1] not in ("id", "---", "filter") and not cells[1].startswith("`"):
            rows.append((cells[1], cells[2]))
    return rows


def first_line(text):
    return text.split("\n", 1)[0]


def wait_paused(err, timeout=900):
    end = time.time() + timeout
    while time.time() < end:
        for line in read(err).split("\n"):
            if line.startswith("PAUSED "):
                return line.split()
        time.sleep(0.5)
    return None


def markers(repo):
    d = os.path.join(common_dir(repo), "warcore-mutation")
    out = []
    for n in sorted(os.listdir(d)) if os.path.isdir(d) else []:
        if n.endswith(".marker"):
            with open(os.path.join(d, n)) as f:
                out.append([json.loads(l) for l in f.read().split("\n") if l.strip()])
    return out


def group_alive(pgid):
    r = subprocess.run(["ps", "-A", "-o", "pgid=,stat="], stdout=subprocess.PIPE)
    for line in r.stdout.decode().split("\n"):
        f = line.split()
        if len(f) == 2 and int(f[0]) == pgid and not f[1].startswith("Z"):
            return True
    return False


def pid_alive(pid):
    r = subprocess.run(["ps", "-o", "stat=", "-p", str(pid)], stdout=subprocess.PIPE)
    s = r.stdout.decode().strip()
    return s != "" and not s.startswith("Z")


# ------------------------------------------------------------------ cases


def case_seven(c, base_wt):
    rc, out, err, md = c.run(ST, "stale-loaded@selftest-5")
    v = verdicts(out)
    check("1: seven verdicts in order", [x[1] for x in v] == SEVEN, str(v) + " " + first_line(err))
    check("1: exit code 1", rc == 1, str(rc))
    c.clean("1/4a", base_wt)
    return out, md


def case_kill_only(c, base_wt):
    rc, out, err, md = c.run(ST, extra=["--only", "selftest-1"])
    check("1: only the must-kill entry exits 0", rc == 0 and verdicts(out) == [("selftest-1", "killed")],
          "%d %s %s" % (rc, verdicts(out), first_line(err)))
    c.clean("1-kill/4a", base_wt)


def case_restore(c, base_wt, hook, code, label):
    rc, out, err, md = c.run(ST, hook + "@selftest-1", ["--only", "selftest-1"])
    check("%s: exit 2 with %s" % (label, code), rc == 2 and first_line(err) == code, "%d %s" % (rc, first_line(err)))
    c.clean(label, base_wt)


def case_zero(c, base_wt):
    log = os.path.join(c.work, "zero.log")
    rc, out, err, md = c.run(ZERO, extra=["--log", log])
    check("2: zero-executed", rc == 2 and first_line(err) == "zero-executed", "%d %s" % (rc, first_line(err)))
    check("2: no mutation started", "mutant" not in read(log), read(log))
    c.clean("2", base_wt)


def case_extra(c, base_wt):
    rc, out, err, md = c.run(EXTRA)
    v = dict(verdicts(out))
    check("3: crash is invalid", v.get("selftest-crash", "").startswith("invalid ("), str(v))
    check("extra: only unexpected failures is invalid",
          v.get("selftest-unexpected") == "invalid (only tests outside the expected list failed)", str(v))
    check("3: exit code 1", rc == 1, "%d %s" % (rc, first_line(err)))
    c.clean("3", base_wt)


def add_unrelated(c):
    a = os.path.join(c.work, "unrelated-a")
    b = os.path.join(c.work, "unrelated-b")
    git(c.repo, "worktree", "add", "--detach", a, "HEAD")
    git(c.repo, "worktree", "add", "--detach", b, "HEAD")
    shutil.rmtree(b)  # directory gone, registration kept
    return real(a), real(b)


def case_kill9_during_test(c, base_wt):
    p, out, err, md = c.start(ST, "pause-after-release=mutant-test", ["--only", "selftest-1"])
    paused = wait_paused(err)
    check("4b: reached the mutant test", paused is not None, read(err))
    if paused is None:
        p.kill()
        return
    groups = [g["pgid"] for m in markers(c.repo) for g in m[1:]]
    check("4b: mutant test group alive at the kill", group_alive(int(paused[3])))
    p.send_signal(signal.SIGKILL)
    p.wait()
    rc, out2, err2, md2 = c.run(ST, extra=["--only", "selftest-2"])
    check("4b: restart runs", rc == 1 and verdicts(out2) == [("selftest-2", "survived")], "%d %s" % (rc, first_line(err2)))
    check("4b: recorded groups gone", groups and not any(group_alive(g) for g in groups), str(groups))
    check("4b: no markers left", markers(c.repo) == [])
    c.clean("4b/4c", base_wt)


def case_lock(c, base_wt):
    p, out, err, md = c.start(ST, extra=["--only", "selftest-1"])
    end = time.time() + 120
    while time.time() < end and not markers(c.repo):
        time.sleep(0.2)
    rc2, out2, err2, md2 = c.run(ST, extra=["--only", "selftest-1"])
    check("4d: second instance locked", rc2 == 2 and first_line(err2) == "locked", "%d %s" % (rc2, first_line(err2)))
    rc = p.wait()
    check("4d: first instance unchanged", rc == 0 and verdicts(read(out)) == [("selftest-1", "killed")],
          "%d %s" % (rc, verdicts(read(out))))
    c.clean("4d", base_wt)


def case_dirty(c, base_wt):
    f = os.path.join(c.repo, "tools", "headless", "README.md")
    with open(f, "rb") as h:
        orig = h.read()
    with open(f, "ab") as h:
        h.write(b"uncommitted\n")
    rc, out, err, md = c.run(ST, extra=["--only", "selftest-1"])
    with open(f, "wb") as h:
        h.write(orig)
    check("4e: dirty-repo", rc == 2 and first_line(err) == "dirty-repo", "%d %s" % (rc, first_line(err)))
    c.clean("4e", base_wt)


def case_kill_before_register(c, base_wt):
    p, out, err, md = c.start(ST, "pause-before-register=baseline-build", ["--only", "selftest-1"])
    paused = wait_paused(err)
    check("4f1: paused before registering the build", paused is not None, read(err))
    if paused is None:
        p.kill()
        return
    gate = int(paused[3])
    copy = markers(c.repo)[0][0]["copy"]
    p.send_signal(signal.SIGKILL)
    p.wait()
    end = time.time() + 10
    while time.time() < end and pid_alive(gate):
        time.sleep(0.1)
    check("4f1: gate exited by itself", not pid_alive(gate))
    hb = os.path.join(copy, "tools", "headless")
    ran = any(os.path.exists(os.path.join(hb, d)) for d in ("bin", "obj"))
    check("4f1: no dotnet ran in the copy", os.path.isdir(copy) and not ran)
    rc, out2, err2, md2 = c.run(ST, extra=["--only", "selftest-2"])
    check("4f1: restart runs", rc == 1, "%d %s" % (rc, first_line(err2)))
    check("4f1: copy removed", not os.path.exists(copy))
    c.clean("4f1", base_wt)


def case_kill_after_add_release(c, base_wt):
    p, out, err, md = c.start(ST, "pause-after-release=worktree-add", ["--only", "selftest-1"])
    paused = wait_paused(err)
    check("4f2: paused after releasing worktree add", paused is not None, read(err))
    if paused is None:
        p.kill()
        return
    copy = real(markers(c.repo)[0][0]["copy"])
    p.send_signal(signal.SIGKILL)
    p.wait()
    rc, out2, err2, md2 = c.run(ST, extra=["--only", "selftest-2"])
    check("4f2: restart runs", rc == 1, "%d %s" % (rc, first_line(err2)))
    check("4f2: copy directory gone", not os.path.exists(copy))
    check("4f2: copy registration gone", copy not in worktrees(c.repo))
    c.clean("4f2/4c", base_wt)


def case_hygiene(work, repo):
    h = os.path.join(work, "hygiene")
    subprocess.run(["git", "clone", "-q", repo, h], check=True)
    for k, v in (("user.name", "selftest"), ("user.email", "selftest@invalid")):
        git(h, "config", k, v)
    git(h, "config", "core.hooksPath", "/dev/null")
    t = os.path.join(h, "Assets", "WarCore", "Tests", "EditMode", "HygieneSelfTest.cs")

    def commit(lines, msg):
        with open(t, "w") as f:
            f.write("\n".join(lines) + "\n")
        git(h, "add", "-A")
        git(h, "commit", "-q", "-m", msg)
        return git(h, "rev-parse", "HEAD").strip()

    def hy(b, e):
        r = subprocess.run([sys.executable, HYGIENE, "--repo", h, b, e], stdout=subprocess.PIPE)
        return r.returncode, r.stdout.decode()

    old = "        Assert.That(a, Is.EqualTo(1).Or.Not.EqualTo(2));"
    new = "        Assert.That(b, Is.EqualTo(3).Or.Not.EqualTo(4));"
    keep = ["class H {", "    void M() {", "        int a = 1;"]
    b0 = commit(keep + [old, "    }", "}"], "base")
    c1 = commit(keep + [old, new, "    }", "}"], "add one")
    rc, out = hy(b0, c1)
    check("6: added line fails with file and line",
          rc == 1 and "Assets/WarCore/Tests/EditMode/HygieneSelfTest.cs:5: or-not-equal" in out, out)
    git(h, "reset", "-q", "--hard", b0)
    c2 = commit(keep + [new, "    }", "}"], "add one, delete one")
    rc, out = hy(b0, c2)
    check("6: net zero still fails", rc == 1 and "HygieneSelfTest.cs:4: or-not-equal" in out, out)
    git(h, "reset", "-q", "--hard", b0)
    c3 = commit(keep + ["    }", "}"], "delete only")
    rc, out = hy(b0, c3)
    check("6: delete only passes", rc == 0, out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=".")
    ap.add_argument("--only", help="comma-separated case names: seven,killonly,restore,zero,extra,kill9,lock,"
                                   "dirty,before,afteradd,hygiene")
    ap.add_argument("--keep", action="store_true", help="keep the work directory")
    a = ap.parse_args()
    src = real(git(a.repo, "rev-parse", "--show-toplevel").strip())
    work = real(tempfile.mkdtemp(prefix="warcore-mutation-selftest-"))
    repo = os.path.join(work, "repo")
    subprocess.run(["git", "clone", "-q", src, repo], check=True)
    c = Ctx(repo, work)
    sys.stdout.write("work directory %s\n" % work)
    want = set(a.only.split(",")) if a.only else None

    def on(name):
        return want is None or name in want

    base_wt = worktrees(repo)
    if on("seven"):
        case_seven(c, base_wt)
    if on("killonly"):
        case_kill_only(c, base_wt)
    if on("restore"):
        case_restore(c, base_wt, "restore-short", "restore-mismatch", "8")
        case_restore(c, base_wt, "restore-stale", "baseline-mismatch", "9")
    if on("zero"):
        case_zero(c, base_wt)
    if on("extra"):
        case_extra(c, base_wt)
    if on("dirty"):
        case_dirty(c, base_wt)
    if on("lock"):
        case_lock(c, base_wt)
    if on("kill9") or on("before") or on("afteradd"):
        ua, ub = add_unrelated(c)
        wt = worktrees(repo)
        check("4c: two unrelated worktrees registered", ua in wt and ub in wt)
        if on("kill9"):
            case_kill9_during_test(c, wt)
        if on("before"):
            case_kill_before_register(c, wt)
        if on("afteradd"):
            case_kill_after_add_release(c, wt)
        wt2 = worktrees(repo)
        check("4c: unrelated registrations kept", ua in wt2 and ub in wt2, str(sorted(wt2)))
    if on("hygiene"):
        case_hygiene(work, src)
    sys.stdout.write("selftest: %s\n" % ("FAIL " + ", ".join(failures) if failures else "PASS"))
    if not a.keep and not failures:
        shutil.rmtree(work, ignore_errors=True)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
