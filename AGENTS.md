# AGENTS.md

Quick reference for working in this repo. Keep it terse — only facts an agent is likely to miss.

## Repo shape

- .NET solution `FinanceManagerApi.sln` with two projects:
  - `FinanceManagerApi/` — ASP.NET Core 10 Web API (`net10.0`).
  - `FinanceManagerApi.Moq/` — xUnit test project (the "Moq" name refers to the Moq mocking library, not the project being a mock of the API). References the API project.
- EF Core 10 + Npgsql (PostgreSQL wire). Migrations live in `FinanceManagerApi/Migrations/`.

## Common commands

Run from repo root unless noted.

- Build: `dotnet build FinanceManagerApi.sln`
- Run API locally: `dotnet run --project FinanceManagerApi` (uses `Properties/launchSettings.json` — HTTP profile binds to `http://localhost:5006`, HTTPS profile to `https://localhost:7219`).
- Run tests: `dotnet test FinanceManagerApi.sln` (only `ExpenseServiceTests` exists today).
- Add EF migration: `dotnet ef migrations add <Name> --project FinanceManagerApi` (connection string must be reachable).
- Docker build (local): `docker build -t financemanagerapi .`
- Full local stack (API + nginx + certbot): `docker-compose up`
- Prod image: `musarratchowdhury/financemanagerapi` (see `docker-compose.prod.yml`, exposed on `http://0.0.0.0:8000`; domain `fmapi.muhit.dev`).

## Local DB

- Start: `docker compose -f docker-compose.dev.yml up -d` (creates the `fmapi-cockroach` container, listens on `localhost:26257`).
- Apply migrations: `./db-fresh.ps1` (idempotent — creates `financedb` if missing, generates an idempotent SQL script from EF migrations, applies it via `cockroach sql`). Re-run after any `git pull` that changes `FinanceManagerApi/Migrations/`. Do **not** use `dotnet ef database update` here — see `LOCAL_DB_SETUP.md` troubleshooting for why the script approach is used.
- Stop: `docker compose -f docker-compose.dev.yml down`. Data persists in the `fmapi-cockroach-data` named volume.
- Docker Desktop's VHDX is configured to live on D:\ (Resources → Advanced → Disk image location). Verify before pulling any large images.
- The base `appsettings.json` contains a placeholder `CockroachDb` (localhost) and `JWT:Secret` (`REPLACE_VIA_USER_SECRETS`). For prod / remote deployments, override via:
  - `dotnet user-secrets set "ConnectionStrings:CockroachDb" "..." --project FinanceManagerApi`
  - `dotnet user-secrets set "JWT:Secret" "..." --project FinanceManagerApi`
  - or env vars `ConnectionStrings__CockroachDb` / `JWT__Secret`.

## Gotchas that will burn you

- **The Dockerfile is on .NET 10** (`mcr.microsoft.com/dotnet/aspnet:10.0` / `sdk:10.0`), matching `net10.0` in `FinanceManagerApi.csproj`. It used to be .NET 7, which broke the image build — if you ever see "the image pulls the wrong runtime", check the `FROM` tags first.
- **Committed secrets removed.** The prod `CockroachDb` password and `JWT:Secret` have been moved out of `FinanceManagerApi/appsettings.json` into user-secrets (id `c079809c-038e-401e-b5af-38011dd5f18e`). `FinanceManagerApi/CockroachConnect.txt` still contains a copy of the prod connection string with the real password — treat it as compromised and rotate the password at Cockroach Cloud when convenient.
- **The credential copies in this repo have drifted apart.** `FinanceManagerApi/CockroachConnect.txt` holds a **stale** password — the same stale copy that broke the CI pipeline (SQLSTATE 28P01) on 2026-10-19, and the GitHub secret had to be updated on 2026-10-22. The checked-in `appsettings.json` has only a localhost placeholder, and the user-secrets are **not configured on this machine**. The only current copy left on disk is in an old build output (`FinanceManagerApi/bin/Debug/net7.0/appsettings.json`). Use `scripts/check-sequence.py`, which tries each source and uses the first that authenticates, and rotate both passwords at Cockroach Cloud.
- **JWT lifetime is hardcoded** to 3 hours in `AuthenticationController.GenerateJWTTokenAsync` (`DateTime.Now.AddHours(3)`). The `AccessExpiration`/`RefreshExpiration` values in `appsettings.json` are not actually consumed.
- **Password policy is permissive**: 6 chars, no complexity requirements (see `IdentityOptions` in `Program.cs`). Don't add new password complexity without confirming with the maintainer.
- **CORS is fully open** (`AllowAnyOrigin/Method/Header`, policy name `AllowSites`). Acceptable for the current deployment behind nginx, but don't propagate this pattern to new surfaces.
- **`WeatherForecast.cs` and `FinanceManagerApi.http` are leftover scaffolding** from the ASP.NET template. They are not wired into `Program.cs`. Removing them is fine but not required.
- **`gemma-4-26b-a4b-it` needs 9-12 s per receipt.** Measured against the bundled 238 KB probe image: **12.2 s cold**, 8.6 s and 9.1 s warm. `Gemini:AttemptTimeoutSeconds` defaults to 10, so the first, cold attempt can time out and the sweep silently falls through to the next model. Raise it to 15 if cold starts must pass — the probe and a real scan share the same budget.
- **Don't read `GEMINI_API_KEY` with `Environment.GetEnvironmentVariable`.** `WebApplication.CreateBuilder` already layers environment variables into `IConfiguration`, so a direct read is redundant *and* makes in-memory-config unit tests depend on the developer's machine (the key is set locally, so a "missing key" test passes for the wrong reason). Configuration is the single source of truth.
- **`HttpClient` in the unit tests must not be disposed** (`using var client = new HttpClient(handler)`): disposing it disposes the handler, and any later call through the returned service fails with `ObjectDisposedException`.
- **The repository's include overload is `Filter(predicate, params string[])`.** A Moq setup whose callback is typed `<…, string>` for a single include throws at setup time ("cannot invoke callback with parameters (…, string)") — use `Callback<…, string[]>`.
- **`ReceiptDto.HasImage` is a plain settable property, not a computed getter.** AutoMapper cannot assign a read-only member, so the profile maps it with `MapFrom(r => r.Images.Count > 0)`; `Images` is `Ignore()`d on the DTO→entity map so a client cannot inject image rows.

## Architecture notes

- Layered: Controllers → Services (`ExpenseService`, `IncomeService`, `ReceiptService`) → `GenericRepository<TEntity>` → `FinanceDbContext` (in `DbContext/AppDbContext.cs`, note: the namespace root is `FinanceManagerApi.DbContext` but the file is `AppDbContext.cs`).
- DI in `Program.cs` registers `IGenericRepository<>` (open generic), `IExpenseService`, `IReceiptService` as scoped. `IncomeService` is **not** registered in DI even though `IncomeController` exists — adding a route that hits it will throw at request time.
- Auth: ASP.NET Identity (`UserProfile : IdentityUser`) + JWT bearer. Endpoints under `Controllers/AuthenticationController.cs` (`api/Authentication/register`, `api/Authentication/login`). All other controllers are decorated `[Authorize]`.
- Error handling: `ExceptionMiddleware` (registered first in the pipeline) serializes unhandled exceptions to camelCase `ApiExceptionResponse` JSON. In Development the response includes the stack trace.
- AutoMapper profiles live in `FinanceManagerApi/Profiles/`. `AddAutoMapper` scans the whole assembly, so new profile classes are picked up automatically.
- Entity relationships are configured in `FinanceDbContext.OnModelCreating` (Expense ↔ ExpenseCategory, Income ↔ IncomeCategory, Expense ↔ Receipt). Identity join tables are flagged `HasNoKey()` — keep this when adding new identity-related entities.
- Static files served from `FinanceManagerApi/wwwroot/`; `GET /` redirects to `/index.html`. Swagger UI is only mounted in Development.

## Receipt scanning (Gemini)

- `Services/GeminiReceiptScanService.cs` owns all Gemini calls: `ScanAsync(stream, mime, model?, strict, ct)`, `ListModelsAsync` (paged, cached 10 min) and `ProbeAsync` (the availability sweep, per-model result TTLs).
- `ReceiptController` exposes `POST api/Receipt/Scan` (multipart: `file`, `model`, `strict`), `GET api/Receipt/Models` (unfiltered list + `supportsGenerateContent`) and `POST api/Receipt/Probe`.
- A scan that exhausts the model chain throws `GeminiScanException`; the controller turns it into a **502** `{ message, attempts[] }`. Never let it reach `ExceptionMiddleware` — that rewrites it to "An error occurred, please try again later" and throws away the only actionable part.
- Config: `Gemini:DefaultModel` (`gemma-4-26b-a4b-it`), `Gemini:AttemptTimeoutSeconds` (10), `Gemini:ProbeConcurrency` (4). The key is `GEMINI_API_KEY`, read from configuration.
- Availability has three gates: `supportedGenerationMethods` must contain `generateContent`; a name heuristic rejects tts/image/embedding/aqa/imagen/veo/live; then `Samples/receipt-probe.jpg` proves vision. The models list never declares vision support, so only the probe can tell a text-only model from a vision one.
- The chain advances past any failed model except `auth_error` and `quota`, which rule out every model the key can reach.

## Saving a scanned receipt

- **`POST /api/Receipt` is the only correct way to persist a scanned receipt.** It
  takes the receipt header plus its `expenses[]` and writes them in **one
  `SaveChanges`**, so either the receipt and every line item land, or nothing does.
  EF assigns `Expenses.ReceiptId` from the principal during that save; the client
  never sends it and the controller nulls it out if it does.
- Returns **`201` with `Location: /api/Receipt/{id}`** and the stored receipt
  (including the generated id and each expense's `receiptId`). `GET /api/Receipt/{id}`
  returns `404` when the receipt does not exist.
- **Do not recreate the old per-line-item loop** (one `POST /api/Expense/Create` per
  item). It made partial saves the normal failure mode, could not attach a receipt,
  and a retry duplicated every row that had already succeeded.
- `Expense.Quantity` is **`double?`, not `int?`** — quantities are genuinely
  fractional (0.5 kg, 0.3 l). An `int` here makes the JSON binder reject the whole
  payload with a 400 that carries no useful message.
- `Receipt` carries `Merchant`, `InvoiceNo` (nullable — many receipts print none),
  `GrossTotal` (before discount), `Discount`, `NetPayable` (after discount),
  `TotalItems` and `ReceiptDateTime` (date **and** time). `GrandTotal`/`ExpenseDate`
  are gone; the migration renamed them in place so existing rows kept their values.
- Dates on the way in are re-pinned to UTC by `ReceiptService.PinDateKindsToUtc`
  (same Npgsql rule as expenses), so a client that sends `yyyy-MM-ddTHH:mm:ss.000Z`
  is safe and one that omits the offset cannot 500.

## Receipt photos (Backblaze B2)

One softcopy per receipt, stored in a private B2 bucket through its S3-compatible
API. `Services/Storage/B2ReceiptStorage.cs` owns the SDK; the endpoints live on
`ReceiptImageController`.

- **Config**: `B2:ServiceUrl`, `B2:Region`, `B2:KeyId`, `B2:AppKey`, `B2:Bucket`
  (env vars `B2__*` on Render, user-secrets locally). Empty `appsettings.json`
  values are deliberate — with no credentials the API still boots and only
  `/api/Receipt/{id}/image*` answers **503**.
- **Endpoints** (all ownership-checked, foreign receipt = **404** not 403):
  `POST /api/Receipt/{id}/image` (multipart `file`, upsert/replace),
  `GET …/image` (metadata), `GET …/image/url` (presigned, 5 min, clamped 60–3600 s),
  `DELETE …/image`.
- **One image per receipt** is enforced by a unique index on `ReceiptImages.ReceiptId`,
  and re-uploading replaces in place.
- **Validation is server-side**: magic-byte sniffing (JPEG/PNG/WebP only; SVG and
  PDF rejected), a 5 MB per-file cap read with a bounded buffer so `Content-Length`
  is never trusted, and a 200 MB per-user quota (`SUM(SizeBytes)` by `CreatedBy`).
- **Ordering matters**: upload → object → row (delete the object if the row write
  fails); delete → object → row (a store failure keeps the row so it can be
  retried). A future receipt-delete must delete objects first — EF cascade alone
  would orphan bytes.
- Keys are server-built (`receipts/{userId}/{receiptId}/{guid}.{ext}`); the client
  never sees the key, only a signed URL.
- **Never** log or commit `B2:KeyId`/`B2:AppKey`, and keep the bucket private.

Two SDK settings are load-bearing: `ForcePathStyle = true`, and
`RequestChecksumCalculation`/`ResponseChecksumValidation` at `WHEN_REQUIRED` —
the AWS SDK v4 defaults add checksums that B2 rejects.

## Conventions

- Entities in `Models/Entity/`, DTOs in `Models/DTO/`, error/response shapes in `Models/Errors/`.
- Controllers are under `[Authorize]` by default; authentication-free endpoints must be added to `AuthenticationController` or opted out explicitly.
- `commands.txt` at repo root documents the manual AWS/EC2 deploy dance (`docker-compose down` → `docker-compose up` after `git pull`). Use it as the source of truth for deploy steps — the CI/CD pipeline (below) is the newer path.
- `db-fresh.ps1` at repo root — local DB migration script (see "Local DB" section).

## CI/CD (`.github/workflows/ci-cd.yml`)

There **is** a pipeline — `commands.txt` predates it. On push to `main` (and on
`workflow_dispatch`): build → test → push the Docker image to
`musarratchowdhury/financemanagerapi` → generate the EF migration SQL → apply
it to CockroachDB → notify Render.

- Secrets: `COCKROACH_CONNECTION_STRING`, `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, `RENDER_DEPLOY_HOOK_URL` (optional — empty means Render polls Docker Hub on its own schedule, which takes a few minutes).
- **`RENDER_DEPLOY_HOOK_URL` is not set**, so a green CI run does not mean the API is live yet — Render picks up the new image on its own schedule.
- The step order matters: the Docker image is pushed **before** the migration step, so a failed migration leaves a new image on Docker Hub that Render may still auto-deploy.
- `dotnet ef migrations script --idempotent` regenerates the **whole** schema every run (not just pending migrations). The apply step is therefore made idempotent by rewriting the SQL — see the "CockroachDB migrations" section below.

## CockroachDB migrations (the apply step has three traps)

- **`cockroach sql --set=NAME=VALUE` configures CLIENT-SIDE options** (display_format, errexit, echo, …), *not* server session variables. `--set=statement_timeout=30s` fails with `ERROR: -e: unknown variable name: "statement_timeout"`. Set the timeout with SQL instead: `SET statement_timeout = '30s';`.
- **`dotnet ef migrations script --output` writes a UTF-8 BOM.** The cockroach CLI reads the file literally, so the BOM lands in front of the first statement and it dies with `syntax error`. The rewrite step reads with `encoding="utf-8-sig"` (strips it) and writes back with `encoding="utf-8"` (adds none).
- **`DO $EF$` guards cannot run, so the script drops applied migrations instead.** The CI reads `__EFMigrationsHistory` (`cockroach sql --format=csv --execute='SELECT "MigrationId" FROM "__EFMigrationsHistory"'`) and passes it to the rewrite as a third argument; every block whose migration is recorded there is removed, so only genuinely pending migrations run. The `IF [NOT] EXISTS` / `ON CONFLICT DO NOTHING` guards stay as a second layer for a migration that crashed part-way through its own statements.
- **CockroachDB has no `RENAME COLUMN IF EXISTS`** even though the ALTER TABLE docs list the clause under RENAME COLUMN: the server answers `syntax error at or near "exists"`. One-shot DDL like that is why the applied-migration list above exists - do not try to guard a rename with `IF EXISTS`.
- **No Windows cockroach binaries are published.** To exercise the CLI locally, run it in Docker: `docker run --rm -v "<dir>:/sql" cockroachdb/cockroach:v24.2.0 sql --url="postgresql://user:pass@host:26257/db?sslmode=require" --file=/sql/cockroach.sql`.
- A `.sql` file written by PowerShell `Out-File -Encoding utf8` carries a BOM too — write generated SQL with a BOM-less writer (`python` `write_text(..., encoding="utf-8")`).

## Budgets & goals

A category ceiling per month plus savings goals, both server-side.

- **One limit per (user, category)**, unique index `(CreatedBy, ExpenseCategoryId)`.
  The ceiling **carries forward** into every new month; the spend it is compared against is
  always computed for the month being viewed, so spending "resets" on the 1st and the limit
  does not. `EffectiveFrom` is informational only (drives "applies from November 2026
  onward") — there is deliberately **no per-month history**.
- `GET api/Budget/summary?month=&year=` returns the whole screen: totals, per-category rows,
  `projected`, `safeToSpendPerDay`, `daysInMonth/Elapsed/Remaining`. The maths lives in
  `BudgetService` (not the client) so it is unit-testable and identical everywhere:
  - `projected = daysElapsed > 0 ? spent / daysElapsed * daysInMonth : 0` (linear run-rate)
  - `safeToSpendPerDay = max(0, (limit - spent) / daysRemaining)` — leftovers over left-over days,
    not a pace penalty
  - `status` = `over` >100%, `near` ≥80%, `onTrack` otherwise, `noBudget` when the limit is 0
  - month windows are UTC, so "days left" follows the server's UTC date
  - spend comes from `IExpenseService.GetExpensesOfAMonthAsync` (already user-scoped), so the
    ownership predicate is not duplicated
- `POST api/Budget` `{categoryId, monthlyLimit}` upserts; `DELETE api/Budget/{categoryId}` clears.
  Saving updates `MonthlyLimit`/`EffectiveFrom` only — ownership and the category id are server-owned.
- **Goals use a contribution ledger**: `SavingsGoal` stores name/target/targetDate only, and
  "saved so far" is `SUM(GoalContributions.Amount)`. A wrong entry is **deleted**, never edited,
  which is what keeps `avgSavedPerMonth` and the pace verdict honest.
  `api/Goal` supports list/get/create/update/delete plus
  `POST api/Goal/{id}/contribution` and `DELETE api/Goal/{id}/contribution/{contributionId}`.
  Opening balance is stored as the first contribution.
- **EF relationship fix-up already attaches a newly inserted contribution to the tracked goal.**
  `SavingsGoalService` therefore adds it to the loaded collection only when it is missing
  (`if (!goal.Contributions.Contains(contribution))`); an unconditional `Add` double-counts the
  amount in the response and in `AvgSavedPerMonth` (caught by `BudgetPersistenceTests`).
- Unit tests: `BudgetServiceTests`, `SavingsGoalServiceTests` (Moq) and `BudgetPersistenceTests`
  (real `FinanceDbContext` on the in-memory provider, so the `ExpenseCategory`/`Contributions`
  includes and the DTO mapping are exercised).

## Diagnosing a generic 500

`ExceptionMiddleware` returns `{"message":"An error occurred, please try again later"}`
in Production and **the full exception + stack trace in Development**. To see the
real cause, run the API locally in Development against the same database:

```
$env:ConnectionStrings__CockroachDb="<prod conn string>"
$env:JWT__Secret="<any local secret>"; $env:JWT__Issuer="http://localhost:5006"; $env:JWT__Audience="user"
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:5006"
dotnet run --project FinanceManagerApi --no-launch-profile
```

`DbInitializer` only seeds when the tables are empty, so pointing it at the prod
database is safe (it prints "already exists - skipping"). Log in through
`POST /api/Authentication/login` to get a token with your own secret, then
reproduce the failing call. The EF/Npgsql command and the inner exception are in
the console output.

## Npgsql / date handling

- **Npgsql (6+) refuses to write a `DateTime` whose `Kind` is not `Utc` into a `timestamp with time zone` column** → `ArgumentException: Cannot write DateTime with Kind=Unspecified …`. A JSON date-only string (`"2026-10-22"`) deserialises with `Kind=Unspecified`, so any client sending a bare date gets a 500. `ExpenseService.NormalizeDateKinds` pins an Unspecified `ExpenseDate`/`EntryDate` to UTC before saving — keep that, and normalise new date-carrying services the same way.
- `Expense.EntryDate` is `DateTime.UtcNow` (Utc) but `Income.EntryDate` and `IncomeCategory.EntryDate` are `DateTime.Now` (**Local**) — Npgsql rejects Local for `timestamptz` too. Change them to `UtcNow` if those tables are ever written.

## CockroachDB schema notes

- `Expenses.CreatedBy` is a plain `uuid` column with **no foreign key** — the ownership check is done in code (`x.CreatedBy == _currentUser.UserId`). `Expenses.ExpenseCategoryId` → `ExpenseCategories.Id` and `Expenses.ReceiptId` → `Receipts.Id` **do** have FKs, so a bad id fails the insert.
- **Sequences can drift behind `max(Id)`** — a failed insert still consumes the sequence on some paths, and CockroachDB's `SERIAL`/`IDENTITY` columns will then raise a duplicate-key error on the next insert. Symptom: `POST /api/Expense/Create` returns 500 while reads work. Fix:
  `SELECT setval('"Expenses_Id_seq"', (SELECT COALESCE(MAX("Id"),0) FROM "Expenses") + 1, false);`
  A sequence *ahead* of `max(Id)` is harmless (just id gaps); behind it is the failure mode.
- Inspect the cluster directly with the `cockroachdb-cloud` MCP (`list_clusters`, `get_cluster`) or `psycopg2` against the same connection string.

## Project scripts (`scripts/`) — reuse these, don't recreate them

- **`scripts/rewrite-migrations.py`** - the EF → CockroachDB SQL rewrite. **The CI workflow calls this** (`python3 scripts/rewrite-migrations.py sql/pending.sql sql/cockroach.sql sql/applied.csv`); edit the script, not the workflow YAML. It drops the blocks of migrations already in `__EFMigrationsHistory` (third argument, CSV), unwraps the remaining `DO $EF$` blocks, strips the `dotnet ef` UTF-8 BOM, adds `IF [NOT] EXISTS` / `ON CONFLICT DO NOTHING` where the statement supports it, and prepends `SET statement_timeout`. Stdlib only, so it runs on the CI runner unchanged.
  ```
  dotnet ef migrations script --project FinanceManagerApi/FinanceManagerApi.csproj --startup-project FinanceManagerApi/FinanceManagerApi.csproj --idempotent --output sql/pending.sql
  python scripts/rewrite-migrations.py sql/pending.sql sql/cockroach.sql
  ```
- **`scripts/check-sequence.py`** — sequence-drift detector for every table. Prints `max(Id)` vs the sequence's next value and the `setval` fix; `--fix` applies it.
  ```
  python scripts/check-sequence.py          # report
  python scripts/check-sequence.py --fix    # repair
  ```
  It tries several credential sources and uses the first that authenticates, because **the copies of the password in this repo drift** — `FinanceManagerApi/CockroachConnect.txt` holds a **stale** password (the same stale copy that broke the CI pipeline). It also prints which source it used.
- **`scripts/probe-api.py`** — probes the live API with both date formats (`yyyy-MM-dd` and `yyyy-MM-ddT00:00:00.000Z`) and deletes the rows it creates. Tells you whether the deployed API normalises an Unspecified date yet.
  ```
  python scripts/probe-api.py --api https://fmapi.muhit.dev
  ```
- **`scripts/run-local-dev.ps1`** — starts the API in **Development** mode so a 500 returns the real exception and stack trace instead of "An error occurred, please try again later".
  ```
  ./scripts/run-local-dev.ps1
  ```

## Project skills (`.opencode/skills/`)

- **`api-500-triage`** — get the real exception behind the generic 500: run the API locally in Development against the real database, read the Npgsql/EF inner exception, inspect the schema, fix sequence drift.
- **`cockroach-ef-migrations`** — generate and apply EF migrations to CockroachDB safely: the `--set` client-option trap, the UTF-8 BOM from `dotnet ef`, rewriting EF DDL blocks into idempotent CockroachDB SQL, verifying locally in Docker.
- **`api-deployment`** — the manual Docker Hub → Render path. Prefer the CI/CD pipeline on push to `main`.

Load them with the `skill` tool by name.

## Reference

- `commands.txt` — manual deploy + EC2 disk-cleanup commands.
- `FinanceManagerApi.postman_collection.json` — local API exercise collection.
- `nginx.conf` — reverse proxy to `financemanagerapi:80`; TLS block is commented out pending Let's Encrypt issuance.
- `docker-compose.yml` — local stack: `financemanagerapi` + `nginx` + `certbot` on `app-network`.
- `docker-compose.dev.yml` — local single-node CockroachDB (this repo's dev DB).
- `docker-compose.prod.yml` — single-container prod compose using the published image.
