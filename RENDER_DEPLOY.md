# Render Deployment Guide

> Deploy FinanceManagerApi to Render using Docker (pushed to Docker Hub), with Cockroach Cloud as the production database.

## Prerequisites

### 1. Push Docker Image to Docker Hub

Build and push the image from the repo root:

```bash
# Build
docker build -t musarratchowdhury/financemanagerapi:latest -f FinanceManagerApi/Dockerfile .

# Push latest
docker push musarratchowdhury/financemanagerapi:latest

# Optional: tag a versioned release
docker tag musarratchowdhury/financemanagerapi:latest musarratchowdhury/financemanagerapi:v1
docker push musarratchowdhury/financemanagerapi:v1
```

### 2. Cockroach Cloud Cluster

- Ensure your CockroachDB Cloud cluster is running and reachable from Render's selected region.
- Have the full connection string ready (host, port, username, password, database name).

### 3. JWT Secret

Generate a fresh 32+ character secret:

```bash
openssl rand -base64 48
```

Store it somewhere safe (e.g., a password manager) — you will need it for the Render environment variable.

---

## Render Dashboard Setup

### 1. Create the Web Service

- Navigate to [Render Dashboard](https://dashboard.render.com/)
- Click **New +** → **Web Service**
- Connect your GitHub repo (`musarratChowdhury/FinanceManagerApi`)
- Branch: `main`

### 2. Configure the Service

| Setting | Value |
|---|---|
| **Environment** | `Docker` |
| **Docker Image** | `musarratchowdhury/financemanagerapi:latest` |
| **Region** | Same region as your CockroachDB Cloud cluster |
| **Instance Type** | Free (or Starter for always-on) |

### 3. Set Environment Variables

Go to **Environment** tab and add:

| Key | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__CockroachDb` | `Host=<host>.cockroachlabs.cloud;Port=26257;Username=<user>;Password=<password>;Database=financedb;SSL Mode=Require;Trust Server Certificate=true;` |
| `JWT__Secret` | `<fresh 32+ char secret>` |
| `JWT__Issuer` | `https://<your-service-name>.onrender.com` |
| `JWT__Audience` | `user` |

> **Note:** Double underscore (`__`) in env var keys maps to nested `.NET` config paths (e.g., `ConnectionStrings__CockroachDb` → `ConnectionStrings:CockroachDb`). Single underscore will not work.

### 4. Set Release Command

Go to **Build & Deploy** → **Release Command**:

```bash
dotnet tool install --global dotnet-ef --version 10.* && export PATH="$PATH:$HOME/.dotnet/tools" && dotnet ef database update --project FinanceManagerApi
```

This runs after the container is built but before receiving traffic, applying any pending EF Core migrations.

### 5. Deploy

Click **Create Web Service**. Render will pull the Docker image, set environment variables, run the release command, and start the service.

---

## Post-Deploy Verification

### 1. Check Service Health

Visit `https://<your-service-name>.onrender.com/` — you should get a redirect response.

### 2. Register a Test User

```http
POST https://<your-service-name>.onrender.com/api/Authentication/register
Content-Type: application/json

{
  "username": "testuser",
  "email": "test@example.com",
  "password": "Test1234"
}
```

Expected: `200 OK`

### 3. Login

```http
POST https://<your-service-name>.onrender.com/api/Authentication/login
Content-Type: application/json

{
  "username": "testuser",
  "password": "Test1234"
}
```

Expected: `200 OK` with `token` field in response body.

### 4. Verify JWT-Authenticated Request

```http
GET https://<your-service-name>.onrender.com/api/Expense
Authorization: Bearer <token>
```

Expected: `200 OK` (or empty array `[]` if no expenses exist).

---

## Troubleshooting

### Cold Starts (Free Plan)

The Free instance pauses after 15 minutes of inactivity. The first request after idle will take ~30 seconds to resume. Acceptable for personal use; upgrade to Starter for always-on.

### Cockroach Cloud Free Tier Auto-Pause

CockroachDB Cloud free clusters pause after ~5 minutes of no connections. This can cause the Render health check or first request to time out. Workarounds:

- Upgrade to a paid CockroachDB tier (never pauses)
- Add an external uptime pinger that hits the service every few minutes

### JWT Secret Rotation

If you change `JWT__Secret` on Render, all existing JWTs are immediately invalidated. Users will need to log in again.

### Migrations Fail on Deploy

If the Release Command fails:

1. Check Render's deploy logs for the error.
2. Verify `ConnectionStrings__CockroachDb` is correct and the cluster is reachable.
3. Manually run the migration locally first to confirm the SQL is valid:
   ```bash
   dotnet ef migrations script --idempotent --project FinanceManagerApi --output migrations.sql
   ```
4. Check `CLOUD_DB_MIGRATION.md` for CockroachDB-specific migration SQL cleaning if needed.

### Docker Image Pull Fails

- Confirm the image exists on Docker Hub: `docker search musarratchowdhury/financemanagerapi`
- Ensure the image tag (`latest` or `v1`) matches what you pushed.
- Check Render logs for specific pull errors.

---

## Updating the Deployment

### After Code Changes

1. Push your changes to the `main` branch (or merge a PR).
2. Rebuild and push the Docker image:
   ```bash
   docker build -t musarratchowdhury/financemanagerapi:latest -f FinanceManagerApi/Dockerfile .
   docker push musarratchowdhury/financemanagerapi:latest
   ```
3. On Render, go to **Deployments** → find the latest → click **Redeploy**.

### After Adding a New EF Migration

1. Apply the migration to your CockroachDB Cloud cluster via SQL Lab or CLI (see `CLOUD_DB_MIGRATION.md`).
2. Push the updated `FinanceManagerApi/Migrations/` folder to GitHub.
3. Render's Release Command will apply the migration on the next deploy.

---

## Security Notes

- **Never commit secrets.** The `JWT__Secret` and CockroachDB password must only exist in Render's environment variables.
- **Rotate CockroachDB password** periodically via CockroachDB Cloud console.
- **CORS is fully open** (`AllowAnyOrigin`). This is acceptable behind Render's HTTPS, but consider restricting to your frontend domain as a hardening step.
