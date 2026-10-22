---
name: api-500-triage
description: Find the real cause behind a generic 500 from the FinanceManagerApi (the response only says "An error occurred, please try again later"). Use when a POST/PUT returns 500, when an endpoint works in one environment and not another, or when you need to inspect the CockroachDB schema and data directly. Covers running the API locally in Development against the real database, reading the Npgsql/EF inner exception, and the sequence-drift failure mode.
compatibility: opencode
metadata:
  project: FinanceManagerApi
  stack: "aspnet-core, ef-core, npgsql, cockroachdb"
---

## What I do

`ExceptionMiddleware` swallows the real error in Production and returns
`{"details":null,"statusCode":500,"message":"An error occurred, please try again
later"}`. This skill gets you the actual exception — and, when the problem is
the database rather than the code, the actual schema and data.

## Step 1 — reproduce locally in Development

Development mode returns the message **and the stack trace**. Run the API
locally against the same database the failure happens on:

```powershell
$env:ConnectionStrings__CockroachDb="Host=...;Port=26257;Username=...;Password=...;Database=financedb;SSL Mode=Require"
$env:JWT__Secret="any-local-secret"; $env:JWT__Issuer="http://localhost:5006"; $env:JWT__Audience="user"
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:5006"
dotnet run --project FinanceManagerApi --no-launch-profile
```

- `DbInitializer` only seeds when the tables are empty, so pointing it at the
  prod database is safe — it logs "already exists - skipping".
- Your own `JWT__Secret` means the tokens the **prod** API issues will not
  validate locally. Log in through the local instance to get a local token:
  `POST /api/Authentication/login` with the same credentials.
- `UseHttpsRedirection` logs "Failed to determine the https port for redirect"
  and then does nothing when only an HTTP URL is configured — that warning is
  harmless.

The console shows the failed SQL, the EF command, and the inner exception.

## Step 2 — read the inner exception

The EF wrapper message is always the same and never the useful part. Go one
level deeper:

```
System.ArgumentException: Cannot write DateTime with Kind=Unspecified to
PostgreSQL type 'timestamp with time zone', only UTC is supported.
```

That is the whole diagnosis. See "The date trap" below.

## Step 3 — look at the database directly

Either the `cockroachdb-cloud` MCP (list_clusters, get_cluster, and its SQL
tools) or `psycopg2` with the same connection string:

```python
import psycopg2
c = psycopg2.connect('host=... port=26257 user=... password=... dbname=financedb sslmode=require')
cur = c.cursor()
cur.execute("""select column_name, data_type, is_nullable, column_default
               from information_schema.columns
               where table_name = 'Expenses' order by ordinal_position""")
for r in cur.fetchall(): print(r)

cur.execute("""select con.conname, pg_get_constraintdef(con.oid)
               from pg_constraint con
               join pg_class rel on rel.oid = con.conrelid
               where rel.relname = 'Expenses'""")
for r in cur.fetchall(): print(r)
```

Watch for:
- a column that is `NOT NULL` with no default and no value in the payload,
- a `NOT NULL` column the DTO/entity does not carry at all,
- FK columns (`ExpenseCategoryId`, `ReceiptId`) pointing at ids that do not
  exist — `Expense.CreatedBy` has **no** FK, so ownership is enforced in code.

## The date trap (the most common 500 here)

Npgsql 6+ refuses to write a `DateTime` whose `Kind` is not `Utc` into a
`timestamp with time zone` column. A JSON date-only string (`"2026-10-22"`)
deserialises with `Kind=Unspecified` → insert fails.

Fix on the server (the service normalises before saving):

```csharp
if (dto.ExpenseDate.HasValue && dto.ExpenseDate.Value.Kind == DateTimeKind.Unspecified)
    dto.ExpenseDate = DateTime.SpecifyKind(dto.ExpenseDate.Value, DateTimeKind.Utc);
```

Also check the entity defaults: `Income.EntryDate` and `IncomeCategory.EntryDate`
are `DateTime.Now` (**Local**) — Npgsql rejects Local for `timestamptz` too.
They should be `UtcNow`.

## The sequence-drift trap (500 on insert, reads are fine)

CockroachDB's `SERIAL`/`IDENTITY` columns have a sequence that can drift
**behind** `max(Id)`. The next insert then collides with an existing primary
key and the API returns 500 with a duplicate-key inner exception.

```sql
select count(*), max("Id") from "Expenses";
select last_value, is_called from "Expenses_Id_seq";
```

If `last_value < max("Id")`, fix it:

```sql
SELECT setval('"Expenses_Id_seq"',
               (SELECT COALESCE(MAX("Id"), 0) FROM "Expenses") + 1, false);
```

A sequence *ahead* of `max(Id)` is harmless (just id gaps). Behind is the bug.

## Step 4 — confirm the fix

Call the endpoint again and check the status code, not the body:

```python
r = requests.post('https://fmapi.muhit.dev/api/Expense/Create', headers=h, json=payload, timeout=60)
print(r.status_code)   # 200 == fixed
```

Then delete the row you created so the ledger stays clean:

```python
requests.delete(f"{base}/api/Expense/Delete/{created_id}", headers=h, timeout=60)
```

## Related

- `cockroach-ef-migrations` — the migration pipeline and its two CLI traps.
