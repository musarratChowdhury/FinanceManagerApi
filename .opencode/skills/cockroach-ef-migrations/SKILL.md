---
name: cockroach-ef-migrations
description: Generate and apply EF Core migrations to CockroachDB without breaking them. Use when the CI migration step fails, when adding a migration, when the cockroach CLI errors with "unknown variable name" or "syntax error", or when setting up a fresh database. Covers the --set client-option trap, the UTF-8 BOM from dotnet ef, rewriting EF DDL blocks into CockroachDB-compatible idempotent SQL, and verifying the apply locally in Docker.
compatibility: opencode
metadata:
  project: FinanceManagerApi
  stack: "ef-core, cockroachdb, npgsql"
---

## What I do

EF Core's migration SQL is PostgreSQL-flavoured, not CockroachDB-flavoured, and
the `cockroach sql` CLI has two sharp edges. This skill produces a script that
is safe to re-apply and runs the apply step the same way CI does — locally
first, so you do not discover the failure in the pipeline.

## The pipeline

```
dotnet ef migrations script --idempotent --output sql/pending.sql
        │
        ├─ rewrite (python)  ──► sql/cockroach.sql
        │
        └─ cockroach sql --url=... --file=sql/cockroach.sql
```

The rewrite is not optional: EF emits `DO $EF$ ... END $EF$` blocks, which
CockroachDB does not accept as DDL, and the `--idempotent` script re-creates the
whole schema every run.

## Trap 1 — `--set` is client-side options, not session variables

```
$ cockroach sql --url=... --set=statement_timeout=30s --file=...
ERROR: -e: unknown variable name: "statement_timeout"
```

`--set` configures the **SQL shell's client-side options** (display_format,
errexit, echo, border, check_syntax, …). `statement_timeout` is a *server*
session variable, so the CLI does not recognise it.

Set it with SQL instead, at the top of the file:

```sql
SET statement_timeout = '30s';
```

## Trap 2 — `dotnet ef` writes a UTF-8 BOM

```
$ cockroach sql --url=... --file=sql/cockroach.sql
ERROR: at or near "﻿create": syntax error
```

`dotnet ef migrations script --output` writes a UTF-8 BOM. The cockroach CLI
reads the file literally, so the BOM lands in front of the first statement.
Strip it on read, do not re-add it on write:

```python
source = Path("sql/pending.sql").read_text(encoding="utf-8-sig")   # strips
...
Path("sql/cockroach.sql").write_text(source, encoding="utf-8")     # adds none
```

The same applies to anything else that writes SQL on Windows: PowerShell's
`Out-File -Encoding utf8` emits a BOM. Use a BOM-less writer.

## The rewrite

```python
import re
from pathlib import Path

source = Path("sql/pending.sql").read_text(encoding="utf-8-sig")

# EF wraps conditional DDL in DO $EF$ blocks CockroachDB cannot parse.
source = re.sub(r"(?is)START TRANSACTION;\s*|COMMIT;\s*", "", source)
source = re.sub(
    r"(?is)DO \$EF\$\s*BEGIN\s*IF NOT EXISTS\(.*?\)\s*THEN\s*(.*?)\s*END IF;\s*END \$EF\$;",
    r"\1", source)

# Make it re-appliable.
source = re.sub(r'(?i)CREATE TABLE\s+(?=")',  "CREATE TABLE IF NOT EXISTS ", source)
source = re.sub(r'(?i)CREATE INDEX\s+(?=")',  "CREATE INDEX IF NOT EXISTS ", source)
source = re.sub(r'(?i)ALTER TABLE\s+("(?:[^"]|"")+")\s+ADD\s+CONSTRAINT\s+(?=")',
                r"ALTER TABLE \1 ADD CONSTRAINT IF NOT EXISTS ", source)
source = re.sub(r'(?i)ALTER TABLE\s+("(?:[^"]|"")+")\s+DROP\s+CONSTRAINT\s+(?=")',
                r"ALTER TABLE \1 DROP CONSTRAINT IF EXISTS ", source)
source = re.sub(r'(?i)ALTER TABLE\s+("(?:[^"]|"")+")\s+ADD\s+(?=")',
                r"ALTER TABLE \1 ADD COLUMN IF NOT EXISTS ", source)
source = re.sub(
    r'(?is)INSERT INTO "__EFMigrationsHistory" '
    r'\("MigrationId", "ProductVersion"\)\s*VALUES\s*'
    r"\('([^']+)', '([^']+)'\);",
    r'''INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('\1', '\2') ON CONFLICT ("MigrationId") DO NOTHING;''', source)

source = "SET statement_timeout = '30s';\n" + source
Path("sql/cockroach.sql").write_text(source, encoding="utf-8")
```

## Verify locally before pushing

**No Windows cockroach binaries are published** — run the CLI in Docker with
the same version the CI downloads (`cockroach-v24.2.0`):

```bash
docker run --rm -v "$PWD/sql:/sql" cockroachdb/cockroach:v24.2.0 sql \
  --url="postgresql://user:pass@host:26257/db?sslmode=require" \
  --file=/sql/cockroach.sql
```

Exit code 0 = the script is valid. Read the output for `NOTICE: … already
exists, skipping` — those are the idempotent no-ops, and their presence proves
the script is being re-applied rather than creating anything.

**Check the script is not destructive before running it against a real
cluster:**

```bash
grep -nE '^\s*(DROP|DELETE|TRUNCATE|RENAME|ALTER COLUMN)' sql/cockroach.sql
```

Anything that shows up needs a deliberate decision — `ALTER COLUMN … DROP NOT
NULL` is fine, `DROP COLUMN` is not.

## Fresh database

`db-fresh.ps1` at the repo root does the local version of all of the above
(creates `financedb`, generates the script, applies it). Re-run it after any
pull that touches `FinanceManagerApi/Migrations/`.

## Do not use `dotnet ef database update`

`Program.cs` registers `UseNpgsql(connstring)` with no CockroachDB mode, so
EF's own update path emits the `DO $EF$` blocks CockroachDB rejects. That is
exactly why this repo uses a generated script instead.

## Related

- `api-500-triage` — the other reason a POST returns 500: sequence drift.
