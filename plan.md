# Plan: Deploy FinanceManagerApi to Render (Cockroach Cloud DB)

## Goal

Deploy this ASP.NET Core 10 Web API to Render, using the existing
Cockroach Cloud cluster as the production database. Keep all secrets out of
source control. No code changes required for the deploy itself.

## Decisions

| Topic | Choice |
|---|---|
| Production DB | Cockroach Cloud (existing external cluster) |
| Migrations | Render Release Command (`dotnet ef database update`) |
| Infra as code | Manual dashboard config (no `render.yaml`) |
| Render environment | Native `.NET` buildpack (skip Dockerfile) |

### Why native .NET instead of Docker on Render

The repo's `Dockerfile` still uses `mcr.microsoft.com/dotnet/aspnet:7.0` /
`sdk:7.0` base images while `FinanceManagerApi.csproj` targets `net10.0`.
That mismatch is tracked in `AGENTS.md` as a known gotcha. Render's native
.NET environment supports `net10.0` out of the box, so the cleanest
path is to skip Docker on Render and let the platform build with
`dotnet`. The existing Dockerfile can be fixed separately for the
EC2 / local Docker path.

## Render dashboard setup

### 1. Create the Web Service

- New -> Web Service -> connect this GitHub repo.
- Environment: **`.NET`** (not Docker).
- Region: same region as the Cockroach Cloud cluster.
- Branch: `main` (or the deploy branch).
- Build Command:
  ```
  dotnet publish FinanceManagerApi -c Release -o /app/publish
  ```
- Start Command:
  ```
  dotnet /app/publish/FinanceManagerApi.dll
  ```
  Render's .NET env often auto-injects this; verify in the preview.
- Plan: Free (cold starts) or Starter (always-on, recommended for prod).

### 2. Environment variables

On the service -> **Environment** tab, add:

| Key | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__CockroachDb` | Cockroach Cloud **external** connection string |
| `JWT__Secret` | Fresh 32+ char random value |
| `JWT__Issuer` | `https://<your-service>.onrender.com` |
| `JWT__Audience` | `user` |

Notes:
- Double underscore (`__`) in env var keys is the .NET convention for
  nested config paths. It maps to `ConnectionStrings:CockroachDb` in
  `IConfiguration`. A single `_` will not work.
- Generate `JWT__Secret` with `openssl rand -base64 48` or similar.
  Do **not** reuse the local dev secret from user-secrets.
- Cockroach Cloud free-tier certs are signed by Let's Encrypt; start
  with `SSL Mode=VerifyFull`. If Npgsql rejects the cert chain, add
  `Trust Server Certificate=true` to the connection string.

Example Cockroach Cloud connection string shape:
```
Host=<host>;Port=26257;Username=<user>;Password=<pw>;Database=financedb;SSL Mode=VerifyFull
```

### 3. Release Command (runs migrations after build, before traffic)

Service -> Settings -> Build & Deploy -> Release Command:
```
dotnet tool install --global dotnet-ef --version 10.* && export PATH="$PATH:$HOME/.dotnet/tools" && dotnet ef database update --project FinanceManagerApi
```

The tool install is cached after the first deploy. If `dotnet-ef` is
already pinned, drop the install line.

### 4. Smoke test after first deploy

- `GET https://<service>.onrender.com/` -> 200/redirect to `/index.html`.
- Swagger is **not** mounted in Production (per `AGENTS.md`).
- `POST /api/Authentication/register` with a test user -> 200.
- `POST /api/Authentication/login` -> returns JWT.
- `GET /api/Expense` with the JWT -> 200 (verifies DB connectivity).

## File changes in this plan

**None.** This plan is pure Render dashboard configuration.

Optional follow-ups (not part of this change, tracked for later):
- Bump `Dockerfile` base images from `7.0` to `8.0` (or whatever matches
  `net10.0`) for the EC2 / local Docker path.
- Add a "Render deploy" section to `AGENTS.md` so the next agent knows
  the env var keys and Release Command.
- Rotate the Cockroach Cloud password: `FinanceManagerApi/CockroachConnect.txt`
  still contains the old prod password in plaintext (see `AGENTS.md`).

## Risks / things to watch

- **Cold starts (Free plan):** first request after ~15 min idle takes
  ~30s. Acceptable for personal use; consider Starter for always-on.
- **Cockroach Cloud free cluster pauses** after ~5 min of no
  connections. First Render request after idle will trigger a resume
  and may time out. Options: upgrade to a paid Cockroach tier, or add
  an external uptime ping.
- **Migration drift:** any local EF migrations not yet committed will
  apply on first Render deploy. Make sure `FinanceManagerApi/Migrations/`
  is committed and pushed before deploying.
- **JWT secret rotation:** setting `JWT__Secret` on Render invalidates
  every token already issued. Coordinate with the frontend or accept
  a one-time re-login.
- **CORS:** `Program.cs` registers an `AllowSites` policy with
  `AllowAnyOrigin/Method/Header`. Fine behind Render's HTTPS, but worth
  locking down to the real frontend domain as a follow-up.
- **IncomeController is not wired into DI** (see `AGENTS.md`). Hitting
  any `/api/Income` route will throw at request time. Out of scope for
  this plan, but worth fixing before the frontend starts using income
  endpoints.

## Pre-deploy checklist

- [ ] `FinanceManagerApi/Migrations/` committed and pushed.
- [ ] Cockroach Cloud cluster running and reachable from Render's
      region (test with `psql` or the Cockroach Cloud SQL shell).
- [ ] Fresh `JWT__Secret` generated and saved somewhere safe.
- [ ] `FinanceManagerApi/CockroachConnect.txt` password rotated
      (recommended, not blocking).
