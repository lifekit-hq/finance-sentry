"""Tests for scripts/harness/check_lanes.py (the lane table in docs/harness/gate-contract.md).

Allowed case: the real table passes, every glob matches a tracked file and no file is in two lanes.
Blocked case: a glob matching nothing, a file in two lanes, and a table with no light lane are each rejected.

Run: tests/harness/run.sh
"""
import importlib.util
import os
import unittest

REPO = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
spec = importlib.util.spec_from_file_location("check_lanes", os.path.join(REPO, "scripts", "harness", "check_lanes.py"))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)

FILES = ["docs/a.md", "docs/money.md", "backend/x/Migrations/1.cs"]


class Lanes(unittest.TestCase):
    def test_real_table_passes(self):
        with open(os.path.join(REPO, "docs", "harness", "gate-contract.md"), encoding="utf-8") as fh:
            rows = check.parse(fh.read())
        self.assertGreater(len(rows), 10)
        self.assertEqual(check.problems(rows, check.tracked()), [])

    def test_fixture_accepted(self):
        rows = [("full", "docs/money.md"), ("full", "backend/*/Migrations/**"), ("light", "docs/a.md")]
        self.assertEqual(check.problems(rows, FILES), [])

    def test_glob_matching_nothing_rejected(self):
        rows = [("full", "docs/money.md"), ("light", "docs/none/**")]
        self.assertEqual(check.problems(rows, FILES), ["glob 'docs/none/**' (light) matches no file"])

    def test_path_in_two_lanes_rejected(self):
        rows = [("full", "docs/money.md"), ("light", "docs/**")]
        self.assertEqual(check.problems(rows, FILES), ["path 'docs/money.md' is in two lanes"])

    def test_missing_light_lane_rejected(self):
        self.assertEqual(check.problems([("full", "docs/a.md")], FILES), ["both lanes 'full' and 'light' need at least one row"])

    def test_star_does_not_cross_directories(self):
        self.assertFalse(check.regex("docs/*.md").match("docs/x/y.md"))
        self.assertTrue(check.regex("docs/**").match("docs/x/y.md"))


if __name__ == "__main__":
    unittest.main()
