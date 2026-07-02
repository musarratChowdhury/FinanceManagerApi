# Local DB Setup & Secret Migration Plan

> Status: in progress, paused for PC restart.
> Owner: user. OpenCode agent will execute the file-edits steps on resume.

## Why this exists

- The API was reading `ConnectionStrings:CockroachDb` from the committed `appsettings.json`, which pointed at the production CockroachDB Cloud cluster (`weird-gorilla-7085.8nk.cockroachlabs.cloud`).
- The prod cluster is no longer reachable from the dev machine (TCP `26257` timeout to `35.240.243.36`).
- The committed `appsettings.json` also leaks the prod CockroachDB password and the `JWT:Secret` — these must be moved to user-secrets.
- Local dev should run against a single-node CockroachDB in Docker, with Docker's VHDX on D:\ to avoid filling the C:\ SSD.

## Decisions (already made)

| Question | Choice |
| --- | --- |
| Local DB image | **CockroachDB single-node** (matches prod) |
| How to ship the run command | **`docker-compose.dev.yml`** at repo root |
| Remove dead `DefaultConnection`? | **Yes** (unused MySQL config) |

## File changes to make on resume

### 1. NEW: `docker-compose.dev.yml` (repo root)

```yaml
services:
  cockroachdb:
    image: cockroachdb/cockroach:latest
    container_name: fmapi-cockroach
    command: start-single-node --insecure
    ports:
      - "26257:26257"
      - "8081:8081"
    volumes:
      - fmapi-cockroach-data:/cockroach/cockroach-data

volumes:
  fmapi-cockroach-data:
```

The named volume is owned by Docker — its physical storage follows the VHDX location set in Docker Desktop (D:\).

### 2. EDIT: `FinanceManagerApi/appsettings.json`

Replace the `ConnectionStrings` and `JWT` blocks.

Before:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Port=3306;Database=fmdb;Uid=muhit;Pwd=1234;",
  "CockroachDb": "Host=weird-gorilla-7085.8nk.cockroachlabs.cloud;Port=26257;Username=musarrat;Password=59CZlHkfuwQ6NHNdBWy6Fw;Database=financedb;SSL Mode=Require;Trust Server Certificate=true;"
},
"JWT": {
  "Secret": "vttuidhqptuitogmeumhyhwwcduznmzohqhrusbbbeuttwmrlyfuiewhkamawxqzaveuehlfiylwylesyiworsqzpsjuxglkuoqxwpnszarwedqpnlvfngkeaisixcmk",
  "Issuer": "http://localhost:5000",
  "Audience": "user",
  "AccessExpiration": 60,
  "RefreshExpiration": 60
},
```

After:
```json
"ConnectionStrings": {
  "CockroachDb": "Host=localhost;Port=26257;Username=root;Database=financedb"
},
"JWT": {
  "Secret": "REPLACE_VIA_USER_SECRETS",
  "Issuer": "http://localhost:5006",
  "Audience": "user"
},
```

Notes:
- `DefaultConnection` removed (dead MySQL config).
- `CockroachDb` is now a localhost default; `appsettings.Development.json` overrides it for dev. In prod, an env var or user-secrets overrides it.
- `JWT:Secret` is a placeholder; real value goes into user-secrets.
- `JWT:Issuer` corrected from `http://localhost:5000` to `http://localhost:5006` to match the actual dev launch profile (AGENTS.md flags this as a known mismatch).
- `AccessExpiration` / `RefreshExpiration` dropped — AGENTS.md flags these as never consumed (the controller hardcodes 3 hours).

### 3. EDIT: `FinanceManagerApi/appsettings.Development.json`

Before:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Port=3306;Database=fmdb;Uid=muhit;Pwd=1234;"
}
```

After:
```json
"ConnectionStrings": {
  "CockroachDb": "Host=localhost;Port=26257;Username=root;Database=financedb"
},
"JWT": {
  "Secret": "dev-only-secret-do-not-use-in-prod-must-be-32-chars-or-more"
}
```

Notes:
- `DefaultConnection` removed.
- `CockroachDb` points at the local Docker container.
- Dev `JWT:Secret` is long enough to satisfy the HS256 minimum key length in .NET 10.

### 4. EDIT: `AGENTS.md`

Add a new section after "Common commands":

```markdown
## Local DB

- Start: `docker compose -f docker-compose.dev.yml up -d` (creates the `fmapi-cockroach` container, listens on `localhost:26257`).
- Create database (one-time): `docker exec -it fmapi-cockroach ./cockroach sql --insecure -e "CREATE DATABASE IF NOT EXISTS financedb;"`.
- Apply migrations: `dotnet ef database update --project FinanceManagerApi`.
- Stop: `docker compose -f docker-compose.dev.yml down`. Data persists in the `fmapi-cockroach-data` named volume.
- Docker Desktop's VHDX is configured to live on D:\ (Resources → Advanced → Disk image location). Verify before pulling any large images.
- The base `appsettings.json` contains a placeholder `CockroachDb` (localhost) and `JWT:Secret` (`REPLACE_VIA_USER_SECRETS`). For prod / remote deployments, override via:
  - `dotnet user-secrets set "ConnectionStrings:CockroachDb" "..." --project FinanceManagerApi`
  - `dotnet user-secrets set "JWT:Secret" "..." --project FinanceManagerApi`
  - or env vars `ConnectionStrings__CockroachDb` / `JWT__Secret`.
```

Also update the "Gotchas" bullet about committed secrets to reflect that they have been removed from the committed file and live in user-secrets instead.

## Manual steps (user does these, not the agent)

### A. Install Docker Desktop

1. Download from https://www.docker.com/products/docker-desktop/
2. Run the installer (admin required). WSL2 backend is enabled automatically.
3. Restart Windows when prompted.

### B. Move Docker VHDX to D:\

1. Open Docker Desktop → Settings → Resources → Advanced.
2. Change **Disk image location** to e.g. `D:\DockerData\wsl\data\ext4.vhdx`.
3. Click **Apply**. Docker restarts and migrates the VHDX.
4. Verify the old VHDX is gone from `C:\Users\<you>\AppData\Local\Docker\wsl\data\` — if it's still there, free the space manually (see Troubleshooting below).

### C. Start the local DB

```powershell
docker compose -f docker-compose.dev.yml up -d
```

### D. Apply migrations

```powershell
./db-fresh.ps1
```

The script is idempotent. It ensures `financedb` exists, generates an idempotent SQL script from the EF Core migrations, and applies it through the container's `cockroach sql`. Re-run it any time after a fresh `git pull` to bring the schema up to date. The generated `.ef-migrations.sql` is created at the repo root during the run and removed on success; it stays on disk (and the script exits non-zero) if the apply fails, so you can inspect it.

### E. Run the API

```powershell
dotnet run --project FinanceManagerApi
```

Visit `http://localhost:5006/swagger` and exercise `POST /api/Authentication/register` + `POST /api/Authentication/login` to confirm a round-trip.

### F. (When deploying to prod) Set real secrets via user-secrets

```powershell
dotnet user-secrets set "ConnectionStrings:CockroachDb" "Host=weird-gorilla-7085.8nk.cockroachlabs.cloud;Port=26257;Username=musarrat;Password=<real-pw>;Database=financedb;SSL Mode=Require;Trust Server Certificate=true;" --project FinanceManagerApi
dotnet user-secrets set "JWT:Secret" "<real-secret>" --project FinanceManagerApi
```

The csproj already declares `<UserSecretsId>c079809c-038e-401e-b5af-38011dd5f18e</UserSecretsId>`. User secrets live in `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` and are never committed.

## Verification checklist

- [ ] `docker ps` shows `fmapi-cockroach` running and healthy.
- [ ] `./db-fresh.ps1` exits with code 0.
- [ ] `docker exec fmapi-cockroach ./cockroach sql --insecure -e "SHOW DATABASES;"` lists `financedb`.
- [ ] `docker exec fmapi-cockroach ./cockroach sql --insecure -d financedb -e "SHOW TABLES;"` lists the EF + Identity tables (e.g. `Expenses`, `ExpenseCategories`, `Incomes`, `IncomeCategories`, `Receipts`, `AspNetUsers`, `AspNetRoles`, `__EFMigrationsHistory`).
- [ ] Re-running `./db-fresh.ps1` is a no-op (idempotent script skips already-applied migrations).
- [ ] `dotnet run --project FinanceManagerApi` starts without throwing the previous `IdentityPasskeyData` / `IdentityUserPasskey<string>` model errors.
- [ ] `GET http://localhost:5006/swagger` returns 200.
- [ ] `POST /api/Authentication/register` with a fresh username returns 200.
- [ ] `POST /api/Authentication/login` with that user returns 200 + JWT.
- [ ] `git grep -n "59CZlHkfuwQ6NHNdBWy6Fw" FinanceManagerApi/` returns no matches (committed secret is gone).

## Troubleshooting

### Docker VHDX not migrating automatically

If Docker Desktop doesn't move the VHDX on Apply, you can move the WSL2 distro manually:

```powershell
wsl --shutdown
wsl --export docker-desktop-data "D:\DockerData\docker-desktop-data.tar"
wsl --unregister docker-desktop-data
wsl --import docker-desktop-data "D:\DockerData\data" "D:\DockerData\docker-desktop-data.tar"
```

Then restart Docker Desktop. The `ext4.vhdx` will be recreated on the new path.

### Port 26257 already in use

If you had a prior local CockroachDB on a different Docker host, kill it: `docker rm -f fmapi-cockroach` then re-run the compose.

### `dotnet ef` complains it can't reach the DB

The error will tell you `localhost:26257` is unreachable. Confirm with:

```powershell
Test-NetConnection -ComputerName localhost -Port 26257
```

If that fails, the container is down. `docker compose -f docker-compose.dev.yml ps` to check.

### JWT signing key error at startup

`IDX10720` or similar — `JWT:Secret` is too short for HS256 in .NET 10. The dev secret in `appsettings.Development.json` is intentionally long; do not shorten it.

### Want to migrate manually without `db-fresh.ps1`

The script wraps two steps. Equivalent one-liner:

```powershell
docker exec fmapi-cockroach ./cockroach sql --insecure -e "CREATE DATABASE IF NOT EXISTS financedb;"
dotnet ef migrations script --idempotent --project FinanceManagerApi --output .ef-migrations.sql
Get-Content -Raw .\.ef-migrations.sql | docker exec -i fmapi-cockroach ./cockroach sql --insecure -d financedb
Remove-Item .\.ef-migrations.sql
```

### Why `dotnet ef database update` is not used

`database update` boots the EF design-time host and runs each migration's `Up` through the Npgsql provider against a live connection. On this project that path is unreliable (provider / design-time model quirks). The script approach (`migrations script --idempotent` → emit SQL → `cockroach sql`) only needs the model to compile, and `cockroach sql` is a normal Postgres-protocol client. If `db-fresh.ps1` itself fails, the generated `.ef-migrations.sql` stays on disk so you can inspect or hand-apply the SQL.

### Why the generated SQL is post-processed (CockroachDB DO-block limitation)

The Npgsql EF Core provider wraps every DDL statement in a PL/pgSQL anonymous block:

```sql
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = 'X') THEN
    CREATE TABLE "..." (...);
    END IF;
END $EF$;
```

CockroachDB does **not** support DDL inside `DO` blocks — it returns `unimplemented: CREATE TABLE usage inside a function definition is not supported` (see CockroachDB issue #110080). The script post-processes the generated SQL to strip the `DO` wrappers, then applies each migration's DDL statements individually through `cockroach sql`. The script also maintains `__EFMigrationsHistory` itself: before applying a migration, it checks whether the row already exists, and on success it inserts the row. Re-runs are idempotent.

### Want to point the API at a different DB later

Env vars override everything. From PowerShell:

```powershell
$env:ConnectionStrings__CockroachDb = "Host=...;Port=...;Username=...;Password=...;Database=...;SSL Mode=Require;Trust Server Certificate=true;"
dotnet run --project FinanceManagerApi
```

For bash:

```bash
ConnectionStrings__CockroachDb="Host=...;..." dotnet run --project FinanceManagerApi
```

## OpenCode resume prompt (paste when picking this back up)

```
Resume LOCAL_DB_SETUP.md. Run all four file edits listed in "File changes to make on resume", then stop. Do not run docker / dotnet commands — those are manual steps the user does after the file edits.
```
