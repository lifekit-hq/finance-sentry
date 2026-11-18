"""Tests for scripts/harness/attribution.py on a fixture of three fake no-mistakes runs.

Allowed case: the counts below are asserted exactly and a corrupted hook-log line is reported, not fatal.
Blocked case: a missing state file or a malformed embed target fails loudly instead of emitting numbers;
the inputs are byte-identical after a run and test.log is never opened.

Run: tests/harness/run.sh
"""
import builtins
import hashlib
import importlib.util
import json
import os
import sqlite3
import tempfile
import unittest

REPO = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SCRIPT = os.path.join(REPO, "scripts", "harness", "attribution.py")
spec = importlib.util.spec_from_file_location("attribution", SCRIPT)
attribution = importlib.util.module_from_spec(spec)
spec.loader.exec_module(attribution)

SCHEMA = """
CREATE TABLE repos (id TEXT PRIMARY KEY, working_path TEXT, upstream_url TEXT);
CREATE TABLE runs (id TEXT PRIMARY KEY, repo_id TEXT, branch TEXT, status TEXT, created_at INTEGER);
CREATE TABLE step_results (id TEXT PRIMARY KEY, run_id TEXT, step_name TEXT);
CREATE TABLE step_rounds (id TEXT PRIMARY KEY, step_result_id TEXT, round INTEGER, findings_json TEXT,
                          selected_finding_ids TEXT);
"""


def finding(fid, action, desc, severity="warning", file="a.cs"):
    return {"id": fid, "severity": severity, "file": file, "action": action, "description": desc}


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.state = os.path.join(self.tmp.name, "nm")
        os.makedirs(os.path.join(self.state, "logs"))
        self.db = os.path.join(self.state, "state.sqlite")
        c = sqlite3.connect(self.db)
        c.executescript(SCHEMA)
        c.execute("INSERT INTO repos VALUES ('r1','/w/finance-sentry','https://github.com/o/finance-sentry.git')")
        c.execute("INSERT INTO repos VALUES ('r2','/w/other','https://github.com/o/other.git')")
        c.commit()
        c.close()
        # run A: two review rounds, f1 fixed (repeats in round 2), f2 is a reviewer no-op note
        self.add_run("A", 100, [(1, [finding("f1", "auto-fix", "Poisoned tracker loses the write"),
                                 finding("f2", "no-op", "Tradeoff note", "info")], ["f1"]),
                            (2, [finding("f1", "auto-fix", "Poisoned tracker loses the write")], [])],
                 rebase="conflicts detected, asking agent to resolve...\nconflicts detected, asking agent to resolve...\n",
                 push="pushed successfully\n", review="user-fix round starting after round 1 (1 findings selected)\n")
        # run B: one round; f3 leaves a choice the brief did not make, f4 cites a linked-only doc
        self.add_run("B", 200, [(1, [finding("f3", "ask-user", "Header ignores the intent for the verdict"),
                                 finding("f4", "auto-fix", "Stale prose in money-semantics.md after the change")],
                             ["f3", "f4"])], push="pushed successfully\n")
        # run C: no review finding on the code, one info docs finding that nobody selected
        self.add_run("C", 300, [(1, [finding("f5", "auto-fix", "Stale doc paragraph", "info")], [])])
        # a run in another repo must never count
        self.add_run("X", 400, [(1, [finding("x1", "auto-fix", "Other repo bug")], ["x1"])], repo="r2")
        self.hooks = os.path.join(self.tmp.name, "hooks.log.jsonl")
        with open(self.hooks, "w") as fh:
            fh.write('{"ts":"t","hook":"PreToolUse","file":"m","decision":"block:migrations"}\n')
            fh.write('{"ts":"t","hook":"PreToolUse","file":"x","decision":"allow"}\n')
            fh.write("{this is not json\n")
            fh.write('{"ts":"t","hook":"Stop","file":"frontend","decision":"block:red","seconds":9.5}\n')
            fh.write('{"ts":"t","hook":"Stop","file":"-","decision":"skip:stop-hook-active"}\n')

    def add_run(self, rid, ts, rounds, repo="r1", rebase="", push="", review=""):
        c = sqlite3.connect(self.db)
        c.execute("INSERT INTO runs VALUES (?,?,?,?,?)", (rid, repo, "fm/" + rid, "completed", ts))
        c.execute("INSERT INTO step_results VALUES (?,?,?)", ("sr" + rid, rid, "review"))
        for rnd, findings, selected in rounds:
            c.execute("INSERT INTO step_rounds VALUES (?,?,?,?,?)",
                      ("%s-%d" % (rid, rnd), "sr" + rid, rnd, json.dumps({"findings": findings}), json.dumps(selected)))
        c.commit()
        c.close()
        logdir = os.path.join(self.state, "logs", rid)
        os.makedirs(logdir)
        for name, body in (("rebase.log", rebase), ("push.log", push), ("review.log", review),
                           ("test.log", "SECRET-IN-TEST-LOG\n")):
            with open(os.path.join(logdir, name), "w") as fh:
                fh.write(body)

    def summary(self, **kw):
        args = type("A", (), dict(window=3, since_ts=None, repo_match="finance-sentry",
                                  state_dir=self.state, hooks_log=[self.hooks]))()
        for k, v in kw.items():
            setattr(args, k, v)
        return attribution.build(args)

    def digest(self):
        h = hashlib.sha256()
        for root, _, files in sorted(os.walk(self.state)):
            for f in sorted(files):
                with open(os.path.join(root, f), "rb") as fh:
                    h.update(fh.read())
        with open(self.hooks, "rb") as fh:
            h.update(fh.read())
        return h.hexdigest()


class AllowedCases(Fixture):
    def test_counts_on_three_fake_runs(self):
        s = self.summary()
        self.assertEqual(s["window"]["runs"], 3)
        self.assertEqual([r["run"] for r in s["runs"]], ["A", "B", "C"])
        self.assertEqual(s["findings_total"], 5)
        self.assertEqual(s["findings_by_owner"],
                         {"disposition": 1, "gate_error": 2, "decomposition": 1, "harness_load": 1})
        rc = s["recurrence_by_class"]
        self.assertEqual((rc["correctness"]["findings"], rc["correctness"]["runs"], rc["correctness"]["recurring"]),
                         (2, 1, False))
        self.assertEqual((rc["docs"]["findings"], rc["docs"]["runs"], rc["docs"]["recurring"]), (2, 2, True))
        self.assertEqual((rc["scope"]["findings"], rc["scope"]["runs"]), (1, 1))
        self.assertEqual(s["review_rounds"]["per_run"], {"A": 2, "B": 1, "C": 1})
        self.assertEqual(s["review_rounds"]["max"], 2)
        self.assertEqual(s["review_rounds"]["mean"], 1.33)
        self.assertEqual(s["rebase_conflicts"], 2)
        self.assertEqual(s["pushes"], 2)
        self.assertEqual({r["run"]: r["fix_rounds"] for r in s["runs"]}, {"A": 1, "B": 0, "C": 0})

    def test_corrupted_hook_line_is_reported_not_fatal(self):
        s = self.summary()
        self.assertEqual(s["hooks"]["corrupt_lines"], 1)
        self.assertTrue(any(w["problem"] == "corrupt line skipped" and w["line"] == 3 for w in s["warnings"]))
        self.assertEqual(s["hooks"]["per_hook"]["PreToolUse"]["fires"], 2)
        self.assertEqual(s["hooks"]["per_hook"]["PreToolUse"]["blocks"], 1)
        self.assertEqual(s["hooks"]["per_hook"]["Stop"], {"fires": 2, "blocks": 1,
                                                          "decisions": {"block": 1, "skip": 1}})
        self.assertEqual((s["hooks"]["total_fires"], s["hooks"]["total_blocks"]), (4, 2))

    def test_window_takes_the_latest_n_and_since_takes_the_first_n(self):
        self.assertEqual([r["run"] for r in self.summary(window=2)["runs"]], ["B", "C"])
        self.assertEqual([r["run"] for r in self.summary(window=2, since_ts=200)["runs"]], ["B", "C"])
        self.assertEqual([r["run"] for r in self.summary(window=1, since_ts=101)["runs"]], ["B"])

    def test_missing_hook_log_is_empty_not_fatal(self):
        s = self.summary(hooks_log=[os.path.join(self.tmp.name, "absent.jsonl")])
        self.assertEqual(s["hooks"]["total_fires"], 0)


class BlockedCases(Fixture):
    def test_inputs_are_read_only_and_test_log_is_never_opened(self):
        before = self.digest()
        opened = []
        real_open = builtins.open

        def spy(path, *a, **k):
            opened.append(str(path))
            return real_open(path, *a, **k)

        builtins.open = spy
        try:
            self.summary()
        finally:
            builtins.open = real_open
        self.assertFalse([p for p in opened if p.endswith("test.log")])
        self.assertEqual(before, self.digest())

    def test_database_is_opened_read_only(self):
        conn = attribution.open_ro(self.state)
        with self.assertRaises(sqlite3.OperationalError):
            conn.execute("DELETE FROM runs")
        conn.close()

    def test_no_finding_text_in_output(self):
        self.assertNotIn("Poisoned tracker", json.dumps(self.summary()))

    def test_missing_state_fails_loudly(self):
        with self.assertRaises(sqlite3.OperationalError):
            self.summary(state_dir=os.path.join(self.tmp.name, "nowhere"))

    def test_embed_target_without_block_exits_2(self):
        page = os.path.join(self.tmp.name, "p.html")
        with open(page, "w") as fh:
            fh.write("<html></html>")
        rc = attribution.main(["--window", "3", "--state-dir", self.state, "--embed-into", page,
                               "--out", os.path.join(self.tmp.name, "o.json")])
        self.assertEqual(rc, 2)

    def test_embed_replaces_the_block_in_place(self):
        page = os.path.join(self.tmp.name, "p.html")
        with open(page, "w") as fh:
            fh.write('<p>x</p><script type="application/json" id="summary">{}</script><p>y</p>')
        rc = attribution.main(["--window", "3", "--state-dir", self.state, "--embed-into", page,
                               "--out", os.path.join(self.tmp.name, "o.json")])
        self.assertEqual(rc, 0)
        with open(page) as fh:
            body = fh.read()
        self.assertTrue(body.startswith("<p>x</p>") and body.endswith("<p>y</p>"))
        start = body.index('id="summary">') + len('id="summary">')
        self.assertEqual(json.loads(body[start:body.index("</script>")])["window"]["runs"], 3)


if __name__ == "__main__":
    unittest.main()
