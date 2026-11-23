#!/usr/bin/env python3
"""Rewrite an EF Core migration script into CockroachDB-compatible SQL.

Usage:
    python3 scripts/rewrite-migrations.py [input.sql] [output.sql] [applied.csv]

Defaults: sql/pending.sql -> sql/cockroach.sql

`applied.csv` is the output of
`cockroach sql --format=csv --execute='SELECT "MigrationId" FROM "__EFMigrationsHistory"'`.
Migrations listed there have their whole block dropped, because CockroachDB
cannot evaluate EF's `DO $EF$ IF NOT EXISTS(history row)` guard and one-shot DDL
(such as `RENAME COLUMN`) has no `IF EXISTS` form to fall back on.

Why this exists
---------------
`dotnet ef migrations script --idempotent` emits PostgreSQL-flavoured SQL that
CockroachDB cannot run as-is, and it regenerates the whole schema on every
invocation. This script produces a script that is safe to re-apply.

Three things it has to get right:

1. EF wraps conditional DDL in `DO $EF$ ... END $EF$` blocks. CockroachDB
   rejects those, so they are unwrapped and the idempotency is preserved with
   CockroachDB's own `IF [NOT] EXISTS` / `ON CONFLICT DO NOTHING`.

2. `dotnet ef migrations script --output` writes a UTF-8 BOM. The cockroach
   CLI reads the file literally, so the BOM lands in front of the first
   statement and it dies with `syntax error`. Read with `utf-8-sig` (which
   strips it) and write back with plain `utf-8` (which adds none).

3. `cockroach sql --set=NAME=VALUE` configures CLIENT-SIDE options
   (display_format, errexit, ...), not server session variables, so
   `--set=statement_timeout=30s` fails with "unknown variable name". The
   timeout is therefore set with SQL.

No third-party dependencies - python3 from the standard library only, so it
runs unchanged on the CI runner and on Windows.
"""

import re
import sys
from pathlib import Path

DEFAULT_INPUT = Path("sql/pending.sql")
DEFAULT_OUTPUT = Path("sql/cockroach.sql")
STATEMENT_TIMEOUT = "30s"


def rewrite(source: str, applied: set[str] | None = None) -> str:
    applied = applied or set()

    # CockroachDB cannot parse EF's transaction wrappers or DO $EF$ blocks.
    source = re.sub(r"(?is)START TRANSACTION;\s*|COMMIT;\s*", "", source)

    # Each migration's statements sit inside
    #   DO $EF$ BEGIN IF NOT EXISTS(history row for 'Id') THEN <body> END IF; END $EF$;
    # i.e. "run only if this migration has not been recorded yet". CockroachDB
    # cannot evaluate that, so a block whose migration is already in
    # __EFMigrationsHistory is dropped outright; a block that is still pending
    # keeps its body. This is what makes one-shot DDL (RENAME COLUMN) safe:
    # CockroachDB has no IF EXISTS for it, so it must simply not re-run.
    block = re.compile(
        r'(?is)DO \$EF\$\s*BEGIN\s*IF NOT EXISTS\(\s*SELECT 1 FROM "__EFMigrationsHistory"\s*'
        r'WHERE "MigrationId" = \'(?P<id>[^\']+)\'\s*\)\s*THEN\s*(?P<body>.*?)\s*'
        r'END IF;\s*END \$EF\$;'
    )

    def keep_pending(match: re.Match[str]) -> str:
        return "" if match.group("id") in applied else match.group("body")

    source = block.sub(keep_pending, source)

    # Anything left wrapped (schema guards, etc.) cannot run as a DO block.
    source = re.sub(
        r"(?is)DO \$EF\$\s*BEGIN\s*IF NOT EXISTS\(.*?\)\s*THEN\s*(.*?)\s*END IF;\s*END \$EF\$;",
        r"\1",
        source,
    )

    # Make every statement re-appliable.
    source = re.sub(r'(?i)CREATE TABLE\s+(?=")', "CREATE TABLE IF NOT EXISTS ", source)
    source = re.sub(r'(?i)CREATE INDEX\s+(?=")', "CREATE INDEX IF NOT EXISTS ", source)
    # A unique index is its own statement shape; without this rule it stays
    # non-idempotent and the second apply dies on "index ... already exists"
    # (which is exactly how IX_Settings_Key failed).
    source = re.sub(
        r'(?i)CREATE UNIQUE INDEX\s+(?=")',
        "CREATE UNIQUE INDEX IF NOT EXISTS ",
        source,
    )
    source = re.sub(
        r'(?i)ALTER TABLE\s+("(?:[^"]|"")+")\s+ADD\s+CONSTRAINT\s+(?=")',
        r"ALTER TABLE \1 ADD CONSTRAINT IF NOT EXISTS ",
        source,
    )
    source = re.sub(
        r'(?i)ALTER TABLE\s+("(?:[^"]|"")+")\s+DROP\s+CONSTRAINT\s+(?=")',
        r"ALTER TABLE \1 DROP CONSTRAINT IF EXISTS ",
        source,
    )
    source = re.sub(
        r'(?i)ALTER TABLE\s+("(?:[^"]|"")+")\s+ADD\s+(?=")',
        r"ALTER TABLE \1 ADD COLUMN IF NOT EXISTS ",
        source,
    )
    source = re.sub(
        r'(?is)INSERT INTO "__EFMigrationsHistory" '
        r'\("MigrationId", "ProductVersion"\)\s*VALUES\s*'
        r"\('([^']+)', '([^']+)'\);",
        r'''INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('\1', '\2') ON CONFLICT ("MigrationId") DO NOTHING;''',
        source,
    )

    # Bound the run so a wedged DDL statement cannot hang the job forever.
    return f"SET statement_timeout = '{STATEMENT_TIMEOUT}';\n" + source


def read_applied(path: Path | None) -> set[str]:
    """Migration ids already recorded in __EFMigrationsHistory."""
    if path is None or not path.exists():
        return set()

    ids: set[str] = set()
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        value = line.strip().strip('"')
        # Skip the CSV header and blank lines.
        if not value or value == "MigrationId":
            continue
        ids.add(value)

    return ids


def main(argv: list[str]) -> int:
    if len(argv) > 4:
        print(__doc__)
        return 2

    in_path = Path(argv[1]) if len(argv) > 1 else DEFAULT_INPUT
    out_path = Path(argv[2]) if len(argv) > 2 else DEFAULT_OUTPUT
    applied_path = Path(argv[3]) if len(argv) > 3 else None

    if not in_path.exists():
        print(f"error: {in_path} not found - generate it first with:", file=sys.stderr)
        print("  dotnet ef migrations script --project FinanceManagerApi/FinanceManagerApi.csproj"
              " --startup-project FinanceManagerApi/FinanceManagerApi.csproj"
              " --idempotent --output sql/pending.sql", file=sys.stderr)
        return 1

    # utf-8-sig strips the BOM that `dotnet ef` writes.
    source = in_path.read_text(encoding="utf-8-sig")
    applied = read_applied(applied_path)
    result = rewrite(source, applied)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    # plain utf-8 - no BOM, or the cockroach CLI chokes on the first statement.
    out_path.write_text(result, encoding="utf-8")

    statements = len([s for s in result.split(";") if s.strip()])
    print(f"rewrote {in_path} -> {out_path} ({len(result.splitlines())} lines, "
          f"~{statements} statements, {len(applied)} applied migration(s) skipped)")

    if "RENAME COLUMN" in result and not applied:
        print("warning: one-shot DDL survived with no applied-migration list, so a "
              "re-run will fail on it. Pass the __EFMigrationsHistory rows as the "
              "third argument.", file=sys.stderr)

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
