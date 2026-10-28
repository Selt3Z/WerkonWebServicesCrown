# Прописывает crown.local → 127.0.0.1 в hosts.
# Запускать от Администратора:
#   powershell -ExecutionPolicy Bypass -File docker\setup-hosts.ps1

$ErrorActionPreference = "Stop"
$HostName = "crown.local"
$HostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$Entry = "127.0.0.1`t$HostName"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host ""
    Write-Host "ERROR: нужен PowerShell от Администратора."
    Write-Host ""
    exit 1
}

try {
    $lines = @(Get-Content $HostsPath -ErrorAction Stop)
    $filtered = $lines | Where-Object {
        $_ -notmatch '(?i)^\s*127\.0\.0\.1\s+(wwsratchet\.local|ratchet\.wws|ratchet\.local|crown\.local)\s*$'
    }

    if (-not ($filtered -match [regex]::Escape($HostName))) {
        $filtered = @($filtered) + $Entry
    }

    $tmp = Join-Path $env:TEMP "hosts.crown.$PID.tmp"
    Set-Content -Path $tmp -Value ($filtered -join "`r`n") -Encoding ASCII
    # Копирование через временный файл — меньше шансов поймать lock, чем Set-Content прямо в hosts.
    Copy-Item -Path $tmp -Destination $HostsPath -Force
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Host ""
    Write-Host "ERROR: не удалось записать hosts: $($_.Exception.Message)"
    Write-Host "Закрой VPN/антивирус/редактор, который держит файл, и повтори."
    Write-Host "Или вручную добавь строку в $HostsPath :"
    Write-Host "  $Entry"
    Write-Host ""
    exit 1
}

$verify = Get-Content $HostsPath -Raw
if ($verify -notmatch '(?m)^\s*127\.0\.0\.1\s+crown\.local\s*$') {
    Write-Host ""
    Write-Host "ERROR: запись не появилась в hosts после записи."
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "OK: $HostName -> 127.0.0.1"
Write-Host "Открывай: http://$HostName  (или http://$HostName`:8088 если Crown на 8088)"
Write-Host ""
