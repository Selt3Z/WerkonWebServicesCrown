# Applies POSTGRES_PASSWORD from ../.env to the RUNNING database (no data loss).
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File docker\apply-postgres-password.ps1

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$EnvFile = Join-Path $RepoRoot ".env"

if (-not (Test-Path $EnvFile)) {
    Write-Error ".env not found at $EnvFile"
}

$password = $null
Get-Content $EnvFile | ForEach-Object {
    if ($_ -match '^\s*POSTGRES_PASSWORD=(.+)$') {
        $password = $Matches[1].Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($password)) {
    Write-Error "POSTGRES_PASSWORD not found in .env"
}

$container = "crown_postgres"
$running = docker ps --filter "name=$container" --filter "status=running" -q
if (-not $running) {
    Write-Error "Container '$container' is not running. Start Postgres in Docker Desktop first."
}

# Inside the container local psql connects as postgres without the old password.
$escaped = $password -replace "'", "''"
$sql = "ALTER USER postgres WITH PASSWORD '$escaped';"

docker exec $container psql -U postgres -d postgres -v ON_ERROR_STOP=1 -c $sql

Write-Host "Done. Password in the database now matches .env"
Write-Host "Restart API if it was already running."
