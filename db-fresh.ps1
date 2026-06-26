$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$Project = Join-Path $RepoRoot 'FinanceManagerApi'
$SqlPath = Join-Path $RepoRoot '.ef-migrations.sql'
$Container = 'fmapi-cockroach'
$DbName = 'financedb'
$Port = 26257

function Fail($msg) {
    Write-Error $msg
    exit 1
}

function Invoke-CockroachSql {
    param([string]$Sql)
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        $output = $Sql | docker exec -i $Container ./cockroach sql --insecure -d $DbName 2>&1
    } finally {
        $ErrorActionPreference = $prevPref
    }
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host ('    ' + $_) }
    }
    return $LASTEXITCODE
}

function Get-MigrationCount {
    param([string]$MigrationId)
    $sql = "SELECT COUNT(*) FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '$MigrationId';"
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        $output = $sql | docker exec -i $Container ./cockroach sql --insecure -d $DbName 2>&1
    } finally {
        $ErrorActionPreference = $prevPref
    }
    if ($LASTEXITCODE -ne 0) { return -1 }
    foreach ($line in $output) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\d+$') {
            return [int]$trimmed
        }
    }
    return 0
}

function Preserve-And-Exit($code) {
    Write-Warning "Preserving generated script at $SqlPath for inspection."
    exit $code
}

Write-Host '[1/6] Checking prerequisites...'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Fail 'docker is not on PATH. Install Docker Desktop: https://www.docker.com/products/docker-desktop/'
}

$running = docker ps --filter "name=^${Container}$" --format '{{.Names}}' 2>$null
if ($running -ne $Container) {
    Fail "Container '$Container' is not running. Start it with: docker compose -f docker-compose.dev.yml up -d (see LOCAL_DB_SETUP.md)."
}

$tnc = Test-NetConnection -ComputerName localhost -Port $Port -WarningAction SilentlyContinue
if (-not $tnc.TcpTestSucceeded) {
    Fail "Port $Port is not reachable on localhost. Is the '$Container' container healthy?"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail 'dotnet is not on PATH. Install the .NET 10 SDK.'
}

Write-Host ('    dotnet --version:     ' + (dotnet --version))

$efOutput = & dotnet ef --version 2>&1
$efExit = $LASTEXITCODE
if ($efExit -ne 0) {
    Write-Host ("    dotnet ef --version FAILED (exit $efExit):")
    $efOutput | ForEach-Object { Write-Host ('      ' + $_) }
    Fail "dotnet-ef tool is not installed or failed to run. Install with: dotnet tool install --global dotnet-ef --version 10.*"
}
$efVersionLine = ($efOutput | Select-Object -Last 1)
Write-Host ('    dotnet ef --version: ' + $efVersionLine)

Write-Host '[2/6] Building project (required for migrations script)...'

$buildLog = & dotnet build $Project --nologo -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    $buildLog | Write-Host
    Fail "Build of $Project failed. Fix build errors before migrating."
}

Write-Host "[3/6] Ensuring database '$DbName' exists..."

docker exec $Container ./cockroach sql --insecure -e "CREATE DATABASE IF NOT EXISTS $DbName;"
if ($LASTEXITCODE -ne 0) {
    Fail "Failed to create database '$DbName'."
}

Write-Host '[4/6] Generating idempotent SQL script from migrations...'

& dotnet ef migrations script --idempotent --project $Project --output $SqlPath
if ($LASTEXITCODE -ne 0) {
    Fail 'dotnet ef migrations script failed. See output above.'
}

Write-Host '[5/6] Applying migrations (CockroachDB rejects DDL inside DO blocks, so the EF-generated wrappers are stripped and each migration is applied individually)...'

$content = Get-Content -Raw $SqlPath

$historyTableMatch = [regex]::Match($content, '(?s)CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"[^;]*;')
if ($historyTableMatch.Success) {
    $ec = Invoke-CockroachSql $historyTableMatch.Value
    if ($ec -ne 0) { Preserve-And-Exit $ec }
}

$sectionPattern = '(?s)START TRANSACTION;(.*?)COMMIT;'
$ddlPattern = '(?s)DO \$EF\$.*?BEGIN\s+IF NOT EXISTS\(.*?THEN\s+(.*?)\s+END IF;\s+END \$EF\$;'

$sections = [regex]::Matches($content, $sectionPattern)
if ($sections.Count -eq 0) {
    Preserve-And-Exit 1
}

foreach ($section in $sections) {
    $body = $section.Groups[1].Value
    $idMatch = [regex]::Match($body, 'WHERE "MigrationId" = ''([^'']+)''')
    if (-not $idMatch.Success) { continue }
    $migrationId = $idMatch.Groups[1].Value

    $count = Get-MigrationCount $migrationId
    if ($count -lt 0) { Preserve-And-Exit 1 }
    if ($count -eq 1) {
        Write-Host "    $migrationId : already applied, skipping"
        continue
    }

    $ddls = [regex]::Matches($body, $ddlPattern)
    Write-Host "    $migrationId : applying $($ddls.Count) statement(s)..."

    foreach ($d in $ddls) {
        $ddl = $d.Groups[1].Value.Trim()
        if ($ddl -match 'INSERT INTO "__EFMigrationsHistory"') { continue }

        $ec = Invoke-CockroachSql $ddl
        if ($ec -ne 0) { Preserve-And-Exit $ec }
    }

    $historyInsert = "INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('$migrationId', '$efVersionLine');"
    $ec = Invoke-CockroachSql $historyInsert
    if ($ec -ne 0) { Preserve-And-Exit $ec }
}

Remove-Item -Force $SqlPath

Write-Host "[6/6] Verifying tables in '$DbName'..."

docker exec $Container ./cockroach sql --insecure -d $DbName -e 'SHOW TABLES;'

Write-Host ''
Write-Host "Done. Database '$DbName' is up to date."
