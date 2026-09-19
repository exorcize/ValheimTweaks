# Pulls the ValheimTweaks audit lines out of the BepInEx log.
#
# What it shows by default: the chest audit and the store trace, i.e. every line
# starting with [AUDIT] or [CHESTS]. That is the context needed to answer "where
# did my item go": which chest, its owner, who touched it, and the item counts
# before and after.
#
# Usage:
#   .\log.ps1                       prints the [AUDIT] / [CHESTS] lines
#   .\log.ps1 -All                  prints the whole log
#   .\log.ps1 -Pattern 'AUDIT'      only lines matching this regex
#   .\log.ps1 -Out audit.txt        also writes the result to a file to send on
#   .\log.ps1 -Watch                follows the log live (Ctrl+C to stop)
#
# If PowerShell refuses to run scripts:
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\log.ps1

param(
    [string]$Pattern = '\[AUDIT\]|\[CHESTS\]',
    [switch]$All,
    [string]$Out,
    [switch]$Watch
)

$ErrorActionPreference = 'Stop'

$candidates = @(
    (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim\profiles\Default\BepInEx\LogOutput.log'),
    'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\LogOutput.log',
    (Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\Player.log')
)

$log = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $log) {
    throw "No BepInEx log found. Looked in:`n  " + ($candidates -join "`n  ")
}

Write-Host "log: $log" -ForegroundColor DarkGray

if ($Watch) {
    Write-Host "watching (Ctrl+C to stop) ..." -ForegroundColor DarkGray
    Get-Content -LiteralPath $log -Wait | Where-Object { $_ -match $Pattern } | ForEach-Object { $_ }
    return
}

if ($All) {
    $lines = @(Get-Content -LiteralPath $log)
} else {
    $lines = @(Get-Content -LiteralPath $log | Where-Object { $_ -match $Pattern })
}

if ($Out) {
    $lines | Set-Content -LiteralPath $Out -Encoding UTF8
    Write-Host "written: $Out ($($lines.Count) lines)" -ForegroundColor Green
} elseif ($lines.Count -eq 0) {
    Write-Host "no lines matched '$Pattern' yet." -ForegroundColor Yellow
} else {
    $lines | ForEach-Object { $_ }
}
