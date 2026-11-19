#!/usr/bin/env python3
"""Offline tests for open_pr.py: a throwaway git remote, the real pii_guard.py, and a fake gh that records calls.

Run: python3 -I tools/open-pr/test_open_pr.py
Strings that pii-guard must catch are assembled at run time so this file itself passes the tree scan.
"""
import json, os, shutil, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "open_pr.py")
GUARD = os.path.join(HERE, "..", "pii-guard", "pii_guard.py")

FAKE_GH = r'''#!/usr/bin/env python3
import json, os, sys
a = sys.argv[1:]
rec = {"argv": a}
for x in a:
    if x.startswith("--body-file="):
        rec["body"] = open(x.split("=", 1)[1], encoding="utf-8", newline="").read()
with open(os.environ["FAKE_GH_LOG"], "a", encoding="utf-8") as f:
    f.write(json.dumps(rec) + "\n")
if a[:2] == ["pr", "list"]:
    print(os.environ.get("FAKE_GH_LIST", "0"))
elif a[:2] == ["pr", "view"]:
    print(os.environ.get("FAKE_GH_VIEW", "OPEN\tmain\tclaude/ok\tfalse"))
elif a[:2] == ["pr", "create"]:
    print("https://github.com/o/r/pull/123")
'''

IDENT = {"GIT_AUTHOR_NAME": "warcore", "GIT_AUTHOR_EMAIL": "noreply@users.noreply.github.com",
         "GIT_COMMITTER_NAME": "warcore", "GIT_COMMITTER_EMAIL": "noreply@users.noreply.github.com",
         "GIT_AUTHOR_DATE": "2026-01-01T00:00:00+0000", "GIT_COMMITTER_DATE": "2026-01-01T00:00:00+0000"}

SESSION_LINK = "https://" + "claude" + ".ai/" + "code/session_" + "0123456789abcdef"
EMAIL = "someone" + "@" + "corp-mail.io"
WRITES = (["pr", "create"], ["pr", "edit"])


def write(path, text):
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def git(cwd, *args, extra=None):
    e = dict(os.environ)
    e.update(IDENT)
    e.update(extra or {})
    subprocess.run(["git", "-c", "core.hooksPath=/dev/null", "-c", "init.defaultBranch=main"] + list(args),
                   cwd=cwd, env=e, check=True, capture_output=True)


class OpenPrTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp()
        origin, seed = os.path.join(cls.tmp, "origin.git"), os.path.join(cls.tmp, "seed")
        git(cls.tmp, "init", "-q", "--bare", origin)
        git(cls.tmp, "init", "-q", seed)
        write(os.path.join(seed, "a.txt"), "one\n")
        git(seed, "add", "-A"); git(seed, "commit", "-q", "-m", "Add a")
        git(seed, "branch", "claude/empty")
        git(seed, "checkout", "-q", "-b", "claude/ok")
        write(os.path.join(seed, "b.txt"), "two\n")
        git(seed, "add", "-A"); git(seed, "commit", "-q", "-m", "Add b")
        git(seed, "checkout", "-q", "-b", "wip/bad-ident", "main")
        write(os.path.join(seed, "c.txt"), "three\n")
        git(seed, "add", "-A"); git(seed, "commit", "-q", "-m", "Add c", extra={"GIT_AUTHOR_EMAIL": EMAIL})
        git(seed, "remote", "add", "origin", origin)
        git(seed, "push", "-q", "origin", "main", "claude/ok", "claude/empty", "wip/bad-ident")
        cls.work = os.path.join(cls.tmp, "work")
        git(cls.tmp, "clone", "-q", origin, cls.work)
        cls.bin = os.path.join(cls.tmp, "bin")
        os.mkdir(cls.bin)
        gh = os.path.join(cls.bin, "gh")
        write(gh, "#!" + sys.executable + "\n" + FAKE_GH.split("\n", 1)[1])
        os.chmod(gh, 0o755)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def run_pr(self, step, **inputs):
        log = os.path.join(self.tmp, "gh.log")
        if os.path.exists(log):
            os.remove(log)
        self.summary = os.path.join(self.tmp, "summary.md")
        if os.path.exists(self.summary):
            os.remove(self.summary)
        e = {k: v for k, v in os.environ.items() if not k.startswith(("GITHUB_", "PII_", "GH_"))}
        e.update(PATH=self.bin + os.pathsep + e.get("PATH", ""), FAKE_GH_LOG=log, GUARD=GUARD,
                 PII_TERMS="w:zqxtermzq", GUARD_REQUIRE_TERMS="1", GH_TOKEN="x", GH_REPO="o/r",
                 GITHUB_STEP_SUMMARY=self.summary, **IDENT)
        e.update(MODE="open", BRANCH="claude/ok", PR="", TITLE="Add b", BODY="Adds b.\n", DRY_RUN="false")
        e.update({k.upper(): v for k, v in inputs.items() if not k.startswith("fake_")})
        e.update({k.upper(): v for k, v in inputs.items() if k.startswith("fake_")})
        p = subprocess.run([sys.executable, "-I", SCRIPT, step], cwd=self.work, env=e, capture_output=True, text=True)
        calls = [json.loads(l) for l in read(log).splitlines()] if os.path.exists(log) else []
        return p.returncode, p.stdout + p.stderr, calls

    def both(self, **inputs):
        """check then apply, as the workflow does; stop at the first failure."""
        code, out, calls = self.run_pr("check", **inputs)
        if code != 0:
            return code, out, calls
        code2, out2, calls2 = self.run_pr("apply", **inputs)
        return code2, out + out2, calls + calls2

    def writes(self, calls):
        return [c for c in calls if c["argv"][:2] in WRITES]

    # 1
    def test_valid_branch_opens(self):
        body = "Adds b.\n\n- line two\n"
        code, out, calls = self.both(body=body)
        self.assertEqual(code, 0, out)
        w = self.writes(calls)
        self.assertEqual(len(w), 1)
        argv = w[0]["argv"]
        self.assertEqual(argv[:2], ["pr", "create"])
        self.assertIn("--base=main", argv)
        self.assertIn("--head=claude/ok", argv)
        self.assertIn("--title=Add b", argv)
        self.assertEqual(w[0]["body"], body)
        self.assertIn("opened #123", read(self.summary))

    # 2
    def test_branch_injection_rejected(self):
        for b in ("claude/a b", "claude/a;rm", "claude/$(id)", "claude/`id`", "claude/a\nb", "claude/../x",
                  "claude/a.lock", "claude/.x", "claude/", "claude/a..b", "claude/a@{1}"):
            code, out, calls = self.both(branch=b)
            self.assertEqual(code, 1, b)
            self.assertIn("branch must match", out)
            self.assertEqual(calls, [], b)

    # 3
    def test_branch_prefix_rejected(self):
        for b in ("main", "feature/x", "claudex/y", "Claude/x", "wipx/y"):
            code, out, _ = self.both(branch=b)
            self.assertEqual(code, 1, b)
            self.assertIn("branch must match", out)

    # 4
    def test_title_length(self):
        code, out, _ = self.both(title="x" * 73)
        self.assertEqual(code, 1)
        self.assertIn("title has 73 characters", out)
        code, out, _ = self.both(title="x" * 72)
        self.assertEqual(code, 0, out)

    # 5
    def test_session_link_in_body_rejected(self):
        code, out, calls = self.both(body="See " + SESSION_LINK + "\n")
        self.assertEqual(code, 1)
        self.assertIn("[session-link] pull-request:2", out)
        self.assertNotIn("session_0123", out)
        self.assertEqual(calls, [])

    # 6
    def test_email_in_body_rejected(self):
        code, out, calls = self.both(body="Contact " + EMAIL + "\n")
        self.assertEqual(code, 1)
        self.assertIn("[email] pull-request:2", out)
        self.assertNotIn(EMAIL, out)
        self.assertEqual(calls, [])

    # 7
    def test_dry_run_makes_no_writes(self):
        code, out, calls = self.both(dry_run="true")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.writes(calls), [])
        self.assertIn("dry run", read(self.summary))
        code, out, calls = self.both(mode="edit", branch="", pr="7", dry_run="true")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.writes(calls), [])

    # 8
    def test_edit_without_pr_rejected(self):
        code, out, calls = self.both(mode="edit", branch="", pr="")
        self.assertEqual(code, 1)
        self.assertIn("edit mode needs pr", out)
        self.assertEqual(calls, [])

    # extra cases
    def test_edit_ok(self):
        code, out, calls = self.both(mode="edit", branch="", pr="7", title="New title", body="New body\n")
        self.assertEqual(code, 0, out)
        w = self.writes(calls)
        self.assertEqual([c["argv"][:3] for c in w], [["pr", "edit", "7"]])
        self.assertIn("--title=New title", w[0]["argv"])
        self.assertEqual(w[0]["body"], "New body\n")

    def test_edit_refuses_foreign_or_closed_pr(self):
        for view in ("OPEN\tmain\tfeature/x\tfalse", "CLOSED\tmain\tclaude/ok\tfalse",
                     "OPEN\tmain\tclaude/ok\ttrue", "OPEN\tother\tclaude/ok\tfalse"):
            code, out, calls = self.both(mode="edit", branch="", pr="7", fake_gh_view=view)
            self.assertEqual(code, 1, view)
            self.assertEqual(self.writes(calls), [], view)

    def test_bad_inputs(self):
        cases = [dict(mode="merge"), dict(dry_run="yes"), dict(pr="5"), dict(mode="edit", pr="0", branch=""),
                 dict(mode="edit", pr="5", branch="claude/ok"), dict(mode="edit", pr="5;x", branch=""),
                 dict(title=""), dict(title="a\nb"), dict(title="a\tb"), dict(body="  \n"), dict(body="x" * 60001)]
        for c in cases:
            code, out, calls = self.both(**c)
            self.assertEqual(code, 1, c)
            self.assertEqual(calls, [], c)

    def test_term_in_title_rejected(self):
        code, out, _ = self.both(title="Fix zqxtermzq")
        self.assertEqual(code, 1)
        self.assertIn("[term] pull-request:1", out)

    def test_commit_identity_rejected(self):
        code, out, calls = self.both(branch="wip/bad-ident")
        self.assertEqual(code, 1)
        self.assertIn("identity-email", out)
        self.assertEqual(calls, [])

    def test_branch_without_commits_or_missing(self):
        for b in ("claude/empty", "claude/missing"):
            code, out, calls = self.both(branch=b)
            self.assertEqual(code, 1, b)
            self.assertEqual(calls, [], b)

    def test_already_open_refused(self):
        code, out, calls = self.both(fake_gh_list="1")
        self.assertEqual(code, 1)
        self.assertIn("already exists", out)
        self.assertEqual(self.writes(calls), [])

    def test_missing_token(self):
        code, out, calls = self.both(gh_token="")
        self.assertEqual(code, 1)
        self.assertIn("OPEN_PR_TOKEN", out)
        self.assertEqual(calls, [])

    def test_missing_terms_fails(self):
        code, out, _ = self.both(pii_terms="")
        self.assertEqual(code, 1)

    def test_dash_title_stays_a_value(self):
        code, out, calls = self.both(title="--body-file=/etc/passwd")
        self.assertEqual(code, 0, out)
        argv = self.writes(calls)[0]["argv"]
        self.assertIn("--title=--body-file=/etc/passwd", argv)
        self.assertEqual(sum(a.startswith("--body-file=") for a in argv), 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
