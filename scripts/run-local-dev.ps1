<#
.SYNOPSIS
    Run the FinanceManagerApi locally in Development mode against a given database.

.DESCRIPTION
    The API's ExceptionMiddleware returns a generic
    "An error occurred, please try again later" in Production, but the real
    exception and stack trace in Development. This script runs the API with
    Development settings so a 500 can be diagnosed locally.

    The connection string is read from, in order:
      1. -ConnectionString (if given)
      2. $env:COCKROACH_CONNECTION_STRING
      3. the ConnectionStrings:CockroachDb user-secret of the API project

    DbInitializer only seeds when the tables are empty, so pointing this at the
    production database is safe - it logs "already exists - skipping".

    The JWT secret is overridden with a local value, which means tokens issued
    by the PRODUCTION api will not validate here. Log in through this local
    instance to get a local token.

.EXAMPLE
    ./scripts/run-local-dev.ps1
    ./scripts/run-local-dev.ps1 -ConnectionString "Host=...;Database=financedb;..."
#>
[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$Url = 'http://localhost:5006',
    [string]$JwtSecret = 'local-debug-secret-0123456789abcdef'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $ConnectionString) { $ConnectionString = $env:COCKROACH_CONNECTION_STRING }

if (-not $ConnectionString) {
    Write-Host 'Reading ConnectionStrings:CockroachDb from user-secrets...' -ForegroundColor DarkGray
    $secrets = dotnet user-secrets list --project FinanceManagerApi/FinanceManagerApi.csproj 2>$null
    foreach ($line in $secrets) {
        $key, $value = $line -split ' = ', 2
        if ($key.Trim() -eq 'ConnectionStrings:CockroachDb') { $ConnectionString = $value.Trim() }
    }
}

if (-not $ConnectionString) {
    Write-Error @'
No connection string found. Pass one explicitly or set it up first:

  dotnet user-secrets set "ConnectionStrings:CockroachDb" "<conn string>" --project FinanceManagerApi

then re-run this script.
'@
}

$env:ConnectionStrings__CockroachDb = $ConnectionString
$env:JWT__Secret = $JwtSecret
$env:JWT__Issuer = 'http://localhost:5006'
$env:JWT__Audience = 'user'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $Url

Write-Host "Starting API in Development on $Url" -ForegroundColor Cyan
Write-Host "  (the real exception + stack trace is returned in the 500 response)" -ForegroundColor DarkGray
Write-Host '  Ctrl+C to stop.' -ForegroundColor DarkGray
Write-Host ''

dotnet run --project FinanceManagerApi --no-launch-profile
