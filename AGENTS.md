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

- **Dockerfile base images are .NET 7** (`mcr.microsoft.com/dotnet/aspnet:7.0` / `sdk:7.0`) while `FinanceManagerApi.csproj` targets `net10.0`. Building the image against the current csproj will fail or pull the wrong runtime. Update the `FROM` tags to `8.0`/later (and re-test) before touching Docker.
- **Committed secrets removed.** The prod `CockroachDb` password and `JWT:Secret` have been moved out of `FinanceManagerApi/appsettings.json` into user-secrets (id `c079809c-038e-401e-b5af-38011dd5f18e`). `FinanceManagerApi/CockroachConnect.txt` still contains a copy of the prod connection string with the real password — treat it as compromised and rotate the password at Cockroach Cloud when convenient.
- **JWT lifetime is hardcoded** to 3 hours in `AuthenticationController.GenerateJWTTokenAsync` (`DateTime.Now.AddHours(3)`). The `AccessExpiration`/`RefreshExpiration` values in `appsettings.json` are not actually consumed.
- **Password policy is permissive**: 6 chars, no complexity requirements (see `IdentityOptions` in `Program.cs`). Don't add new password complexity without confirming with the maintainer.
- **CORS is fully open** (`AllowAnyOrigin/Method/Header`, policy name `AllowSites`). Acceptable for the current deployment behind nginx, but don't propagate this pattern to new surfaces.
- **`WeatherForecast.cs` and `FinanceManagerApi.http` are leftover scaffolding** from the ASP.NET template. They are not wired into `Program.cs`. Removing them is fine but not required.

## Architecture notes

- Layered: Controllers → Services (`ExpenseService`, `IncomeService`, `ReceiptService`) → `GenericRepository<TEntity>` → `FinanceDbContext` (in `DbContext/AppDbContext.cs`, note: the namespace root is `FinanceManagerApi.DbContext` but the file is `AppDbContext.cs`).
- DI in `Program.cs` registers `IGenericRepository<>` (open generic), `IExpenseService`, `IReceiptService` as scoped. `IncomeService` is **not** registered in DI even though `IncomeController` exists — adding a route that hits it will throw at request time.
- Auth: ASP.NET Identity (`UserProfile : IdentityUser`) + JWT bearer. Endpoints under `Controllers/AuthenticationController.cs` (`api/Authentication/register`, `api/Authentication/login`). All other controllers are decorated `[Authorize]`.
- Error handling: `ExceptionMiddleware` (registered first in the pipeline) serializes unhandled exceptions to camelCase `ApiExceptionResponse` JSON. In Development the response includes the stack trace.
- AutoMapper profiles live in `FinanceManagerApi/Profiles/`. `AddAutoMapper` scans the whole assembly, so new profile classes are picked up automatically.
- Entity relationships are configured in `FinanceDbContext.OnModelCreating` (Expense ↔ ExpenseCategory, Income ↔ IncomeCategory, Expense ↔ Receipt). Identity join tables are flagged `HasNoKey()` — keep this when adding new identity-related entities.
- Static files served from `FinanceManagerApi/wwwroot/`; `GET /` redirects to `/index.html`. Swagger UI is only mounted in Development.

## Conventions

- Entities in `Models/Entity/`, DTOs in `Models/DTO/`, error/response shapes in `Models/Errors/`.
- Controllers are under `[Authorize]` by default; authentication-free endpoints must be added to `AuthenticationController` or opted out explicitly.
- `commands.txt` at repo root documents the manual AWS/EC2 deploy dance (`docker-compose down` → `docker-compose up` after `git pull`). Use it as the source of truth for deploy steps — there is no CI.
- `db-fresh.ps1` at repo root — local DB migration script (see "Local DB" section).

## Reference

- `commands.txt` — manual deploy + EC2 disk-cleanup commands.
- `FinanceManagerApi.postman_collection.json` — local API exercise collection.
- `nginx.conf` — reverse proxy to `financemanagerapi:80`; TLS block is commented out pending Let's Encrypt issuance.
- `docker-compose.yml` — local stack: `financemanagerapi` + `nginx` + `certbot` on `app-network`.
- `docker-compose.dev.yml` — local single-node CockroachDB (this repo's dev DB).
- `docker-compose.prod.yml` — single-container prod compose using the published image.
