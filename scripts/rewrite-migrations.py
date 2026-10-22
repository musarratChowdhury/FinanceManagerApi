#!/usr/bin/env python3
"""Rewrite an EF Core migration script into CockroachDB-compatible SQL.

Usage:
    python3 scripts/rewrite-migrations.py [input.sql] [output.sql]

Defaults: sql/pending.sql -> sql/cockroach.sql

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


def rewrite(source: str) -> str:
    # CockroachDB cannot parse EF's transaction wrappers or DO $EF$ blocks.
    source = re.sub(r"(?is)START TRANSACTION;\s*|COMMIT;\s*", "", source)
    source = re.sub(
        r"(?is)DO \$EF\$\s*BEGIN\s*IF NOT EXISTS\(.*?\)\s*THEN\s*(.*?)\s*END IF;\s*END \$EF\$;",
        r"\1",
        source,
    )

    # Make every statement re-appliable.
    source = re.sub(r'(?i)CREATE TABLE\s+(?=")', "CREATE TABLE IF NOT EXISTS ", source)
    source = re.sub(r'(?i)CREATE INDEX\s+(?=")', "CREATE INDEX IF NOT EXISTS ", source)
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


def main(argv: list[str]) -> int:
    if len(argv) > 3:
        print(__doc__)
        return 2

    in_path = Path(argv[1]) if len(argv) > 1 else DEFAULT_INPUT
    out_path = Path(argv[2]) if len(argv) > 2 else DEFAULT_OUTPUT

    if not in_path.exists():
        print(f"error: {in_path} not found - generate it first with:", file=sys.stderr)
        print("  dotnet ef migrations script --project FinanceManagerApi/FinanceManagerApi.csproj"
              " --startup-project FinanceManagerApi/FinanceManagerApi.csproj"
              " --idempotent --output sql/pending.sql", file=sys.stderr)
        return 1

    # utf-8-sig strips the BOM that `dotnet ef` writes.
    source = in_path.read_text(encoding="utf-8-sig")
    result = rewrite(source)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    # plain utf-8 - no BOM, or the cockroach CLI chokes on the first statement.
    out_path.write_text(result, encoding="utf-8")

    statements = len([s for s in result.split(";") if s.strip()])
    print(f"rewrote {in_path} -> {out_path} ({len(result.splitlines())} lines, "
          f"~{statements} statements)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
