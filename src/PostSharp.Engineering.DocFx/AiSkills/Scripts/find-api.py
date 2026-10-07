#!/usr/bin/env python3
"""Look up the API documentation of the skill by type or member name.

Usage (from the skill root directory, i.e. the directory containing api/):
    python scripts/find-api.py <name-or-uid>
    python scripts/find-api.py MyType
    python scripts/find-api.py MyNamespace.MyType.MyMethod

Prints the matching UIDs and, for the most specific matches, the full
documentation block (summary, syntax, parameters, remarks) extracted from the
DocFx YML — so you don't need to read the whole YML file.

Requires Python 3 and no third-party packages. If Python is unavailable, fall
back to: grep -i "<name>" api/.manifest, then read the listed .yml file.
"""

import json
import re
import sys
from pathlib import Path

MAX_LISTED = 25
MAX_BLOCKS = 3


def find_skill_root() -> Path:
    # The script lives in <skill-root>/scripts/; also allow running from the root.
    for candidate in (Path(__file__).resolve().parent.parent, Path.cwd()):
        if (candidate / "api" / ".manifest").is_file():
            return candidate
    sys.exit("Cannot find api/.manifest. Run this script from the skill root directory.")


def extract_item(yml_text: str, uid: str) -> str:
    """Extract the '- uid: <uid>' item block from a DocFx ManagedReference YML."""
    lines = yml_text.splitlines()
    out: list[str] = []
    capture = False
    for line in lines:
        if re.match(r"^- uid: " + re.escape(uid) + r"\s*$", line):
            capture = True
        elif capture and (line.startswith("- uid: ") or re.match(r"^[A-Za-z][A-Za-z0-9]*:", line)):
            break
        if capture:
            out.append(line)
    return "\n".join(out)


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    if not args:
        print(__doc__)
        return 1

    # A trailing '*' denotes all the overloads in a docfx xref; the manifest has no entry for it.
    query = args[0].rstrip("*")
    root = find_skill_root()
    manifest = json.loads((root / "api" / ".manifest").read_text(encoding="utf-8"))

    # Exact UID match first; otherwise case-insensitive substring match.
    matches = [u for u in manifest if u == query]
    if not matches:
        q = query.lower()
        matches = [u for u in manifest if q in u.lower()]

    if not matches:
        print(f"No API matching '{query}'. Try a shorter substring.")
        return 1

    print(f"{len(matches)} match(es) for '{query}':")
    for uid in matches[:MAX_LISTED]:
        print(f"  {uid}  ->  api/{manifest[uid]}")
    if len(matches) > MAX_LISTED:
        print(f"  ... and {len(matches) - MAX_LISTED} more. Refine the query.")

    # Print full documentation for the most specific (shortest-UID) matches.
    for uid in sorted(matches, key=len)[:MAX_BLOCKS]:
        yml_path = root / "api" / manifest[uid]
        if not yml_path.is_file():
            continue
        block = extract_item(yml_path.read_text(encoding="utf-8"), uid)
        if block:
            print("\n" + "=" * 72)
            print(block)

    return 0


if __name__ == "__main__":
    sys.exit(main())
