---
name: api-deployment
description: Use ONLY when building, publishing, or verifying the FinanceManagerApi Docker image for Docker Hub and Render deployment. Prefer the CI/CD pipeline on push to main; this covers the manual build-and-push path and how to verify what is actually running.
compatibility: opencode
metadata:
  project: FinanceManagerApi
  pipeline: docker-hub-render
---

# FinanceManager API Deployment

Use this skill for deployment requests involving the ASP.NET Core API in this repository. The production image is published as `musarratchowdhury/financemanagerapi` and Render pulls the `latest` tag according to the deployment documentation.

## Prefer the CI/CD pipeline

Pushing to `main` runs `.github/workflows/ci-cd.yml`, which builds, tests,
pushes the image, applies the EF migrations to CockroachDB and notifies Render.
**Use that path unless the user explicitly asks for a manual push** — a manual
push skips the migration step and can ship an image whose schema does not
match the code.

Two things the pipeline does not tell you:

- `RENDER_DEPLOY_HOOK_URL` is not configured, so Render polls Docker Hub on its
  own schedule. A green workflow does **not** mean the API is live — wait a few
  minutes and probe the API for the behaviour you changed.
- The image is pushed **before** the migration step, so a failed migration
  still leaves a new image on Docker Hub that Render may auto-deploy.

## Safety Rules

- Never print, request, or commit database passwords, JWT secrets, or Docker Hub tokens.
- Do not deploy a dirty worktree without explicitly telling the user which uncommitted files will be included and receiving confirmation.
- Do not push an image until tests and the solution build pass.
- Use the repository root as the Docker build context.
- Do not run database migrations during image build — see the `cockroach-ef-migrations` skill for the supported path.
- Treat a Docker Hub push as an external side effect and require confirmation unless the user explicitly requested the push.

## Preflight

Run from the repository root:

```powershell
git status --short --branch
dotnet test FinanceManagerApi.sln
dotnet build FinanceManagerApi.sln --configuration Release
docker info
```

Confirm that `FinanceManagerApi/FinanceManagerApi.csproj` and `FinanceManagerApi/Dockerfile` target compatible .NET versions. The current project and Dockerfile target `net10.0` and .NET 10 images.

If `git status` reports changes, summarize them before building. The Docker build uses the current working tree, not only committed files.

## Build and Tag

Build from the repository root:

```powershell
docker build --pull -t musarratchowdhury/financemanagerapi:latest -f FinanceManagerApi/Dockerfile .
```

Prefer an immutable release tag in addition to `latest`:

```powershell
docker tag musarratchowdhury/financemanagerapi:latest musarratchowdhury/financemanagerapi:vYYYY-MM-DD
```

Use the actual deployment date for `vYYYY-MM-DD`; do not overwrite an existing version tag.

## Authentication and Push

Check Docker Hub authentication without exposing credentials. If authentication is missing, tell the user to run `docker login` locally.

After confirmation:

```powershell
docker push musarratchowdhury/financemanagerapi:latest
docker push musarratchowdhury/financemanagerapi:vYYYY-MM-DD
```

Report the pushed image digest when Docker returns it.

## Render

Verify that the Render service is configured for Docker image deployment. If it uses the native .NET buildpack, pushing this image will not update the service and deployment must stop for clarification.

For a Docker-based Render service:

1. Confirm the image is `musarratchowdhury/financemanagerapi:latest`.
2. Redeploy the service or trigger a manual deploy after the push.
3. Do not change `ConnectionStrings__CockroachDb`, `JWT__Secret`, `JWT__Issuer`, or `JWT__Audience` from the deployment workflow.

## Smoke Test

After Render reports the deployment is live, verify the service URL without exposing secrets:

```powershell
Invoke-WebRequest -Uri 'https://<service-host>/' -Method Get
```

Then authenticate with a test account using a secret supplied interactively or through a local environment variable, and use the returned token to test an authenticated endpoint. Never place credentials or tokens in shell history, source files, or the final report.

Check Render logs for cold starts, database connectivity, migration failures, and container port errors. The container listens on port `8000` through `ASPNETCORE_URLS` when configured by the deployment environment.

## Rollback

Keep the previous immutable image tag. If the new deployment fails, configure Render to use the previous known-good tag instead of rebuilding over it.
