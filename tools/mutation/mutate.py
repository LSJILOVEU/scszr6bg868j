#!/usr/bin/env python3
"""Mutation runner for the headless WarCore test project (Linux and macOS, standard library only).

Every mutation is made in an isolated detached worktree under the system temp directory; the source
repository is never written. See tools/mutation/README.md for the list format, verdict rules and exit codes.
"""
import argparse
import fcntl
import hashlib
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

PROJECT = "tools/headless/WarCore.Headless.Tests.csproj"
PRODUCTION = ("Sim", "Combat", "Content", "Control", "Interim", "Presentation", "Battle")
TEST_ASSEMBLY = "WarCore.Sim.Tests"
LOADED_DIR = "tools/headless/bin/Release/net8.0"
TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
MIN_TIMEOUT = 600
DEFAULT_TIMEOUT = 900
GROUP_WAIT = 60
ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")

# The gate: a new session (own process group) that blocks on a pipe until the runner has recorded the group,
# then execs the real command. EOF (runner died first) means exit without doing anything.
GATE = (
    "import os,sys\n"
    "fd=int(sys.argv[1])\n"
    "b=os.read(fd,1)\n"
    "if not b: os._exit(0)\n"
    "os.close(fd)\n"
    "os.execvp(sys.argv[2],sys.argv[2:])\n"
)

INVALID_COMPILE = "invalid (compile failed)"
INVALID_NOT_BUILT = "invalid (build not effective)"
INVALID_STALE = "invalid (build not effective: old assembly loaded)"
INVALID_RESULT = "invalid (results missing)"
INVALID_COUNT = "invalid (executed count changed)"
INVALID_TIMEOUT = "invalid (timeout)"
INVALID_NOT_ASSERT = "invalid (expected tests did not fail on an assertion)"
INVALID_UNEXPECTED = "invalid (only tests outside the expected list failed)"
INVALID_SOURCE = "invalid (original text not found exactly once)"
INVALID_EXPECT = "invalid (an expected name matches no baseline test)"
KILLED = "killed"
SURVIVED = "survived"


class Fatal(Exception):
    def __init__(self, code, text):
        Exception.__init__(self, code)
        self.code = code
        self.text = text


class Interrupted(Exception):
    pass


# ---------------------------------------------------------------- list format


def parse_list(path):
    entries = []
    cur = None
    block = None
    try:
        with open(path, "r", encoding="utf-8") as f:
            lines = f.read().split("\n")
    except (OSError, UnicodeDecodeError) as ex:
        raise Fatal("bad-list", "%s: cannot read the list as UTF-8 text: %s" % (path, ex))
    if lines and lines[-1] == "":
        lines.pop()

    def bad(n, msg):
        raise Fatal("bad-list", "%s:%d: %s" % (path, n, msg))

    def close(n):
        if cur is None:
            return
        for k in ("file", "filter", "note"):
            if not cur.get(k):
                bad(n, "entry %s has no %s" % (cur["id"], k))
        if not cur["expect"]:
            bad(n, "entry %s has no expect" % cur["id"])
        if cur.get("original") is None or cur["original"] == "":
            bad(n, "entry %s has no original text" % cur["id"])
        if cur.get("replacement") is None:
            bad(n, "entry %s has no replacement block" % cur["id"])
        entries.append(cur)

    for n, raw in enumerate(lines, 1):
        line = raw.rstrip("\r")
        if block is not None and (line == "|" or line.startswith("| ")):
            cur[block].append(line[2:])
            continue
        if block is not None:
            cur[block] = "\n".join(cur[block])
            block = None
        if line.strip() == "" or line.lstrip().startswith("#"):
            continue
        m = re.match(r"^\[(.+)\]$", line)
        if m:
            close(n)
            mid = m.group(1)
            if not ID_RE.match(mid):
                bad(n, "bad id")
            if any(e["id"] == mid for e in entries):
                bad(n, "duplicate id " + mid)
            cur = {"id": mid, "expect": []}
            continue
        if cur is None:
            bad(n, "text outside an entry")
        if line in ("original:", "replacement:"):
            key = line[:-1]
            if key in cur:
                bad(n, "second %s block" % key)
            block = key
            cur[key] = []
            continue
        m = re.match(r"^(file|filter|expect|note)\s*=\s*(.*)$", line)
        if not m:
            bad(n, "unknown line")
        key, val = m.group(1), m.group(2).strip()
        if val == "":
            bad(n, "empty " + key)
        if key == "expect":
            cur["expect"].append(val)
        elif key in cur:
            bad(n, "second " + key)
        else:
            cur[key] = val
    if block is not None:
        cur[block] = "\n".join(cur[block])
    close(len(lines))
    for e in entries:
        p = e["file"]
        if p.startswith("/") or "\\" in p or ".." in p.split("/") or os.path.normpath(p) != p:
            raise Fatal("bad-list", "%s: entry %s: file must be a normalized repository-relative path" % (path, e["id"]))
    if not entries:
        raise Fatal("bad-list", "%s: no entries" % path)
    return entries


def assembly_of(path):
    m = re.match(r"^Assets/WarCore/(%s)/" % "|".join(PRODUCTION), path)
    if m:
        return "WarCore." + m.group(1)
    if path.startswith("Assets/WarCore/Tests/EditMode/") or path.startswith("Assets/WarCore/Tests/MapContent/"):
        return TEST_ASSEMBLY
    if re.match(r"^tools/headless/[^/]+\.cs$", path):
        return TEST_ASSEMBLY
    return None


def new_artifact(asm):
    if asm == TEST_ASSEMBLY:
        return "tools/headless/obj/Release/net8.0/%s.dll" % asm
    return "tools/headless/%s/bin/Release/net8.0/%s.dll" % (asm[len("WarCore."):], asm)


def loaded_artifact(asm):
    return "%s/%s.dll" % (LOADED_DIR, asm)


ALL_ASSEMBLIES = ["WarCore." + x for x in PRODUCTION] + [TEST_ASSEMBLY]


# ---------------------------------------------------------------- small helpers


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


def sha_file(p):
    try:
        with open(p, "rb") as f:
            return sha_bytes(f.read())
    except OSError:
        return None


def real(p):
    return os.path.realpath(p)


def inside(path, root):
    return path == root or path.startswith(root.rstrip(os.sep) + os.sep)


def git(repo, *args, check=True):
    r = subprocess.run(["git", "-C", repo] + list(args), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if check and r.returncode != 0:
        raise Fatal("git", "git %s failed: %s" % (" ".join(args), r.stderr.decode("utf-8", "replace").strip()))
    return r.stdout.decode("utf-8", "replace")


def worktree_paths(repo):
    out = git(repo, "worktree", "list", "--porcelain")
    return {real(l[len("worktree "):]) for l in out.split("\n") if l.startswith("worktree ")}


def ps_env():
    env = dict(os.environ)
    env["LC_ALL"] = "C"
    return env


def lstart_of(pid):
    r = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                       env=ps_env())
    text = " ".join(r.stdout.decode("utf-8", "replace").split())
    try:
        parse_lstart(text)
    except ValueError:
        raise Fatal("stale-process", "cannot read the start time of process %d" % pid)
    return text


def parse_lstart(s):
    return time.mktime(time.strptime(" ".join(s.split()), "%a %b %d %H:%M:%S %Y"))


def processes():
    """(pid, pgid, lstart) of every live (non-zombie) process."""
    r = subprocess.run(["ps", "-A", "-o", "pid=,pgid=,stat=,lstart="], stdout=subprocess.PIPE,
                       stderr=subprocess.DEVNULL, env=ps_env())
    lines = [l for l in r.stdout.decode("utf-8", "replace").split("\n") if l.strip()]
    if r.returncode != 0 or not lines:
        raise Fatal("stale-process", "ps -A failed (exit %d)" % r.returncode)
    out = []
    for line in lines:
        f = line.split()
        try:
            if len(f) != 8:
                raise ValueError(line)
            if f[2].startswith("Z"):
                continue
            out.append((int(f[0]), int(f[1]), " ".join(f[3:8])))
        except ValueError:
            raise Fatal("stale-process", "unreadable ps line: " + line)
    return out


def group_members(pgid):
    return [p for p in processes() if p[1] == pgid]


def kill_group_and_wait(pgid):
    try:
        os.killpg(pgid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    except PermissionError:
        return False
    end = time.time() + GROUP_WAIT
    while time.time() < end:
        if not group_members(pgid):
            return True
        time.sleep(0.2)
    return not group_members(pgid)


def write_fsync(path, text, mode="a"):
    with open(path, mode, encoding="utf-8") as f:
        f.write(text)
        f.flush()
        os.fsync(f.fileno())


def selftest_hooks():
    """WARCORE_MUTATION_SELFTEST: comma-separated 'hook@mutation-id' or 'pause-...=stage' items (self-test only)."""
    hooks = {}
    pauses = {}
    for item in os.environ.get("WARCORE_MUTATION_SELFTEST", "").split(","):
        item = item.strip()
        if not item:
            continue
        if "@" in item:
            h, mid = item.split("@", 1)
            hooks.setdefault(mid, set()).add(h)
        elif "=" in item:
            p, stage = item.split("=", 1)
            pauses[p] = stage
    return hooks, pauses


# ---------------------------------------------------------------- the runner


class Runner(object):
    def __init__(self, args):
        self.args = args
        self.hooks, self.pauses = selftest_hooks()
        self.groups = []  # pgids started by this run
        self.marker = None
        self.copy = None
        self.tmp = None
        self.w1 = None
        self.repo = None
        self.state_dir = None
        self.result_seq = 0
        self.log = None
        self.mutant_bytes = {}

    def progress(self, msg):
        if self.args.log:
            write_fsync(self.args.log, "%s %s\n" % (time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), msg))

    def pause(self, point, stage, pid):
        if self.pauses.get(point) == stage:
            sys.stderr.write("PAUSED %s %s %d\n" % (point, stage, pid))
            sys.stderr.flush()
            while True:
                time.sleep(3600)

    # -- gated child processes

    def run_gated(self, cmd, cwd, stage, timeout):
        """Run cmd in its own process group, released only after the group is on the marker. Returns (rc, timed_out)."""
        r, w = os.pipe()
        env = dict(os.environ)
        env.update({"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1", "MSBUILDDISABLENODEREUSE": "1",
                    "DOTNET_CLI_USE_MSBUILD_SERVER": "0", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1"})
        env.pop("WARCORE_MUTATION_SELFTEST", None)
        p = subprocess.Popen([sys.executable, "-c", GATE, str(r)] + cmd, cwd=cwd, pass_fds=(r,),
                             start_new_session=True, stdin=subprocess.DEVNULL, stdout=self.log,
                             stderr=subprocess.STDOUT, env=env)
        os.close(r)
        self.groups.append(p.pid)
        try:
            self.pause("pause-before-register", stage, p.pid)
            write_fsync(self.marker, json.dumps({"pgid": p.pid, "leader": p.pid, "lstart": lstart_of(p.pid),
                                                 "stage": stage}) + "\n")
            os.write(w, b"x")
        finally:
            os.close(w)
        self.pause("pause-after-release", stage, p.pid)
        timed_out = False
        try:
            rc = p.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            rc = None
        except Interrupted:
            self.stop_group(p)
            raise
        if timed_out or group_members(p.pid):
            self.stop_group(p)
        self.groups.remove(p.pid)
        return rc, timed_out

    def stop_group(self, p):
        """Kill the child's whole group; it must be empty and the leader reaped within GROUP_WAIT seconds."""
        if not kill_group_and_wait(p.pid):
            raise Fatal("stale-process", "process group %d did not exit" % p.pid)
        try:
            p.wait(timeout=GROUP_WAIT)
        except subprocess.TimeoutExpired:
            raise Fatal("stale-process", "process %d was not reaped" % p.pid)

    # -- lifecycle

    def start(self):
        a = self.args
        top = subprocess.run(["git", "-C", a.repo, "rev-parse", "--show-toplevel"], stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE)
        if top.returncode != 0:
            raise Fatal("not-a-repo", "not a git repository: " + a.repo)
        self.repo = real(top.stdout.decode().strip())
        for opt, target in (("--out", a.out), ("--log", a.log)):
            if target and inside(real(target), self.repo):
                raise Fatal("bad-args", "%s must be outside the repository" % opt)
        if git(self.repo, "status", "--porcelain").strip():
            raise Fatal("dirty-repo", "the repository has uncommitted changes")
        common = git(self.repo, "rev-parse", "--git-common-dir").strip()
        if not os.path.isabs(common):
            common = os.path.join(self.repo, common)
        self.common = real(common)
        self.state_dir = os.path.join(self.common, "warcore-mutation")
        os.makedirs(self.state_dir, exist_ok=True)
        self.lock_fd = os.open(os.path.join(self.state_dir, "lock"), os.O_RDWR | os.O_CREAT, 0o644)
        try:
            fcntl.flock(self.lock_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            raise Fatal("locked", "another mutation run holds " + os.path.join(self.state_dir, "lock"))
        w0 = worktree_paths(self.repo)
        stale = self.clean_stale()
        self.w1 = w0 - stale
        if worktree_paths(self.repo) != self.w1:
            raise Fatal("worktree-leak", "worktrees after stale cleanup differ from the expected set")
        self.head = git(self.repo, "rev-parse", "HEAD").strip()
        self.tmp = real(tempfile.mkdtemp(prefix="warcore-mutation-"))
        self.copy = os.path.join(self.tmp, "wt")
        self.marker = os.path.join(self.state_dir, "%d.marker" % os.getpid())
        write_fsync(self.marker, json.dumps({"repo": self.repo, "copy": self.copy, "tmp": self.tmp,
                                             "pid": os.getpid(), "lstart": lstart_of(os.getpid())}) + "\n", "w")
        self.log = open(os.path.join(self.tmp, "commands.log"), "ab")
        rc, to = self.run_gated(["git", "-C", self.repo, "worktree", "add", "--detach", self.copy, self.head],
                                self.repo, "worktree-add", self.args.timeout)
        if rc != 0:
            raise Fatal("git", "git worktree add failed")

    def clean_stale(self):
        """Every marker belongs to a dead run (live runs hold the lock). Returns the copy paths it owned."""
        copies = set()
        for name in sorted(os.listdir(self.state_dir)):
            if not name.endswith(".marker"):
                continue
            mpath = os.path.join(self.state_dir, name)
            recs = []
            with open(mpath, "r", encoding="utf-8") as f:
                for l in f.read().split("\n"):
                    try:
                        recs.append(json.loads(l))
                    except ValueError:
                        pass  # a torn last line: its gate was never released and exits on EOF
            if not recs or "copy" not in recs[0]:
                os.remove(mpath)  # torn header: nothing was created after it
                continue
            head = recs[0]
            for g in recs[1:]:
                members = group_members(g["pgid"])
                if not members:
                    continue
                reg = parse_lstart(g["lstart"])
                leader = [m for m in members if m[0] == g["leader"]]
                if leader:
                    ours = " ".join(leader[0][2].split()) == " ".join(g["lstart"].split())
                else:
                    ours = all(parse_lstart(m[2]) >= reg for m in members)
                if not ours:
                    raise Fatal("stale-process", "process group %d of %s is not recognised" % (g["pgid"], name))
                if not kill_group_and_wait(g["pgid"]):
                    raise Fatal("stale-process", "process group %d of %s did not exit" % (g["pgid"], name))
            copy = real(head["copy"])
            self.remove_copy(copy)
            tmp = head.get("tmp")
            if tmp and os.path.isdir(tmp) and os.path.basename(tmp).startswith("warcore-mutation-"):
                shutil.rmtree(tmp, ignore_errors=True)
            os.remove(mpath)
            copies.add(copy)
        return copies

    def admin_dir_of(self, copy):
        wdir = os.path.join(self.common, "worktrees")
        if not os.path.isdir(wdir):
            return None
        for d in os.listdir(wdir):
            g = os.path.join(wdir, d, "gitdir")
            try:
                with open(g, "r", encoding="utf-8") as f:
                    target = f.read().strip()
            except OSError:
                continue
            if real(target) == real(os.path.join(copy, ".git")):
                return os.path.join(wdir, d)
        return None

    def remove_copy(self, copy):
        if copy in worktree_paths(self.repo) and os.path.isdir(copy):
            git(self.repo, "worktree", "remove", "--force", copy, check=False)
        admin = self.admin_dir_of(copy)
        if admin:
            shutil.rmtree(admin)
        if os.path.isdir(copy):
            shutil.rmtree(copy)

    def finish(self):
        """Shared exit path: groups gone, copy removed (no global prune), marker removed, worktree set == W1."""
        errors = []
        for pg in self.groups:
            if group_members(pg) and not kill_group_and_wait(pg):
                errors.append(("stale-process", "process group %d did not exit" % pg))
        if errors:
            return errors
        if self.log:
            self.log.close()
        if self.copy:
            self.remove_copy(self.copy)
        if self.tmp and os.path.isdir(self.tmp):
            shutil.rmtree(self.tmp, ignore_errors=True)
        if self.marker and os.path.exists(self.marker):
            os.remove(self.marker)
        if self.w1 is not None and worktree_paths(self.repo) != self.w1:
            errors.append(("worktree-leak", "worktrees at exit differ from the set after stale cleanup"))
        return errors

    # -- build and test

    def build(self, stage, timeout):
        return self.run_gated(["dotnet", "build", PROJECT, "-c", "Release", "--disable-build-servers"],
                              self.copy, stage, timeout)

    def hashes(self):
        h = {}
        for asm in ALL_ASSEMBLIES:
            h[asm] = (sha_file(os.path.join(self.copy, new_artifact(asm))),
                      sha_file(os.path.join(self.copy, loaded_artifact(asm))))
        return h

    def test(self, flt, stage, timeout):
        self.result_seq += 1
        d = os.path.join(self.tmp, "results", "%04d" % self.result_seq)
        os.makedirs(d)
        rc, to = self.run_gated(["dotnet", "test", PROJECT, "-c", "Release", "--no-build", "--filter", flt,
                                 "--results-directory", d, "--logger", "trx", "--",
                                 "NUnit.TestOutputXml=" + os.path.join(d, "nunit")], self.copy, stage, timeout)
        if to:
            return {"timeout": True}
        return read_results(d)

    # -- the run

    def run(self, entries):
        a = self.args
        if a.only:
            ids = a.only.split(",")
            missing = [i for i in ids if not any(e["id"] == i for e in entries)]
            if missing:
                raise Fatal("bad-list", "--only names unknown ids: " + ",".join(missing))
            entries = [e for e in entries if e["id"] in ids]
        self.start()
        self.progress("copy " + self.copy)
        rc, to = self.build("baseline-build", a.timeout)
        if rc != 0:
            raise Fatal("baseline-build", "the baseline build failed")
        base = self.hashes()
        missing = [asm for asm, (n, l) in base.items() if n is None or l is None or n != l]
        if missing:
            raise Fatal("baseline-build", "baseline assemblies missing or not copied: " + ",".join(missing))
        self.base = base
        self.base_bytes = {}
        for asm in ALL_ASSEMBLIES:
            with open(os.path.join(self.copy, loaded_artifact(asm)), "rb") as f:
                self.base_bytes[asm] = f.read()
        self.base_counts = {}
        self.base_names = {}
        for flt in sorted({e["filter"] for e in entries}):
            res = self.test(flt, "baseline-test", a.timeout)
            if res.get("timeout") or res.get("invalid"):
                raise Fatal("baseline-result", "baseline results for filter %s are missing or invalid: %s"
                            % (flt, res.get("invalid", "timeout")))
            if res["executed"] == 0:
                raise Fatal("zero-executed", "filter %s executed 0 tests" % flt)
            if res["failed"]:
                raise Fatal("baseline-red", "filter %s is red before any mutation: %s" % (flt, ", ".join(res["failed"][:3])))
            self.base_counts[flt] = res["executed"]
            self.base_names[flt] = res["names"]
            self.progress("baseline %s: %d" % (flt, res["executed"]))
        rows = []
        for e in entries:
            rows.append(self.mutate(e))
        return rows

    def mutate(self, e):
        a = self.args
        hooks = self.hooks.get(e["id"], set())
        t0 = time.time()
        row = {"id": e["id"], "verdict": None, "failed": [], "executed": None, "note": e["note"], "detail": "",
               "asm": assembly_of(e["file"]), "sha_before": None, "sha_after": None}
        path = os.path.join(self.copy, e["file"])
        if real(path) != path or os.path.islink(path) or not os.path.isfile(path):
            row["verdict"] = INVALID_SOURCE
            row["detail"] = "file not found, or reached through a symbolic link"
            row["seconds"] = time.time() - t0
            return row
        with open(path, "rb") as f:
            orig = f.read()
        orig_sha = sha_bytes(orig)
        needle = e["original"].encode("utf-8")
        count = orig.count(needle)
        if count != 1:
            row["verdict"] = INVALID_SOURCE
            row["detail"] = "original occurs %d times" % count
            row["seconds"] = time.time() - t0
            return row
        unmatched = [x for x in e["expect"] if not any(x in n for n in self.base_names[e["filter"]])]
        if unmatched:
            row["verdict"] = INVALID_EXPECT
            row["detail"] = "; ".join(unmatched)
            row["seconds"] = time.time() - t0
            return row
        asm = row["asm"]
        if asm:
            row["sha_before"] = self.base[asm][0]
        deadline = t0 + a.timeout
        try:
            with open(path, "wb") as f:
                f.write(orig.replace(needle, e["replacement"].encode("utf-8"), 1))
            self.progress("%s: mutant build" % e["id"])
            rc, to = self.build("mutant-build", max(1, deadline - time.time()))
            if to:
                row["verdict"] = INVALID_TIMEOUT
            elif rc != 0:
                row["verdict"] = INVALID_COMPILE
            else:
                if "stale-loaded" in hooks and asm:
                    with open(os.path.join(self.copy, loaded_artifact(asm)), "wb") as f:
                        f.write(self.base_bytes[asm])
                now = self.hashes()
                if asm:
                    row["sha_after"] = now[asm][0]
                if asm is None or now[asm][0] == self.base[asm][0]:
                    row["verdict"] = INVALID_NOT_BUILT
                elif now[asm][1] != now[asm][0]:
                    row["verdict"] = INVALID_STALE
                else:
                    with open(os.path.join(self.copy, loaded_artifact(asm)), "rb") as f:
                        self.mutant_bytes[e["id"]] = f.read()
                    self.progress("%s: mutant test" % e["id"])
                    res = self.test(e["filter"], "mutant-test", max(1, deadline - time.time()))
                    self.judge(e, res, row)
        finally:
            self.restore(e, path, orig, orig_sha, asm, hooks)
        row["seconds"] = time.time() - t0
        return row

    def judge(self, e, res, row):
        if res.get("timeout"):
            row["verdict"] = INVALID_TIMEOUT
            return
        if res.get("invalid"):
            row["verdict"] = INVALID_RESULT
            row["detail"] = res["invalid"]
            return
        row["executed"] = res["executed"]
        row["failed"] = res["failed"]
        row["failures"] = res["failures"]
        if res["executed"] != self.base_counts[e["filter"]]:
            row["verdict"] = INVALID_COUNT
            return
        if not res["failed"]:
            row["verdict"] = SURVIVED
            return
        expected = [n for n in res["failed"] if any(x in n for x in e["expect"])]
        if any(res["failures"][n]["assertion"] for n in expected):
            row["verdict"] = KILLED
        elif expected:
            row["verdict"] = INVALID_NOT_ASSERT
        else:
            row["verdict"] = INVALID_UNEXPECTED
            row["detail"] = ", ".join(res["failed"])

    def restore(self, e, path, orig, orig_sha, asm, hooks):
        a = self.args
        data = orig[:-1] if "restore-short" in hooks else orig
        with open(path, "wb") as f:
            f.write(data)
        if sha_file(path) != orig_sha:
            raise Fatal("restore-mismatch", "%s: restored file sha256 differs from the original" % e["id"])
        rc, to = self.build("restore-build", a.timeout)
        if rc != 0 or to:
            raise Fatal("baseline-mismatch", "%s: the build after restore failed" % e["id"])
        if "restore-stale" in hooks and asm and e["id"] in self.mutant_bytes:
            with open(os.path.join(self.copy, loaded_artifact(asm)), "wb") as f:
                f.write(self.mutant_bytes[e["id"]])
        now = self.hashes()
        for x in ALL_ASSEMBLIES:
            if now[x] != (self.base[x][0], self.base[x][0]):
                raise Fatal("baseline-mismatch", "%s: %s is not back to its baseline bytes after restore" % (e["id"], x))
        res = self.test(e["filter"], "restore-test", a.timeout)
        if res.get("timeout") or res.get("invalid") or res["failed"] or res["executed"] != self.base_counts[e["filter"]]:
            raise Fatal("baseline-red", "%s: filter is not green with the baseline count after restore" % e["id"])


# ---------------------------------------------------------------- results


def read_results(d):
    trx, xmls = [], []
    for root, _, files in os.walk(d):
        for f in files:
            if f.endswith(".trx"):
                trx.append(os.path.join(root, f))
            elif f.endswith(".xml"):
                xmls.append(os.path.join(root, f))
    if len(trx) != 1 or len(xmls) != 1:
        return {"invalid": "%d trx and %d NUnit XML files" % (len(trx), len(xmls))}
    try:
        return parse_results(trx[0], xmls[0])
    except (ET.ParseError, ValueError, TypeError, AttributeError, KeyError) as ex:
        return {"invalid": "unreadable result file: %s: %s" % (type(ex).__name__, ex)}


def parse_results(trx_path, xml_path):
    t = ET.parse(trx_path).getroot()
    x = ET.parse(xml_path).getroot()
    summary = t.find("t:ResultSummary", TRX_NS)
    counters = summary.find("t:Counters", TRX_NS) if summary is not None else None
    if summary is None or counters is None:
        return {"invalid": "trx has no ResultSummary/Counters"}
    if summary.get("outcome") in ("Aborted", "Error"):
        return {"invalid": "trx outcome " + summary.get("outcome")}
    executed = int(counters.get("executed"))
    if executed < 0:
        raise ValueError("negative executed count")
    defs = {}
    for ut in t.iterfind(".//t:TestDefinitions/t:UnitTest", TRX_NS):
        m = ut.find("t:TestMethod", TRX_NS)
        defs[ut.get("id")] = m.get("className") + "." + ut.get("name")
    trx_names = []
    for r in t.iterfind(".//t:Results/t:UnitTestResult", TRX_NS):
        if r.get("testId") not in defs:
            return {"invalid": "trx result without a definition"}
        if r.get("outcome") != "NotExecuted":
            trx_names.append(defs[r.get("testId")])
    cases = {}
    for tc in x.iter("test-case"):
        cases[tc.get("fullname")] = tc
    if len(trx_names) != executed or len(set(trx_names)) != executed:
        return {"invalid": "trx lists %d results for %d executed" % (len(trx_names), executed)}
    if set(trx_names) - set(cases):
        return {"invalid": "trx and NUnit XML name different tests"}
    failed = []
    failures = {}
    for n in sorted(set(trx_names)):
        tc = cases[n]
        if tc.get("result") != "Passed":
            label, site = tc.get("label"), tc.get("site")
            failed.append(n)
            failures[n] = {"result": tc.get("result"), "label": label, "site": site,
                           "assertion": tc.get("result") == "Failed" and label is None and site in (None, "Test")}
    return {"executed": executed, "failed": failed, "failures": failures, "names": sorted(set(trx_names))}


# ---------------------------------------------------------------- output


def cell(text):
    return str(text).replace("\\", "\\\\").replace("|", "\\|").replace("\n", " ")


def table(runner, rows):
    out = []
    out.append("Repository HEAD: %s" % runner.head)
    out.append("")
    out.append("| filter | baseline executed |")
    out.append("|---|---|")
    for flt in sorted(runner.base_counts):
        out.append("| `%s` | %d |" % (cell(flt), runner.base_counts[flt]))
    out.append("")
    out.append("| id | verdict | failed tests (first 3) | executed | seconds |")
    out.append("|---|---|---|---|---|")
    for r in rows:
        names = r["failed"][:3]
        failed = "<br>".join("`%s`" % cell(n) for n in names) if names else "-"
        if len(r["failed"]) > 3:
            failed += " (+%d)" % (len(r["failed"]) - 3)
        out.append("| %s | %s | %s | %s | %.0f |" % (cell(r["id"]), cell(r["verdict"]), failed,
                                                    "-" if r["executed"] is None else r["executed"], r["seconds"]))
    return "\n".join(out) + "\n"


def details(rows):
    out = ["", "## Details", ""]
    for r in rows:
        out.append("### %s" % r["id"])
        out.append("")
        out.append("- note: %s" % r["note"])
        out.append("- verdict: %s%s" % (r["verdict"], (" -- " + r["detail"]) if r["detail"] else ""))
        out.append("- assembly: %s" % (r["asm"] or "(not mapped)"))
        out.append("- sha256 before: %s" % (r["sha_before"] or "-"))
        out.append("- sha256 after: %s" % (r["sha_after"] or "-"))
        out.append("- failed tests: %d" % len(r["failed"]))
        for n in r["failed"]:
            f = r["failures"][n]
            out.append("  - `%s` result=%s label=%s site=%s" % (n, f["result"], f["label"] if f["label"] is not None else "(none)",
                                                    f["site"] if f["site"] is not None else "(none)"))
        out.append("")
    return "\n".join(out)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Run a mutation list against the headless test project.")
    ap.add_argument("list", help="mutation list file (see README.md)")
    ap.add_argument("--repo", default=".", help="repository (default: current directory)")
    ap.add_argument("--out", help="also write the table and per-mutation details to this file")
    ap.add_argument("--only", help="comma-separated ids to run")
    ap.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT,
                    help="seconds per mutation (build and test), default %d, minimum %d" % (DEFAULT_TIMEOUT, MIN_TIMEOUT))
    ap.add_argument("--log", help="append progress lines to this file")
    args = ap.parse_args(argv)
    if args.timeout < MIN_TIMEOUT:
        sys.stderr.write("bad-args\n--timeout must be at least %d\n" % MIN_TIMEOUT)
        return 2
    runner = Runner(args)

    def on_signal(signum, frame):
        raise Interrupted()

    signal.signal(signal.SIGINT, on_signal)
    signal.signal(signal.SIGTERM, on_signal)
    error = None
    rows = None
    try:
        entries = parse_list(args.list)
        rows = runner.run(entries)
    except Fatal as ex:
        error = (ex.code, ex.text)
    except Interrupted:
        error = ("interrupted", "stopped by a signal")
    except Exception as ex:  # never a traceback instead of an error id
        error = ("internal", "%s: %s" % (type(ex).__name__, ex))
    finally:
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        try:
            late = runner.finish() if runner.repo and runner.w1 is not None else []
        except Fatal as ex:
            late = [(ex.code, ex.text)]
        except Exception as ex:
            late = [("internal", "finish: %s: %s" % (type(ex).__name__, ex))]
    errors = ([error] if error else []) + late
    if errors:
        sys.stderr.write(errors[0][0] + "\n")
        for code, text in errors:
            sys.stderr.write("%s: %s\n" % (code, text))
        return 2
    text = table(runner, rows)
    sys.stdout.write(text)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write(text + details(rows))
    return 0 if all(r["verdict"] == KILLED for r in rows) else 1


if __name__ == "__main__":
    sys.exit(main())
