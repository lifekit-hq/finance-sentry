#!/usr/bin/env python3
"""Outer-loop attribution for the repo harness: one JSON summary over a window of no-mistakes runs.

Reads, never writes (the only write is the optional --out / --embed-into target):
  <state-dir>/state.sqlite          opened read-only (SQLite URI mode=ro)
  <state-dir>/logs/<run>/review.log, push.log, rebase.log     (test.log is never opened)
  .claude/hooks.log.jsonl           one or more files via --hooks-log

No model call, no network, no secrets: only counts, run ids, branch names and finding ids leave
this script; finding text is used for classification and is not emitted.

Owner classes are the 2026-11-16 taxonomy (disposition, gate_error, decomposition, harness_load).
The assignment is mechanical, by the rules in classify_owner(); it is a first cut for a human to
read, not a verdict. Metric of record is recurrence per defect class and rounds per run, never the
raw findings count.

Usage:
  attribution.py --window 20 [--since-ts EPOCH] [--repo-match finance-sentry]
                 [--state-dir ~/.no-mistakes] [--hooks-log PATH ...]
                 [--out summary.json] [--embed-into docs/harness/scorecard.html]
"""
import argparse
import json
import os
import re
import sqlite3
import sys
from collections import Counter, defaultdict

SCHEMA_VERSION = 1
OWNERS = ("disposition", "gate_error", "decomposition", "harness_load")
CLASSES = ("correctness", "scope", "docs", "style", "missing-test", "perf", "rule-violation")
LINKED_ONLY_DOCS = ("money-semantics", "finance-framework", "app-state.md", "qa.md", "ai-pipeline")
EMBED_RE = re.compile(
    r'(<script type="application/json" id="summary">).*?(</script>)', re.S)

CLASS_RULES = (  # first match wins; order matters
    ("rule-violation", re.compile(r"file organi[sz]ation|AGENTS\.md|constitution|\brule\b.*\bviolat", re.I)),
    ("missing-test", re.compile(r"\b(e2e|unit test|test coverage|no test|assert|flaky|spec file)\b", re.I)),
    ("docs", re.compile(r"\b(docs?|documentation|readme|stale|comment|jsdoc|\.md)\b", re.I)),
    ("perf", re.compile(r"\b(perf|performance|latency|uncached|n\+1|slow)\b", re.I)),
    ("scope", re.compile(r"\b(unrequested|beyond the (ask|brief|intent)|out of scope|scope|not (asked|requested)|intent)\b", re.I)),
    ("style", re.compile(r"\b(style|naming|cosmetic|dead code|unused|trivial|identical shape)\b", re.I)),
)


def classify_class(finding):
    cat = (finding.get("category") or "").strip().lower()
    if cat in CLASSES:
        return cat
    text = finding.get("description") or ""
    for name, rx in CLASS_RULES:
        if rx.search(text):
            return name
    return "correctness"


def classify_owner(finding, selected):
    """Mechanical owner class. `selected` = the finding was sent to a fix round."""
    action = finding.get("action")
    text = (finding.get("description") or "").lower()
    if action == "no-op":
        return "gate_error"            # reviewer note that changes nothing
    if not selected:
        return "gate_error"            # declined, argued down, or shipped unfixed
    if any(d in text for d in LINKED_ONLY_DOCS):
        return "harness_load"          # rule lives in a doc that is linked, not auto-loaded
    if action == "ask-user" and (classify_class(finding) == "scope" or "intent" in text or "brief" in text):
        return "decomposition"         # the brief or spec left the choice open
    return "disposition"               # worker or fixer own the defect


def open_ro(state_dir):
    path = os.path.join(state_dir, "state.sqlite")
    return sqlite3.connect("file:%s?mode=ro" % path, uri=True)


def read_lines(path, warnings, label):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            return fh.read().splitlines()
    except FileNotFoundError:
        return []
    except OSError as exc:
        warnings.append({"source": label, "problem": "unreadable: %s" % exc.__class__.__name__})
        return []


def pick_runs(conn, repo_match, window, since_ts):
    sql = ("SELECT r.id, r.branch, r.status, r.created_at FROM runs r JOIN repos p ON p.id = r.repo_id "
           "WHERE (p.upstream_url LIKE ? OR p.working_path LIKE ?)")
    args = ["%" + repo_match + "%"] * 2
    if since_ts is not None:
        sql += " AND r.created_at >= ?"
        args.append(since_ts)
    # Oldest first within the window; --window takes the N earliest runs after --since-ts, or the N latest overall.
    if since_ts is not None:
        sql += " ORDER BY r.created_at ASC LIMIT ?"
    else:
        sql = "SELECT * FROM (" + sql + " ORDER BY r.created_at DESC LIMIT ?) ORDER BY 4 ASC"
    args.append(window)
    return conn.execute(sql, args).fetchall()


def run_findings(conn, run_id, warnings):
    """Distinct findings per run: first round in which an id+file appears, with whether it was selected."""
    rows = conn.execute(
        "SELECT sr.step_name, r.round, r.findings_json, r.selected_finding_ids FROM step_rounds r "
        "JOIN step_results sr ON sr.id = r.step_result_id WHERE sr.run_id = ? "
        "AND sr.step_name IN ('review','test','document') ORDER BY sr.step_name, r.round", (run_id,)).fetchall()
    seen, out, rounds = set(), [], Counter()
    for step, rnd, fj, sel in rows:
        if step == "review":
            rounds["review"] += 1
        if not fj:
            continue
        try:
            findings = json.loads(fj).get("findings") or []
            selected = set(json.loads(sel)) if sel else set()
        except (ValueError, AttributeError, TypeError):
            warnings.append({"source": "state.sqlite", "run": run_id, "problem": "unparseable findings_json"})
            continue
        for f in findings:
            key = (step, f.get("id"), f.get("file"))
            if key in seen:
                continue
            seen.add(key)
            f = dict(f, _step=step)
            f["_selected"] = f.get("id") in selected
            out.append(f)
    return out, rounds["review"]


def summarize_hooks(paths, warnings):
    fires, blocks, bad = Counter(), Counter(), 0
    decisions = defaultdict(Counter)
    for p in paths:
        for n, line in enumerate(read_lines(p, warnings, "hooks-log"), 1):
            if not line.strip():
                continue
            try:
                rec = json.loads(line)
                hook, decision = rec["hook"], str(rec["decision"])
            except (ValueError, KeyError, TypeError):
                bad += 1
                warnings.append({"source": "hooks-log", "file": os.path.basename(p), "line": n,
                                 "problem": "corrupt line skipped"})
                continue
            fires[hook] += 1
            decisions[hook][decision.split(":")[0]] += 1
            if decision.startswith("block"):
                blocks[hook] += 1
    hooks = {h: {"fires": fires[h], "blocks": blocks[h], "decisions": dict(decisions[h])} for h in sorted(fires)}
    return {"per_hook": hooks, "total_fires": sum(fires.values()), "total_blocks": sum(blocks.values()),
            "corrupt_lines": bad}


def build(args):
    warnings = []
    conn = open_ro(args.state_dir)
    try:
        runs = pick_runs(conn, args.repo_match, args.window, args.since_ts)
        per_run, owners, classes = [], Counter(), Counter()
        class_runs = defaultdict(set)
        owner_class = defaultdict(Counter)
        conflicts = pushes = 0
        for run_id, branch, status, created in runs:
            findings, review_rounds = run_findings(conn, run_id, warnings)
            logdir = os.path.join(args.state_dir, "logs", run_id)
            rebase = read_lines(os.path.join(logdir, "rebase.log"), warnings, "rebase.log")
            push = read_lines(os.path.join(logdir, "push.log"), warnings, "push.log")
            review = read_lines(os.path.join(logdir, "review.log"), warnings, "review.log")
            run_conflicts = sum(1 for ln in rebase if "conflicts detected" in ln)
            run_pushes = sum(1 for ln in push if "pushed successfully" in ln)
            fix_rounds = sum(1 for ln in review if ln.startswith("user-fix round starting"))
            conflicts += run_conflicts
            pushes += run_pushes
            for f in findings:
                o, c = classify_owner(f, f["_selected"]), classify_class(f)
                owners[o] += 1
                classes[c] += 1
                owner_class[o][c] += 1
                class_runs[c].add(run_id)
            per_run.append({"run": run_id, "branch": branch, "status": status, "created_at": created,
                            "review_rounds": review_rounds, "fix_rounds": fix_rounds,
                            "findings": len(findings), "rebase_conflicts": run_conflicts, "pushes": run_pushes})
    finally:
        conn.close()
    n = len(per_run)
    rounds = [r["review_rounds"] for r in per_run]
    summary = {
        "schema": SCHEMA_VERSION,
        "window": {"runs": n, "requested": args.window, "since_ts": args.since_ts,
                   "first_run": per_run[0]["run"] if per_run else None,
                   "last_run": per_run[-1]["run"] if per_run else None},
        "findings_by_owner": {o: owners[o] for o in OWNERS},
        "findings_total": sum(owners.values()),
        "owner_by_class": {o: dict(owner_class[o]) for o in OWNERS},
        "recurrence_by_class": {c: {"findings": classes[c], "runs": len(class_runs[c]),
                                    "recurring": len(class_runs[c]) >= 2} for c in CLASSES},
        "review_rounds": {"per_run": {r["run"]: r["review_rounds"] for r in per_run},
                          "mean": round(sum(rounds) / n, 2) if n else 0, "max": max(rounds) if rounds else 0},
        "rebase_conflicts": conflicts,
        "pushes": pushes,
        "hooks": summarize_hooks(args.hooks_log, warnings),
        "runs": per_run,
        "warnings": warnings,
    }
    return summary


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--window", type=int, default=20, help="number of runs (default 20)")
    ap.add_argument("--since-ts", type=int, default=None, help="epoch; take the first N runs created at or after it")
    ap.add_argument("--repo-match", default="finance-sentry")
    ap.add_argument("--state-dir", default=os.path.expanduser("~/.no-mistakes"))
    ap.add_argument("--hooks-log", action="append", default=[])
    ap.add_argument("--out")
    ap.add_argument("--embed-into", help="scorecard HTML whose summary block is replaced")
    args = ap.parse_args(argv)
    summary = build(args)
    text = json.dumps(summary, indent=2, sort_keys=True)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    if args.embed_into:
        with open(args.embed_into, "r", encoding="utf-8") as fh:
            page = fh.read()
        if not EMBED_RE.search(page):
            print("no summary block in %s" % args.embed_into, file=sys.stderr)
            return 2
        safe = text.replace("</", "<\\/")
        page = EMBED_RE.sub(lambda m: m.group(1) + "\n" + safe + "\n" + m.group(2), page, count=1)
        with open(args.embed_into, "w", encoding="utf-8") as fh:
            fh.write(page)
    if not args.out:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
