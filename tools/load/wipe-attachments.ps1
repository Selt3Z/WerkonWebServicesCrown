# Снести файлы вложений на диске после stress с -UploadAttach.
# Таблицы чистит cleanup-keep-users.sql; этот скрипт — только каталог на хосте.

param(
    [string] $Path = ""
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if (-not $Path) {
    $line = Get-Content (Join-Path $root ".env") -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '^ATTACHMENTS_HOST_PATH=' }
    if ($line) { $Path = ($line -split '=', 2)[1].Trim().Trim('"') }
}
if (-not $Path) { $Path = Join-Path $root "data\attachments" }
if (-not [System.IO.Path]::IsPathRooted($Path)) {
    $Path = Join-Path $root $Path
}

if (-not (Test-Path $Path)) {
    Write-Host "Нет каталога: $Path"
    exit 0
}

Write-Host "Удаляю содержимое: $Path"
Get-ChildItem -Force $Path | Remove-Item -Recurse -Force
Write-Host "Готово."
