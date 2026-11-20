#!/usr/bin/env python3
"""Offline tests for plan.py and mutation.yml. Run: python3 -I tools/mutation/ci/test_plan.py

No network, no dotnet; temporary git repositories only.
Set PLAN_PATH to test another copy of plan.py (used by mutate.py).
"""
import importlib.util
import json
import os
import re
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
PLAN_PATH = os.environ.get("PLAN_PATH", os.path.join(HERE, "plan.py"))
WORKFLOW = os.path.join(ROOT, ".github", "workflows", "mutation.yml")
spec = importlib.util.spec_from_file_location("plan", PLAN_PATH)
plan = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plan)

LISTS = "tools/mutation/lists/"


def entries(prefix, n):
    out = []
    for i in range(n):
        out.append("[%s-%02d]\nfile = Assets/X.cs\nfilter = FullyQualifiedName~X\nexpect = X\nnote = n\n"
                   "original:\n| a\nreplacement:\n| b\n" % (prefix, i + 1))
    return "# comment\n\n" + "\n".join(out)


def git(repo, *args):
    env = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")
    r = subprocess.run(["git", "-C", repo, "-c", "user.name=t", "-c", "user.email=", *args],
                       capture_output=True, text=True, env=env)
    if r.returncode != 0:
        raise RuntimeError(r.stderr)
    return r.stdout.strip()


class Repo:
    """A throwaway repository with a base commit; change() makes the head commit."""

    def __init__(self, base_files):
        self.tmp = tempfile.TemporaryDirectory()
        self.path = self.tmp.name
        git(self.path, "init", "-q")
        self.write(base_files)
        git(self.path, "add", "-A")
        git(self.path, "commit", "-q", "--allow-empty", "-m", "base")
        self.base = git(self.path, "rev-parse", "HEAD")

    def write(self, files):
        for rel, text in files.items():
            p = os.path.join(self.path, rel)
            if text is None:
                os.remove(p)
                continue
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)

    def change(self, files=None, renames=()):
        for old, new in renames:
            os.makedirs(os.path.dirname(os.path.join(self.path, new)), exist_ok=True)
            git(self.path, "mv", old, new)
        self.write(files or {})
        git(self.path, "add", "-A")
        git(self.path, "commit", "-q", "--allow-empty", "-m", "head")
        return self

    def run(self, extra=()):
        out = os.path.join(self.path, "..", os.path.basename(self.path) + ".out")
        r = subprocess.run([sys.executable, "-I", PLAN_PATH, "--repo", self.path, "--base", self.base,
                            "--head", "HEAD", "--github-output", out, *extra],
                           capture_output=True, text=True)
        gh = ""
        if os.path.exists(out):
            with open(out, encoding="utf-8") as fh:
                gh = fh.read()
            os.remove(out)
        return r, gh

    def close(self):
        self.tmp.cleanup()


class PlanCase(unittest.TestCase):
    def go(self, base_files, files=None, renames=()):
        repo = Repo(base_files).change(files, renames)
        self.addCleanup(repo.close)
        return repo.run()

    def ok(self, base_files, files=None, renames=()):
        r, gh = self.go(base_files, files, renames)
        self.assertEqual(r.returncode, 0, r.stderr)
        shards = json.loads(r.stdout)["include"]
        outputs = dict(line.split("=", 1) for line in gh.splitlines())
        self.assertEqual(json.loads(outputs["matrix"]), {"include": shards})
        self.assertEqual(outputs["count"], str(len(shards)))
        return shards

    def fails(self, code, base_files, files=None):
        r, gh = self.go(base_files, files)
        self.assertEqual(r.returncode, 2, r.stdout)
        self.assertEqual(r.stderr.splitlines()[0], code, r.stderr)
        self.assertEqual(r.stdout, "")
        self.assertEqual(gh, "")

    def sizes(self, shards):
        return [len(s["ids"].split(",")) for s in shards]


class A1Empty(PlanCase):
    def test_no_list_changed_gives_empty_table_and_exit_0(self):
        shards = self.ok({LISTS + "old.txt": entries("o", 3)}, {"docs/notes.md": "x\n"})
        self.assertEqual(shards, [])

    def test_unchanged_list_is_not_planned(self):
        shards = self.ok({LISTS + "old.txt": entries("o", 3), "Assets/X.cs": "a"}, {"Assets/X.cs": "b"})
        self.assertEqual(shards, [])


class A2Split(PlanCase):
    def test_three_and_ten_entries_give_one_shard_each(self):
        for n in (3, 10):
            with self.subTest(n=n):
                shards = self.ok({}, {LISTS + "x.txt": entries("x", n)})
                self.assertEqual(self.sizes(shards), [n])

    def test_eleven_entries_give_six_and_five(self):
        shards = self.ok({}, {LISTS + "x.txt": entries("x", 11)})
        self.assertEqual(self.sizes(shards), [6, 5])
        self.assertEqual(shards[0]["ids"], ",".join("x-%02d" % i for i in range(1, 7)))
        self.assertEqual(shards[1]["ids"], ",".join("x-%02d" % i for i in range(7, 12)))

    def test_seventy_six_entries_give_eight_shards_in_order(self):
        shards = self.ok({}, {LISTS + "ml.txt": entries("m", 76)})
        self.assertEqual(self.sizes(shards), [10, 10, 10, 10, 9, 9, 9, 9])
        ids = ",".join(s["ids"] for s in shards).split(",")
        self.assertEqual(ids, ["m-%02d" % i for i in range(1, 77)])
        self.assertEqual([s["label"] for s in shards], ["ml %d/8" % i for i in range(1, 9)])
        self.assertTrue(all(s["list"] == LISTS + "ml.txt" for s in shards))

    def test_modified_list_is_planned_from_head_content(self):
        shards = self.ok({LISTS + "x.txt": entries("x", 3)}, {LISTS + "x.txt": entries("y", 12)})
        self.assertEqual(self.sizes(shards), [6, 6])
        self.assertTrue(shards[0]["ids"].startswith("y-01"))

    def test_renamed_and_changed_list_uses_new_path(self):
        shards = self.ok({LISTS + "a.txt": entries("a", 11)}, {LISTS + "b.txt": entries("a", 11) + "\n# more\n"},
                         renames=[(LISTS + "a.txt", LISTS + "b.txt")])
        self.assertEqual({s["list"] for s in shards}, {LISTS + "b.txt"})
        self.assertEqual(self.sizes(shards), [6, 5])


class A3Scope(PlanCase):
    def test_two_lists_are_not_mixed(self):
        shards = self.ok({}, {LISTS + "a.txt": entries("a", 11), LISTS + "b.txt": entries("b", 3)})
        by_list = {}
        for s in shards:
            by_list.setdefault(s["list"], []).append(s["ids"])
        self.assertEqual(sorted(by_list), [LISTS + "a.txt", LISTS + "b.txt"])
        self.assertTrue(all(i.startswith("a-") for ids in by_list[LISTS + "a.txt"] for i in ids.split(",")))
        self.assertEqual(by_list[LISTS + "b.txt"], ["b-01,b-02,b-03"])
        self.assertEqual(self.sizes(shards), [6, 5, 3])

    def test_files_outside_lists_txt_are_ignored(self):
        shards = self.ok({LISTS + "gone.txt": entries("g", 3)}, {
            "tools/mutation/selftest/x.txt": entries("s", 3),
            LISTS + "readme.md": entries("r", 3),
            LISTS + "sub/y.txt": entries("u", 3),
            "other/lists/z.txt": entries("z", 3),
            LISTS + "gone.txt": None,
        })
        self.assertEqual(shards, [])

    def test_pure_rename_is_not_planned(self):
        shards = self.ok({LISTS + "a.txt": entries("a", 3)}, renames=[(LISTS + "a.txt", LISTS + "b.txt")])
        self.assertEqual(shards, [])

    def test_deleted_list_is_ignored(self):
        shards = self.ok({LISTS + "gone.txt": entries("g", 3), "docs/a.md": "a\n"}, {LISTS + "gone.txt": None})
        self.assertEqual(shards, [])


class A4Errors(PlanCase):
    def test_list_with_no_entries(self):
        self.fails("empty-list", {}, {LISTS + "x.txt": "# only a comment\n"})

    def test_duplicate_id(self):
        self.fails("duplicate-id", {}, {LISTS + "x.txt": entries("x", 2) + "\n" + entries("x", 1)})

    def test_bad_id_characters(self):
        for bad in ("a b", "a;b", "a$(x)", "-a", "a,b"):
            with self.subTest(bad=bad):
                self.fails("bad-id", {}, {LISTS + "x.txt": entries("x", 1) + "\n[%s]\n" % bad})

    def test_bad_list_name_characters(self):
        for name in ("a b.txt", "a;b.txt", "a$b.txt"):
            with self.subTest(name=name):
                self.fails("bad-name", {}, {LISTS + name: entries("x", 1)})

    def test_shard_limit(self):
        shards = self.ok({}, {LISTS + "a.txt": entries("a", 120), LISTS + "b.txt": entries("b", 120)})
        self.assertEqual(len(shards), 24)
        self.fails("too-many-shards", {}, {LISTS + "a.txt": entries("a", 120), LISTS + "b.txt": entries("b", 121)})

    def test_bad_revision(self):
        repo = Repo({})
        self.addCleanup(repo.close)
        repo.base = "0" * 40
        r, gh = repo.run()
        self.assertEqual(r.returncode, 2)
        self.assertEqual(r.stderr.splitlines()[0], "git")
        self.assertEqual(gh, "")


class Split(unittest.TestCase):
    def test_split_sizes(self):
        for n, want in ((1, [1]), (10, [10]), (11, [6, 5]), (20, [10, 10]), (21, [7, 7, 7]), (79, [10] * 7 + [9])):
            with self.subTest(n=n):
                self.assertEqual([len(c) for c in plan.split(list(range(n)))], want)


RUN_KEY = re.compile(r"^(\s*)(?:-\s+)?run:\s*(.*)$")


def run_blocks(text):
    """Every run: value of a workflow file (inline or block scalar), by naive indentation."""
    blocks = []
    lines = text.split("\n")
    i = 0
    while i < len(lines):
        m = RUN_KEY.match(lines[i])
        if not m:
            i += 1
            continue
        indent, value = len(m.group(1)), m.group(2)
        body = [value]
        i += 1
        if value in ("|", ">", "|-", ">-"):
            while i < len(lines) and (lines[i].strip() == "" or len(lines[i]) - len(lines[i].lstrip()) > indent):
                body.append(lines[i])
                i += 1
        blocks.append("\n".join(body))
    return blocks


class A6Workflow(unittest.TestCase):
    def setUp(self):
        with open(WORKFLOW, encoding="utf-8") as fh:
            self.text = fh.read()

    def test_no_expression_in_run_lines(self):
        blocks = run_blocks(self.text)
        self.assertGreaterEqual(len(blocks), 4)
        for b in blocks:
            self.assertNotIn("${{", b)

    def test_tools_come_from_the_base_commit(self):
        blocks = "\n".join(run_blocks(self.text))
        self.assertIn("base/tools/mutation/mutate.py", blocks)
        self.assertIn("base/tools/mutation/hygiene.py", blocks)
        self.assertNotIn("pr/tools/mutation/mutate.py", blocks)
        self.assertNotIn("pr/tools/mutation/hygiene.py", blocks)

    def test_repository_variable_can_switch_shards_off(self):
        self.assertIn("MUTATION_CI: ${{ vars.MUTATION_CI }}", self.text)
        plan_block = [b for b in run_blocks(self.text) if "plan.py" in b]
        self.assertEqual(len(plan_block), 1)
        self.assertIn('if [ "${MUTATION_CI:-}" = off ]; then', plan_block[0])
        self.assertIn("count=0", plan_block[0])

    def test_run_block_parser_sees_block_scalars(self):
        self.assertEqual(run_blocks("steps:\n  - run: |\n      a ${{ x }}\n      b\n  - run: c\n"),
                         ["|\n      a ${{ x }}\n      b", "c"])


if __name__ == "__main__":
    unittest.main()
