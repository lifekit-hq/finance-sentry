"""Check the constitution's "automatic block" wording against docs/harness/constitution-checks.md.

A rule bullet (`- CCn ...`) may say "automatic block" only if its row in the table names a check; a row with a check
must have that check's name (its first backticked token) in the bullet; every bullet and row must pair up. Plain
Python, no network.
"""
import os
import re
import sys

REPO = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ROW = re.compile(r"^\|\s*(CC\d+)\s*\|(.*)\|(.*)\|(.*)\|\s*$")


def parse_table(text):
    """Returns {id: check cell} for each `| CCn | rule | check | decision |` row."""
    rows = {}
    for line in text.splitlines():
        m = ROW.match(line)
        if m:
            rows[m.group(1)] = m.group(3).strip()
    return rows


def parse_bullets(text):
    """Returns {id: bullet text} for each top-level bullet that starts `- CCn`, continuation lines joined."""
    bullets, cur = {}, None
    for line in text.splitlines():
        m = re.match(r"^- (CC\d+)\b", line)
        if m:
            cur = m.group(1)
            bullets[cur] = line
        elif cur and line.startswith("  ") and line.strip():
            bullets[cur] += " " + line.strip()
        else:
            cur = None
    return bullets


def problems(constitution, table):
    rows, bullets = parse_table(table), parse_bullets(constitution)
    out = []
    for rid in sorted(set(bullets) - set(rows), key=lambda r: int(r[2:])):
        out.append(f"{rid} has no row in the table")
    for rid in sorted(set(rows) - set(bullets), key=lambda r: int(r[2:])):
        out.append(f"{rid} is in the table but not in the constitution")
    for rid in sorted(set(rows) & set(bullets), key=lambda r: int(r[2:])):
        check, bullet = rows[rid], " ".join(bullets[rid].split())
        if check.lower().startswith("none"):
            if re.search(r"automatic\s+block", bullet, re.I):
                out.append(f"{rid} says 'automatic block' but the table marks it as having no check")
        else:
            token = re.search(r"`([^`]+)`", check)
            if not token:
                out.append(f"{rid} has a check cell with no backticked check name")
            elif token.group(1) not in bullet:
                out.append(f"{rid} does not name its check '{token.group(1)}'")
    # Any "automatic block" outside a mapped bullet is a rule the table cannot vouch for. The sync-impact comment is history.
    constitution = re.sub(r"<!--.*?-->", "", constitution, flags=re.S)
    mapped = " ".join(bullets.values())
    stray = len(re.findall(r"automatic\s+block", constitution, re.I)) - len(re.findall(r"automatic\s+block", mapped, re.I))
    if stray:
        out.append(f"{stray} 'automatic block' mention(s) outside a tagged rule")
    return out


def main():
    with open(os.path.join(REPO, ".specify", "memory", "constitution.md"), encoding="utf-8") as fh:
        constitution = fh.read()
    with open(os.path.join(REPO, "docs", "harness", "constitution-checks.md"), encoding="utf-8") as fh:
        table = fh.read()
    found = problems(constitution, table)
    for p in found:
        print(f"constitution-checks: {p}", file=sys.stderr)
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
