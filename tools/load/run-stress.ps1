# Жёсткий capacity-stress на прод + отчёт (md/json) в tools/load/reports/
#
#   .\tools\load\run-stress.ps1 -BaseUrl https://ТВОЙ_ХОСТ -AdminPass '***' -DeptPass 'INICinic1'
#   .\tools\load\run-stress.ps1 -BaseUrl https://host -WriteRate 0.5 -UploadAttach -MaxVus 500
#
# После прогона смотри:
#   tools\load\reports\latest.md   — полный отчёт + блок для руководства
#   tools\load\reports\latest.json — те же цифры машиной
#
# Очистка:
#   Get-Content .\tools\load\cleanup-keep-users.sql | docker compose exec -T postgres psql -U postgres -d crown_db
#   .\tools\load\wipe-attachments.ps1

param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,
    [string] $AdminUser = "admin",
    [string] $AdminPass = "",
    [string] $DeptUser = "inic",
    [string] $DeptPass = "INICinic1",
    [ValidateRange(0.0, 1.0)]
    [double] $WriteRate = 0.5,
    [switch] $UploadAttach,
    [int] $MaxVus = 400,
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
$upload = if ($UploadAttach) { "1" } else { "0" }
$reports = Join-Path $root "tools\load\reports"
New-Item -ItemType Directory -Force -Path $reports | Out-Null

Write-Host "STRESS BASE_URL=$BaseUrl WRITE_RATE=$WriteRate UPLOAD_ATTACH=$upload MAX_VUS=$MaxVus"
Write-Host "Отчёты → $reports"

docker run --rm `
    -v "${root}/tools/load:/scripts:ro" `
    -v "${reports}:/reports" `
    -e "BASE_URL=$BaseUrl" `
    -e "ADMIN_USER=$AdminUser" `
    -e "ADMIN_PASS=$AdminPass" `
    -e "DEPT_USER=$DeptUser" `
    -e "DEPT_PASS=$DeptPass" `
    -e "WRITE_RATE=$WriteRate" `
    -e "UPLOAD_ATTACH=$upload" `
    -e "MAX_VUS=$MaxVus" `
    -e "REPORT_DIR=/reports" `
    $K6Image run /scripts/crown-stress.js

Write-Host ""
$execFile = Join-Path $reports "latest-exec.txt"
if (Test-Path $execFile) {
    Write-Host ""
    Get-Content $execFile
    Write-Host ""
    Write-Host "Полный отчёт: $(Join-Path $reports 'latest.md')"
    Write-Host "JSON:          $(Join-Path $reports 'latest.json')"
} else {
    Write-Host "Файлы отчёта не появились — смотри лог k6 выше."
}
