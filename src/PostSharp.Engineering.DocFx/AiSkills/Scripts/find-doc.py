#!/usr/bin/env python3
"""Search the conceptual documentation index of the skill by keyword.

Usage (from the skill root directory, i.e. the directory containing index.yml):
    python scripts/find-doc.py <keyword> [<keyword> ...]
    python scripts/find-doc.py cache invalidation
    python scripts/find-doc.py "getting started"
    python scripts/find-doc.py <article-uid>

Searches article titles, uids, summaries, keywords, and paths in index.yml and
prints the matching articles with their file paths, ready to be read. A single
keyword that equals the uid of an article returns only that article, which
resolves an <xref:uid> link.

Requires Python 3 and no third-party packages. If Python is unavailable, fall
back to: grep -i -B3 "<keyword>" index.yml (each entry lists name, path,
summary, and keywords).
"""

import re
import sys
from pathlib import Path

MAX_RESULTS = 25


def find_skill_root() -> Path:
    for candidate in (Path(__file__).resolve().parent.parent, Path.cwd()):
        if (candidate / "index.yml").is_file():
            return candidate
    sys.exit("Cannot find index.yml. Run this script from the skill root directory.")


def parse_index(text: str) -> list[dict]:
    """Light-weight parse of index.yml: each item starts with a 'name:' key."""
    records: list[dict] = []
    current: dict = {}
    for line in text.splitlines():
        m = re.match(r"^\s*-?\s*(name|uid|path|summary|keywords):\s*(.*)$", line)
        if not m:
            continue
        key, value = m.group(1), m.group(2).strip().strip("'\"")
        if key == "name" and current:
            records.append(current)
            current = {}
        current[key] = value
    if current:
        records.append(current)
    return records


def main() -> int:
    terms = [t.lower() for t in sys.argv[1:]]
    if not terms:
        print(__doc__)
        return 1

    root = find_skill_root()
    records = parse_index((root / "index.yml").read_text(encoding="utf-8"))

    def matches(record: dict) -> bool:
        haystack = " ".join(record.get(k, "") for k in ("name", "uid", "path", "summary", "keywords")).lower()
        return all(t in haystack for t in terms)

    hits = [r for r in records if len(terms) == 1 and r.get("uid", "").lower() == terms[0]]
    if not hits:
        hits = [r for r in records if matches(r)]

    for r in hits[:MAX_RESULTS]:
        print(f"- {r.get('name', '?')}")
        if r.get("uid"):
            print(f"  uid: {r['uid']}")
        if r.get("path"):
            print(f"  path: {r['path']}")
        if r.get("summary"):
            print(f"  {r['summary'][:220]}")
    if len(hits) > MAX_RESULTS:
        print(f"... and {len(hits) - MAX_RESULTS} more. Add keywords to narrow down.")
    print(f"\n{len(hits)} match(es).")
    return 0 if hits else 1


if __name__ == "__main__":
    sys.exit(main())
