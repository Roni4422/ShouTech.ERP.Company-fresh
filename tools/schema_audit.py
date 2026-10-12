#!/usr/bin/env python3
"""Report duplicated SQL table definitions and runtime schema creation sites."""
from __future__ import annotations
import argparse
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CREATE_TABLE = re.compile(r"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:\[?([\w.]+)\]?)", re.I)
ARABIC = re.compile(r"[\u0600-\u06ff]")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path, help="Optional Markdown report path")
    args = parser.parse_args()
    root = args.root.resolve()
    definitions: dict[str, list[str]] = defaultdict(list)
    runtime_ddl: list[str] = []
    arabic_hits: list[str] = []

    for path in root.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in {".sql", ".cs", ".xaml", ".json", ".xml", ".ps1"}:
            continue
        try:
            content = path.read_text(encoding="utf-8-sig")
        except (UnicodeDecodeError, OSError):
            continue
        relative = path.relative_to(root).as_posix()
        for match in CREATE_TABLE.finditer(content):
            table = match.group(1).strip("[]")
            line = content.count("\n", 0, match.start()) + 1
            definitions[table.lower()].append(f"{relative}:{line}")
            if path.suffix.lower() == ".cs":
                runtime_ddl.append(f"{relative}:{line} — CREATE TABLE {table}")
        if ARABIC.search(content) and "Resources/Lang/" not in relative.replace("\\", "/"):
            for number, line in enumerate(content.splitlines(), 1):
                if ARABIC.search(line):
                    arabic_hits.append(f"{relative}:{number}")

    duplicates = {name: places for name, places in sorted(definitions.items()) if len(places) > 1}
    lines = ["# Schema Audit", "", f"- SQL table definitions found: {sum(map(len, definitions.values()))}",
             f"- Table names defined more than once: {len(duplicates)}", f"- Runtime C# CREATE TABLE statements: {len(runtime_ddl)}",
             f"- Files/lines containing Arabic outside language resources: {len(arabic_hits)}", "", "## Duplicate table definitions", ""]
    if duplicates:
        for table, places in duplicates.items():
            lines.append(f"- **{table}**: " + ", ".join(places))
    else:
        lines.append("- None found.")
    lines += ["", "## Runtime schema creation sites", ""] + ( [f"- {x}" for x in runtime_ddl] if runtime_ddl else ["- None found."] )
    lines += ["", "## Arabic content outside language resources", ""] + ([f"- {x}" for x in arabic_hits] if arabic_hits else ["- None found."])
    report = "\n".join(lines) + "\n"
    if args.output:
        out = args.output if args.output.is_absolute() else root / args.output
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(report, encoding="utf-8")
        print(f"Wrote {out}")
    else:
        print(report)
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
