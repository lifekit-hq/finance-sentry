"""Tests for scripts/harness/check_gate_contract.py.

Allowed case: the complete fixture and the real docs/harness/gate-contract.md pass.
Blocked case: a fixture missing one step's model line, and one missing the freeze date, are each rejected by name.

Run: tests/harness/run.sh
"""
import importlib.util
import os
import unittest

REPO = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
spec = importlib.util.spec_from_file_location("check_gate_contract", os.path.join(REPO, "scripts", "harness", "check_gate_contract.py"))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)

COMPLETE = "- Frozen: 2026-11-18\n- no-mistakes version: v1.106.1\n\n" + "".join(
    "| %s | model: sonnet (agent claude, effort high) |\n" % s for s in check.STEPS
)


class GateContract(unittest.TestCase):
    def test_complete_fixture_accepted(self):
        self.assertEqual(check.problems(COMPLETE), [])

    def test_missing_step_model_line_rejected(self):
        text = COMPLETE.replace("| review | model: sonnet (agent claude, effort high) |\n", "")
        self.assertEqual(check.problems(text), ["missing model line for step 'review'"])

    def test_missing_freeze_date_rejected(self):
        text = COMPLETE.replace("- Frozen: 2026-11-18\n", "")
        self.assertEqual(len(check.problems(text)), 1)
        self.assertIn("freeze date", check.problems(text)[0])

    def test_real_document_passes(self):
        with open(os.path.join(REPO, "docs", "harness", "gate-contract.md"), encoding="utf-8") as fh:
            self.assertEqual(check.problems(fh.read()), [])


if __name__ == "__main__":
    unittest.main()
