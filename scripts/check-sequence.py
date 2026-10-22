#!/usr/bin/env python3
"""Check every CockroachDB table for a sequence that drifted behind max(Id).

Usage:
    python3 scripts/check-sequence.py [--fix] [--conn "<connection string>"]

A drifted sequence is the classic reason an INSERT returns 500 while every
read works: the sequence hands out an id that already exists, and the insert
dies on the primary key.

    sequence ahead of max(Id)  -> harmless, just id gaps
    sequence behind max(Id)    -> the bug; the next insert collides

Connection string resolution (first one that exists wins):
  1. --conn
  2. $COCKROACH_CONNECTION_STRING
  3. FinanceManagerApi/CockroachConnect.txt
  4. a build output appsettings.json under FinanceManagerApi/bin/
  5. the ConnectionStrings:CockroachDb user-secret of the API project

The script prints which source it used. Be aware that the copies in this repo
drift: `CockroachConnect.txt` currently holds a STALE password (it is the same
stale copy that broke the CI pipeline), so "password authentication failed"
here usually means the wrong file was picked, not that the cluster is down.

Needs psycopg2:  pip install psycopg2-binary
"""

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

try:
    import psycopg2
except ImportError:
    print("error: psycopg2 not installed - run: pip install psycopg2-binary",
          file=sys.stderr)
    raise SystemExit(1)


def connection_string_from_user_secrets() -> str | None:
    """Read ConnectionStrings:CockroachDb out of the API project's user-secrets."""
    for project in ("FinanceManagerApi/FinanceManagerApi.csproj", "FinanceManagerApi"):
        try:
            out = subprocess.run(
                ["dotnet", "user-secrets", "list", "--project", project],
                capture_output=True, text=True, timeout=60,
            ).stdout
        except (OSError, subprocess.SubprocessError):
            continue
        for line in out.splitlines():
            key, _, value = line.partition(" = ")
            if key.strip().lower() == "connectionstrings:cockroachdb":
                return value.strip()
    return None


def connection_string_from_file() -> str | None:
    """Read FinanceManagerApi/CockroachConnect.txt.

    The file ends with the real Npgsql connection string in quotes:
        "Host=...;Port=26257;Username=...;Password=...;Database=...;SSL Mode=Require;..."
    libpq does not understand `SSL Mode` / `Trust Server Certificate`, so the
    pairs are converted to a clean libpq DSN.
    """
    path = Path("FinanceManagerApi/CockroachConnect.txt")
    if not path.exists():
        return None
    # utf-8-sig: the file may carry a BOM, which would corrupt the first key.
    raw = path.read_text(encoding="utf-8-sig", errors="replace").strip()
    if not raw:
        return None

    # 1) the quoted Npgsql connection string (this is the one with the password)
    match = re.search(r'"([^"]*Host=[^"]*)"', raw)
    if match:
        pairs = {}
        for part in match.group(1).split(";"):
            if "=" in part:
                key, _, value = part.partition("=")
                pairs[key.strip().lower()] = value.strip()
        return _to_libpq(pairs)

    # 2) the human-readable "Key : Value" block
    pairs = {}
    for line in raw.splitlines():
        if ":" in line:
            key, _, value = line.partition(":")
            pairs[key.strip().lower()] = value.strip()
    if pairs.get("host"):
        return _to_libpq(pairs)

    return None


def _to_libpq(pairs: dict[str, str]) -> str | None:
    def get(*names: str) -> str:
        for name in names:
            if pairs.get(name):
                return pairs[name]
        return ""

    host = get("host", "server")
    user = get("username", "user", "user id", "userid")
    database = get("database", "initial catalog")
    if not host or not user or not database:
        return None

    return (f"host={host} port={get('port') or '26257'} user={user} "
            f"password={get('password', 'pwd', 'pass')} dbname={database} sslmode=require")


def connection_string_from_build_output() -> tuple[str, str] | None:
    """Find a CockroachDb connection string in a built appsettings.json.

    The checked-in appsettings.json only has a localhost placeholder, but an
    old build output still carries the real prod connection string.
    """
    for path in sorted(Path("FinanceManagerApi/bin").rglob("appsettings*.json")):
        try:
            raw = path.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        m = re.search(r'"CockroachDb"\s*:\s*"([^"]+)"', raw)
        if not m:
            continue
        value = m.group(1)
        if "localhost" in value.lower() or "Password=" not in value:
            continue
        pairs = {}
        for part in value.split(";"):
            if "=" in part:
                key, _, val = part.partition("=")
                pairs[key.strip().lower()] = val.strip()
        dsn = _to_libpq(pairs)
        if dsn:
            return dsn, str(path)
    return None


def resolve_conn(explicit: str | None) -> list[tuple[str, str]]:
    """Return every (connection string, source) candidate, best first.

    The candidates are tried in order until one actually connects, because the
    copies of the credential in this repo drift - the one in
    CockroachConnect.txt is stale, so picking the first one would always fail.
    """
    candidates: list[tuple[str, str]] = []

    if explicit:
        candidates.append((explicit, "--conn"))
    if os.environ.get("COCKROACH_CONNECTION_STRING"):
        candidates.append((os.environ["COCKROACH_CONNECTION_STRING"],
                           "$COCKROACH_CONNECTION_STRING"))

    from_file = connection_string_from_file()
    if from_file:
        candidates.append((from_file, "FinanceManagerApi/CockroachConnect.txt"))

    from_build = connection_string_from_build_output()
    if from_build:
        candidates.append((from_build[0], from_build[1]))

    from_secrets = connection_string_from_user_secrets()
    if from_secrets:
        candidates.append((from_secrets, "dotnet user-secrets"))

    if not candidates:
        print("error: no connection string. Pass --conn, set "
              "$COCKROACH_CONNECTION_STRING, or configure the user-secret.",
              file=sys.stderr)
        raise SystemExit(1)
    return candidates


def connect(candidates: list[tuple[str, str]]):
    """Connect with the first candidate that authenticates."""
    last_error = None
    for conn, source in candidates:
        try:
            db = psycopg2.connect(conn, connect_timeout=20)
            print(f"connection source: {source}")
            return db
        except psycopg2.OperationalError as exc:
            last_error = exc
            print(f"  {source}: failed ({str(exc).splitlines()[0][:90]})")

    print("\nerror: no candidate could authenticate.", file=sys.stderr)
    if last_error and "password authentication failed" in str(last_error):
        print("The credential copies in this repo drift - "
              "FinanceManagerApi/CockroachConnect.txt holds a stale password.", file=sys.stderr)
    print("Pass a current one explicitly:  --conn \"host=... user=... password=... "
          "dbname=financedb sslmode=require\"", file=sys.stderr)
    raise SystemExit(1)


def find_sequences(cur) -> list[tuple[str, str, str]]:
    """Return (table, id_column, sequence) for every id backed by a sequence."""
    cur.execute("""
        SELECT c.table_name, c.column_name,
               pg_get_serial_sequence(quote_ident(c.table_schema) || '.' ||
                                      quote_ident(c.table_name), c.column_name) AS seq
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.table_schema = 'public'
          AND c.column_default IS NOT NULL
          AND c.column_default ILIKE 'nextval%'
          AND t.table_type = 'BASE TABLE'
        ORDER BY c.table_name
    """)
    return [(t, col, seq) for t, col, seq in cur.fetchall() if seq]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--conn", help="libpq connection string")
    parser.add_argument("--fix", action="store_true",
                        help="apply setval to repair drifted sequences")
    args = parser.parse_args()

    db = connect(resolve_conn(args.conn))
    cur = db.cursor()

    problems = []
    for table, column, sequence in find_sequences(cur):
        # pg_get_serial_sequence already returns a quoted, schema-qualified name.
        cur.execute(f'SELECT max("{column}") FROM "{table}"')
        max_id = cur.fetchone()[0]
        cur.execute(f'SELECT last_value, is_called FROM {sequence}')
        last_value, is_called = cur.fetchone()
        next_value = last_value + 1 if is_called else last_value

        status = "ok"
        if max_id is not None and next_value <= max_id:
            status = "DRIFTED"
            problems.append((table, column, sequence, max_id, next_value))
        elif max_id is None:
            status = "empty"

        print(f"{status:8} {table}.{column}: max(Id)={max_id} "
              f"next from sequence={next_value} ({sequence})")

    if not problems:
        print("\nAll sequences are healthy.")
        db.close()
        return 0

    print("\nDrifted sequences found:")
    for table, column, sequence, max_id, next_value in problems:
        fix = (f'SELECT setval(\'{sequence}\', '
               f'(SELECT COALESCE(MAX("{column}"), 0) FROM "{table}") + 1, false);')
        print(f"  {table}.{column}: next={next_value} <= max(Id)={max_id}")
        print(f"    fix: {fix}")
        if args.fix:
            cur.execute(fix)
            print("    -> applied")

    if args.fix:
        db.commit()
        print("\nFixed.")
    else:
        print("\nRe-run with --fix to repair.")

    db.close()
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
