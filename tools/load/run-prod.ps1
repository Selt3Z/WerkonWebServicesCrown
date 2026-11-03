# Нагрузка на прод (или любой стенд) через Docker k6.
# Пример:
#   .\tools\load\run-prod.ps1 -BaseUrl https://zs-mns.local -AdminPass '***' -DeptPass 'INICinic1'
#   .\tools\load\run-prod.ps1 -BaseUrl https://10.0.0.5 -WriteRate 0.2
#
# BASE бьёт в Caddy: /api/* → api. Логин rate-limit 10/min/IP — скрипт логинится только в setup.

param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [string] $AdminUser = "admin",
    [string] $AdminPass = "",
    [string] $DeptUser = "inic",
    [string] $DeptPass = "INICinic1",
    [ValidateRange(0.0, 1.0)]
    [double] $WriteRate = 0.15,
    [string] $K6Image = "grafana/k6:0.54.0"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $root

if (-not $AdminPass) {
    $line = Get-Content .\.env -ErrorAction SilentlyContinue | Where-Object { $_ -match '^Seed__AdminPassword=' }
    if ($line) { $AdminPass = ($line -split '=', 2)[1] }
}
if (-not $AdminPass) { throw "Задай -AdminPass или Seed__AdminPassword в .env" }

$BaseUrl = $BaseUrl.TrimEnd('/')
Write-Host "BASE_URL=$BaseUrl WRITE_RATE=$WriteRate ADMIN=$AdminUser DEPT=$DeptUser"

docker run --rm `
    -v "${root}/tools/load:/scripts:ro" `
    -e "BASE_URL=$BaseUrl" `
    -e "ADMIN_USER=$AdminUser" `
    -e "ADMIN_PASS=$AdminPass" `
    -e "DEPT_USER=$DeptUser" `
    -e "DEPT_PASS=$DeptPass" `
    -e "WRITE_RATE=$WriteRate" `
    $K6Image run /scripts/crown-smoke.js

Write-Host ""
Write-Host "После теста очистка БД (users сохранить):"
Write-Host "  docker compose exec -T postgres psql -U `$env:POSTGRES_USER -d `$env:POSTGRES_DB -f - < tools/load/cleanup-keep-users.sql"
Write-Host "  или скопируй SQL на сервер и выполни psql. Вложения на диске — отдельно."
