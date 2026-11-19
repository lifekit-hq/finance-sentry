"""Tests for scripts/harness/check_constitution.py (the "automatic block" wording in the constitution).

Allowed case: the real constitution and table agree; a fixture rule that names its check may keep the wording.
Blocked case: a fixture rule the table marks as having no check but that still says "automatic block" is rejected,
as are a check that is not named, a bullet with no row and a stray mention outside a tagged rule.

Run: tests/harness/run.sh
"""
import importlib.util
import os
import unittest

REPO = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
spec = importlib.util.spec_from_file_location("check_constitution", os.path.join(REPO, "scripts", "harness", "check_constitution.py"))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)

TABLE = """
| ID | Rule | Check today | Decision |
| --- | --- | --- | --- |
| CC1 | Lint | `Lint code` in CI | keep as is |
| CC2 | Tests | none | rewrite as guidance |
"""


def constitution(cc1, cc2):
    return f"### Code Review\n\n- CC1 {cc1}\n- CC2 {cc2}\n\n### Next\n"


class Constitution(unittest.TestCase):
    def test_real_constitution_passes(self):
        with open(os.path.join(REPO, ".specify", "memory", "constitution.md"), encoding="utf-8") as fh:
            text = fh.read()
        with open(os.path.join(REPO, "docs", "harness", "constitution-checks.md"), encoding="utf-8") as fh:
            table = fh.read()
        self.assertEqual(len(check.parse_table(table)), 12)
        self.assertEqual(check.problems(text, table), [])

    def test_fixture_accepted(self):
        text = constitution("Linters pass (`Lint code`). automatic block", "Tests ship with logic. Guidance.")
        self.assertEqual(check.problems(text, TABLE), [])

    def test_automatic_block_on_rule_without_check_rejected(self):
        text = constitution("Linters pass (`Lint code`).", "Missing tests → automatic block")
        self.assertEqual(check.problems(text, TABLE), ["CC2 says 'automatic block' but the table marks it as having no check"])

    def test_wrapped_bullet_still_rejected(self):
        text = constitution("Linters pass (`Lint code`).", "Missing tests →\n  automatic\n  block")
        self.assertEqual(len(check.problems(text, TABLE)), 1)

    def test_check_not_named_rejected(self):
        text = constitution("Linters pass.", "Guidance.")
        self.assertEqual(check.problems(text, TABLE), ["CC1 does not name its check 'Lint code'"])

    def test_bullet_without_row_rejected(self):
        text = constitution("(`Lint code`)", "Guidance.") + "- CC3 Extra rule.\n"
        self.assertEqual(check.problems(text, TABLE), ["CC3 has no row in the table"])

    def test_stray_mention_rejected(self):
        text = constitution("(`Lint code`)", "Guidance.") + "\nSomething else → automatic block\n"
        self.assertEqual(check.problems(text, TABLE), ["1 'automatic block' mention(s) outside a tagged rule"])


if __name__ == "__main__":
    unittest.main()
