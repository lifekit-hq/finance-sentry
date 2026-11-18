"""Both-direction tests for the Claude Code hooks: one blocked and one allowed case per hook.

Run: tests/harness/run.sh
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

REPO = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
HOOKS = os.path.join(REPO, ".claude", "hooks")


class HookCase(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.logfile = os.path.join(self.tmp.name, "hooks.log.jsonl")

    def stub(self, name, body):
        path = os.path.join(self.tmp.name, name)
        with open(path, "w") as fh:
            fh.write("#!/bin/sh\n" + body + "\n")
        os.chmod(path, 0o755)
        return path

    def run_hook(self, script, tool, rel_path, **env):
        event = {"tool_name": tool, "cwd": REPO, "tool_input": {"file_path": os.path.join(REPO, rel_path)}}
        e = dict(os.environ, FS_HOOK_LOG=self.logfile, **env)
        return subprocess.run([sys.executable, os.path.join(HOOKS, script)], input=json.dumps(event),
                              capture_output=True, text=True, env=e)

    def last_log(self):
        with open(self.logfile) as fh:
            return json.loads(fh.read().strip().splitlines()[-1])


class PreToolUseTests(HookCase):
    def test_blocked_migration_write(self):
        p = "backend/src/FinanceSentry.Modules.Ledger/Infrastructure/Migrations/M001_Init.cs"
        r = self.run_hook("pre_tool_use.py", "Write", p)
        self.assertEqual(r.returncode, 2)
        self.assertEqual(len(r.stderr.strip().splitlines()), 1)
        self.assertIn("migrations", r.stderr)
        self.assertIn("issue", r.stderr)
        entry = self.last_log()
        self.assertEqual((entry["hook"], entry["file"], entry["decision"]), ("PreToolUse", p, "block:migrations"))
        self.assertIn("ts", entry)

    def test_blocked_protected_configs(self):
        for p in ("frontend/eslint.config.mjs", "frontend/.prettierrc.json", "backend/.editorconfig",
                  "backend/Directory.Build.props", "frontend/e2e/screens/x.png"):
            with self.subTest(p):
                self.assertEqual(self.run_hook("pre_tool_use.py", "Edit", p).returncode, 2)

    def test_allowed_ordinary_edit(self):
        p = "frontend/src/app/app.ts"
        r = self.run_hook("pre_tool_use.py", "Edit", p)
        self.assertEqual((r.returncode, r.stderr), (0, ""))
        self.assertEqual(self.last_log()["decision"], "allow")

    def test_allowed_non_edit_tool(self):
        self.assertEqual(self.run_hook("pre_tool_use.py", "Read", "backend/.editorconfig").returncode, 0)


class PostToolUseTests(HookCase):
    def test_violations_returned_to_model(self):
        stub = self.stub("eslint", 'echo "1:1 error no-console"; exit 1')
        r = self.run_hook("post_tool_use.py", "Edit", "frontend/src/app/app.ts", FS_HOOK_ESLINT=stub)
        self.assertEqual(r.returncode, 0)
        ctx = json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]
        self.assertIn("no-console", ctx)
        self.assertEqual(self.last_log()["decision"], "violations")

    def test_clean_file_adds_no_context(self):
        stub = self.stub("eslint", "exit 0")
        r = self.run_hook("post_tool_use.py", "Write", "frontend/src/app/app.ts", FS_HOOK_ESLINT=stub)
        self.assertEqual((r.returncode, r.stdout), (0, ""))
        self.assertEqual(self.last_log()["decision"], "fixed-clean")

    def test_eslint_gets_only_the_touched_file(self):
        stub = self.stub("eslint", 'echo "$@" > "$0.args"')
        self.run_hook("post_tool_use.py", "Edit", "frontend/src/app/app.ts", FS_HOOK_ESLINT=stub)
        with open(stub + ".args") as fh:
            self.assertEqual(fh.read().split(), ["--fix", "src/app/app.ts"])

    def test_eslint_crash_is_a_tool_failure_not_violations(self):
        stub = self.stub("eslint", 'echo "Oops! Something went wrong"; exit 2')
        r = self.run_hook("post_tool_use.py", "Edit", "frontend/src/app/app.ts", FS_HOOK_ESLINT=stub)
        self.assertEqual((r.returncode, r.stdout), (0, ""))
        self.assertIn("warning", r.stderr)
        self.assertEqual(self.last_log()["decision"], "warn:tool-failed")

    def test_dotnet_format_scoped_to_project_and_file(self):
        stub = self.stub("dotnet", 'echo "$@" >> "$0.args"')
        p = "backend/src/FinanceSentry.Core/Utils/AppUrl.cs"
        r = self.run_hook("post_tool_use.py", "Edit", p, FS_HOOK_DOTNET=stub)
        self.assertEqual((r.returncode, r.stdout), (0, ""))
        with open(stub + ".args") as fh:
            calls = [line.split() for line in fh.read().splitlines()]
        self.assertEqual(len(calls), 2)
        for args in calls:
            self.assertEqual(args[0], "format")
            self.assertTrue(args[1].endswith("FinanceSentry.Core.csproj"))
            self.assertEqual(args[-2:], ["--include", os.path.join(REPO, p)])
        self.assertNotIn("--verify-no-changes", calls[0])
        self.assertIn("--verify-no-changes", calls[1])
        self.assertEqual(self.last_log()["decision"], "fixed-clean")

    def test_dotnet_remaining_violations_returned_to_model(self):
        stub = self.stub("dotnet", 'case "$*" in *--verify-no-changes*) echo "IDE0005 unnecessary using"; exit 2;; esac')
        p = "backend/src/FinanceSentry.Core/Utils/AppUrl.cs"
        r = self.run_hook("post_tool_use.py", "Edit", p, FS_HOOK_DOTNET=stub)
        self.assertEqual(r.returncode, 0)
        ctx = json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]
        self.assertIn("IDE0005", ctx)
        self.assertEqual(self.last_log()["decision"], "violations")

    def test_dotnet_load_failure_is_a_tool_failure_not_violations(self):
        stub = self.stub("dotnet", 'echo "could not load workspace"; exit 4')
        p = "backend/src/FinanceSentry.Core/Utils/AppUrl.cs"
        r = self.run_hook("post_tool_use.py", "Edit", p, FS_HOOK_DOTNET=stub)
        self.assertEqual((r.returncode, r.stdout), (0, ""))
        self.assertEqual(self.last_log()["decision"], "warn:tool-failed")

    def test_missing_tool_fails_open_with_warning(self):
        r = self.run_hook("post_tool_use.py", "Edit", "frontend/src/app/app.ts", FS_HOOK_ESLINT="/nonexistent/eslint")
        self.assertEqual(r.returncode, 0)
        self.assertIn("warning", r.stderr)
        self.assertTrue(self.last_log()["decision"].startswith("warn:tool-missing"))

    def test_other_files_untouched(self):
        r = self.run_hook("post_tool_use.py", "Edit", "docs/money-semantics.md")
        self.assertEqual((r.returncode, r.stdout), (0, ""))
        self.assertEqual(self.last_log()["decision"], "skip")


class StopHookTests(HookCase):
    """Stop hook against a throwaway git repo whose origin/main is the first commit."""

    def setUp(self):
        super().setUp()
        self.repo = os.path.join(self.tmp.name, "repo")
        os.makedirs(self.repo)
        self.git("init", "-q", "-b", "main")
        self.write("README.md", "x")
        self.git("add", "-A")
        self.git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "base")
        self.git("update-ref", "refs/remotes/origin/main", "HEAD")

    def git(self, *args):
        subprocess.run(["git", "-C", self.repo, *args], check=True, capture_output=True)

    def write(self, rel, text):
        path = os.path.join(self.repo, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as fh:
            fh.write(text)

    def run_stop(self, event=None, backend="exit 0", frontend="exit 0"):
        marks = os.path.join(self.tmp.name, "ran")
        if os.path.exists(marks):
            os.remove(marks)
        env = dict(os.environ, FS_HOOK_LOG=self.logfile, FS_STOP_ROOT=self.repo,
                   FS_STOP_BACKEND_CMD=f'echo backend >> "{marks}"; {backend}',
                   FS_STOP_FRONTEND_CMD=f'echo frontend >> "{marks}"; {frontend}')
        r = subprocess.run([sys.executable, os.path.join(HOOKS, "stop_check.py")], input=json.dumps(event or {}),
                           capture_output=True, text=True, env=env)
        ran = open(marks).read().split() if os.path.exists(marks) else []
        return r, ran

    def test_blocked_red_backend_refuses_with_error_lines(self):
        self.write("backend/src/A.cs", "broken")
        out = "\n".join(f"noise {i}" for i in range(50)) + "\nA.cs(1,1): error CS1002: ; expected"
        r, ran = self.run_stop(backend=f'cat <<EOF\n{out}\nEOF\nexit 1')
        self.assertEqual(r.returncode, 2)
        self.assertIn("error CS1002", r.stderr)
        self.assertLessEqual(len(r.stderr.splitlines()), 22)
        self.assertEqual(ran, ["backend"])
        entry = self.last_log()
        self.assertEqual((entry["hook"], entry["file"], entry["decision"]), ("Stop", "backend", "block:red"))

    def test_allowed_green_exits_zero(self):
        self.write("backend/src/A.cs", "ok")
        r, ran = self.run_stop()
        self.assertEqual((r.returncode, r.stderr), (0, ""))
        self.assertEqual(ran, ["backend"])
        self.assertEqual(self.last_log()["decision"], "allow:green")

    def test_stop_hook_active_cannot_loop(self):
        self.write("backend/src/A.cs", "broken")
        r, ran = self.run_stop(event={"stop_hook_active": True}, backend="exit 1")
        self.assertEqual((r.returncode, ran), (0, []))
        self.assertEqual(self.last_log()["decision"], "skip:stop-hook-active")

    def test_split_matches_no_mistakes_test_command(self):
        cases = [("backend/x.cs", ["backend"]), ("Directory.Build.props", ["backend"]),
                 ("frontend/src/a.ts", ["frontend"]), ("scripts/x.sh", ["backend", "frontend"]),
                 ("docs/a.md", []), ("notes.md", [])]
        for path, expected in cases:
            with self.subTest(path):
                self.git("clean", "-fdq")
                self.write(path, "x")
                r, ran = self.run_stop()
                self.assertEqual((r.returncode, ran), (0, expected))

    def test_empty_diff_runs_nothing(self):
        r, ran = self.run_stop()
        self.assertEqual((r.returncode, ran), (0, []))
        self.assertEqual(self.last_log()["decision"], "skip:no-code-change")

    def test_hook_never_modifies_files(self):
        self.write("backend/src/A.cs", "x")
        before = subprocess.run(["git", "-C", self.repo, "status", "--porcelain"], capture_output=True, text=True).stdout
        self.run_stop(backend="exit 1")
        after = subprocess.run(["git", "-C", self.repo, "status", "--porcelain"], capture_output=True, text=True).stdout
        self.assertEqual(before, after)

    def test_one_log_line_per_fire(self):
        self.write("backend/src/A.cs", "x")
        self.run_stop()
        self.run_stop()
        with open(self.logfile) as fh:
            self.assertEqual(len(fh.read().splitlines()), 2)


if __name__ == "__main__":
    unittest.main()
