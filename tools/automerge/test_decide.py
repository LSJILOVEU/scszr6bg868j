#!/usr/bin/env python3
"""Offline tests for decide.py. Run: python3 -I tools/automerge/test_decide.py

Set DECIDE_PATH to test another copy of decide.py (used by mutate.py).
"""
import copy
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
DECIDE_PATH = os.environ.get("DECIDE_PATH", os.path.join(HERE, "decide.py"))
spec = importlib.util.spec_from_file_location("decide", DECIDE_PATH)
decide = importlib.util.module_from_spec(spec)
spec.loader.exec_module(decide)

SHA = "a" * 40
OTHER_SHA = "b" * 40


def base_pr():
    return {
        "number": 7, "title": "Add a thing", "state": "OPEN", "isDraft": False,
        "baseRefName": "main", "headRefOid": SHA,
        "labels": [{"name": "reviewed"}],
    }


def base_files():
    return [{"filename": "docs/notes.md"}, {"filename": "Assets/Sim/A.cs", "previous_filename": "Assets/Sim/B.cs"}]


def base_checks():
    names = ["test (ubuntu-latest)", "test (windows-latest)", "guard", "gate"]
    return [{"id": i + 1, "name": n, "status": "completed", "conclusion": "success"} for i, n in enumerate(names)]


def run(pr=None, files=None, checks=None, sha=SHA):
    files = base_files() if files is None else files
    pr = pr or base_pr()
    pr.setdefault("changedFiles", len(files))
    return decide.decide(pr, files, base_checks() if checks is None else checks, sha)


class Decide(unittest.TestCase):
    # 1
    def test_all_conditions_met_merges(self):
        self.assertEqual(run()[0], decide.MERGE)

    # 2
    def test_missing_reviewed_waits(self):
        pr = base_pr(); pr["labels"] = [{"name": "docs"}]
        self.assertEqual(run(pr=pr)[0], decide.WAIT)

    # 3
    def test_hold_waits(self):
        pr = base_pr(); pr["labels"].append({"name": "hold"})
        self.assertEqual(run(pr=pr)[0], decide.WAIT)

    # 4
    def test_any_required_check_not_success_waits(self):
        for name in decide.REQUIRED_CHECKS:
            for bad in ("failure", "skipped", "cancelled", None):
                checks = base_checks()
                for c in checks:
                    if c["name"] == name:
                        if bad is None:
                            c["status"], c["conclusion"] = "in_progress", None
                        else:
                            c["conclusion"] = bad
                with self.subTest(name=name, bad=bad):
                    self.assertEqual(run(checks=checks)[0], decide.WAIT)
            missing = [c for c in base_checks() if c["name"] != name]
            with self.subTest(name=name, bad="missing"):
                self.assertEqual(run(checks=missing)[0], decide.WAIT)

    # 5
    def test_extra_check_failure_waits(self):
        checks = base_checks() + [{"id": 50, "name": "unity-compat", "status": "completed", "conclusion": "failure"}]
        self.assertEqual(run(checks=checks)[0], decide.WAIT)
        checks[-1].update(status="queued", conclusion=None)
        self.assertEqual(run(checks=checks)[0], decide.WAIT)

    # 6
    def test_extra_check_skipped_merges(self):
        checks = base_checks() + [{"id": 50, "name": "unity-compat", "status": "completed", "conclusion": "skipped"}]
        self.assertEqual(run(checks=checks)[0], decide.MERGE)

    # 7
    def test_head_moved_waits(self):
        self.assertEqual(run(sha=OTHER_SHA)[0], decide.WAIT)

    # 8
    def test_protected_paths_need_human(self):
        for path in (".github/workflows/x.yml", "tools/pii-guard/pii_guard.py", "CLAUDE.md",
                     ".claude/settings.json", "tools/automerge/decide.py"):
            with self.subTest(path=path):
                code, why = run(files=base_files() + [{"filename": path}])
                self.assertEqual(code, decide.MANUAL)
                self.assertIn("needs a human merge", why)

    # 9
    def test_renamed_from_protected_path_needs_human(self):
        files = base_files() + [{"filename": "docs/old-ci.yml", "previous_filename": ".github/workflows/old.yml"}]
        self.assertEqual(run(files=files)[0], decide.MANUAL)

    # 10
    def test_draft_waits(self):
        pr = base_pr(); pr["isDraft"] = True
        self.assertEqual(run(pr=pr)[0], decide.WAIT)

    # 11
    def test_base_not_main_waits(self):
        pr = base_pr(); pr["baseRefName"] = "m0-sim-core"
        self.assertEqual(run(pr=pr)[0], decide.WAIT)

    # Further cases beyond the eleven required ones.
    def test_closed_or_merged_waits(self):
        for state in ("CLOSED", "MERGED"):
            pr = base_pr(); pr["state"] = state
            self.assertEqual(run(pr=pr)[0], decide.WAIT)

    def test_latest_rerun_counts(self):
        checks = base_checks()
        checks.insert(0, {"id": 0, "name": "gate", "status": "completed", "conclusion": "failure"})
        self.assertEqual(run(checks=checks)[0], decide.MERGE)
        checks.append({"id": 99, "name": "gate", "status": "completed", "conclusion": "failure"})
        self.assertEqual(run(checks=checks)[0], decide.WAIT)

    def test_own_running_check_is_ignored(self):
        checks = base_checks() + [{"id": 60, "name": "automerge", "status": "in_progress", "conclusion": None}]
        self.assertEqual(run(checks=checks)[0], decide.MERGE)

    def test_paths_that_only_look_protected_merge(self):
        files = [{"filename": "docs/CLAUDE.md.bak"}, {"filename": "docs/.github-notes.md"},
                 {"filename": "tools/pii-guardian.txt"}, {"filename": "docs/.claude-notes.md"}]
        self.assertEqual(run(files=files)[0], decide.MERGE)

    def test_agent_rule_files_in_subdirectories_need_human(self):
        for path in ("Assets/CLAUDE.md", "docs/a/claude.md", "tools/x/CLAUDE.local.md", "Claude.MD"):
            with self.subTest(path=path):
                self.assertEqual(run(files=base_files() + [{"filename": path}])[0], decide.MANUAL)

    def test_dot_claude_at_any_depth_needs_human(self):
        for path in ("tools/x/.claude/settings.json", "Assets/.Claude/commands/a.md"):
            with self.subTest(path=path):
                self.assertEqual(run(files=base_files() + [{"filename": path}])[0], decide.MANUAL)

    def test_truncated_file_list_needs_human(self):
        pr = base_pr(); pr["changedFiles"] = 3001
        code, why = run(pr=pr)
        self.assertEqual(code, decide.MANUAL)
        self.assertIn("needs a human merge", why)
        pr = base_pr(); pr.pop("changedFiles", None); pr["changedFiles"] = None
        self.assertEqual(run(pr=pr)[0], decide.MANUAL)

    def test_cli_exit_codes(self):
        with tempfile.TemporaryDirectory() as d:
            paths = {}
            pr = base_pr(); pr["changedFiles"] = len(base_files())
            for key, val in (("pr", pr), ("files", base_files()), ("checks", base_checks())):
                paths[key] = os.path.join(d, key + ".json")
                with open(paths[key], "w", encoding="utf-8") as fh:
                    json.dump(val, fh)
            cmd = [sys.executable, "-I", DECIDE_PATH, "--pr", paths["pr"], "--files", paths["files"],
                   "--checks", paths["checks"], "--sha"]
            self.assertEqual(subprocess.run(cmd + [SHA], capture_output=True).returncode, 0)
            self.assertEqual(subprocess.run(cmd + [OTHER_SHA], capture_output=True).returncode, 10)


if __name__ == "__main__":
    unittest.main(verbosity=2)
