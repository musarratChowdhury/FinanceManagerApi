# Cloud CockroachDB Migration Guide

> How to apply EF Core migrations to the production CockroachDB Cloud cluster using dotnet scripts.

## When to use this guide

You need to follow this guide whenever a new EF Core migration is added to the project and the cloud database schema needs to be updated. This happens after `git pull` that includes new migrations, or after adding a new migration locally.

## Prerequisites

### 1. .NET 10 SDK and EF tool

```powershell
dotnet --version          # Should be 10.x
dotnet ef --version       # Should be 10.x
```

Install the EF tool if missing:
```powershell
dotnet tool install --global dotnet-ef --version 10.*
```

### 2. Cloud connection string

The cloud CockroachDB connection string lives in CockroachDB Cloud. Get it from the CockroachDB Cloud console.

Format:
```
Host=weird-gorilla-7085.8nk.cockroachlabs.cloud;Port=26257;Username=musarrat;Password=<PASSWORD>;Database=financedb;SSL Mode=Require;Trust Server Certificate=true;
```

Replace `<PASSWORD>` with the current password. If you don't know it, check `FinanceManagerApi/CockroachConnect.txt` (the password there is **compromised** — rotate it in CockroachDB Cloud).

### 3. CockroachDB Cloud SQL Lab access

You need access to the CockroachDB Cloud SQL Lab for your cluster. Open the CockroachDB Cloud console, select your cluster, and click **SQL Lab**.

---

## Step-by-step

### Step 1: Build the project

```powershell
dotnet build FinanceManagerApi --nologo -v quiet
```

### Step 2: Generate the migration SQL script

```powershell
dotnet ef migrations script --idempotent --project FinanceManagerApi --output migrations.sql
```

This creates `migrations.sql` in the current directory.

### Step 3: Clean the script for CockroachDB

The EF-generated SQL uses PostgreSQL `DO $EF$` blocks to wrap DDL statements. CockroachDB **does not support DDL inside anonymous function blocks** — you will get:

```
ERROR: unimplemented: CREATE TABLE usage inside a function definition is not supported
```

You must post-process the script to strip the `DO $EF$` wrappers and convert the DDL to CockroachDB-compatible idempotent forms.

Save the following as `clean-migrations.ps1` in the repo root:

```powershell
param(
    [string]$InputFile = "migrations.sql",
    [string]$OutputFile = "migrations-clean.sql"
)

$content = Get-Content -Raw $InputFile

$cleaned = $content

# Unwrap EF's DO blocks
$cleaned = $cleaned -replace '(?s)DO \$EF\$\s*BEGIN\s*IF NOT EXISTS\(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = ''[^'']+''\) THEN\s+', ''
$cleaned = $cleaned -replace '(?s)\s+END IF;\s*END \$EF\$;', ';'

# Normalize double semicolons
$cleaned = $cleaned -replace ';\s*;', ';'

# Transform DDL to idempotent CockroachDB forms
$cleaned = $cleaned -replace 'ALTER TABLE\s+("(?:[^"]|"")+")\s+ADD\s+CONSTRAINT\s+(?=")', 'ALTER TABLE $1 ADD CONSTRAINT IF NOT EXISTS '
$cleaned = $cleaned -replace 'ALTER TABLE\s+("(?:[^"]|"")+")\s+DROP\s+CONSTRAINT\s+(?=")', 'ALTER TABLE $1 DROP CONSTRAINT IF EXISTS '
$cleaned = $cleaned -replace 'ALTER TABLE\s+("(?:[^"]|"")+")\s+ADD\s+(?!CONSTRAINT)(?=")', 'ALTER TABLE $1 ADD COLUMN IF NOT EXISTS '
$cleaned = $cleaned -replace 'CREATE TABLE\s+(?=")', 'CREATE TABLE IF NOT EXISTS '
$cleaned = $cleaned -replace 'CREATE INDEX\s+(?=")', 'CREATE INDEX IF NOT EXISTS '

# Make __EFMigrationsHistory inserts idempotent
$cleaned = $cleaned -replace '(?s)INSERT INTO "__EFMigrationsHistory" \("MigrationId", "ProductVersion"\)\s*VALUES \(''([^'']+)'', ''([^'']+)''\);', 'INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES (''$1'', ''$2'') ON CONFLICT ("MigrationId") DO NOTHING;'

# Prepend header
$header = @(
    "-- Auto-generated cleaned migration script for CockroachDB Cloud SQL Lab",
    "-- Generated from: dotnet ef migrations script --idempotent",
    "-- WARNING: Always back up your DB before running migration scripts.",
    ""
)
$cleaned = ($header -join "`n") + $cleaned

[System.IO.File]::WriteAllText($OutputFile, $cleaned)
Write-Host "Cleaned script written to: $OutputFile"
```

Run it:

```powershell
.\clean-migrations.ps1
```

This produces `migrations-clean.sql`.

### Step 4: Run in CockroachDB Cloud SQL Lab

1. Open **CockroachDB Cloud Console** → select your cluster → **SQL Lab**.
2. Make sure the database is set to `financedb` (top-left dropdown).
3. Copy the entire contents of `migrations-clean.sql` and paste into the SQL Lab editor.
4. Click **Run**.

> **Note:** If you get `ERROR: disallowed statement type XXUUU`, the SQL Lab does not allow `START TRANSACTION` / `COMMIT` statements. Remove those lines from the script (keep everything between them) and re-run.

### Step 5: Verify

Run this in SQL Lab to confirm:

```sql
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
```

You should see all migration IDs. Then:

```sql
SHOW TABLES;
```

You should see all expected tables: `ExpenseCategories`, `Expenses`, `IncomeCategories`, `Incomes`, `Receipts`, `Users`, `Roles`, `AspNet*` tables, etc.

---

## One-liner (local Docker DB only)

If you are updating the **local** Docker CockroachDB (not the cloud), use the `db-fresh.ps1` script instead:

```powershell
.\db-fresh.ps1
```

This script is idempotent and handles the DO-block stripping automatically. It does not work for CockroachDB Cloud.

---

## Troubleshooting

### `ERROR: unimplemented: CREATE TABLE usage inside a function definition is not supported`

The script still contains `DO $EF$` blocks. Re-run Step 3 to clean the script.

### `ERROR: disallowed statement type XXUUU`

SQL Lab blocked `START TRANSACTION` or `COMMIT`. Open `migrations-clean.sql`, delete all `START TRANSACTION;` and `COMMIT;` lines, and re-paste into SQL Lab.

### CockroachDB Cloud SQL Lab times out or is unavailable

Use the CockroachDB CLI from your local machine:

```powershell
cockroach sql --url "postgresql://musarrat:<PASSWORD>@weird-gorilla-7085.8nk.cockroachlabs.cloud:26257/financedb?sslmode=require" < migrations-clean.sql
```

Install the CLI from https://www.cockroachlabs.com/docs/cockroachcloud/install-cockroachdb.

### Duplicate key violation on `__EFMigrationsHistory`

The idempotent script uses `ON CONFLICT DO NOTHING`, so this should not happen. If it does, the migration was already partially applied. Query `__EFMigrationsHistory` to see which migrations are recorded, and manually apply any missing DDL statements.

---

## Security

- **The password in `FinanceManagerApi/CockroachConnect.txt` is compromised.** Rotate it in CockroachDB Cloud immediately and update any scripts that use it.
- **Never commit connection strings or passwords to the repo.** Use user-secrets for local dev and environment variables for deployed environments.
- For Render deployment, set `ConnectionStrings__CockroachDb` and `JWT__Secret` as environment variables in the Render dashboard — do not put them in `appsettings.json`.
