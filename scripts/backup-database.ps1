<#
.SYNOPSIS
  Copies the local SQLite database to backups\exchangegift-<timestamp>.db
.DESCRIPTION
  Stop the site first (or at least make sure nobody is revealing a result) so the copy is consistent.
  The data-protection keys folder is also copied, so the administrator's saved sign-in stays valid after a restore.
#>
param(
    [string]$DataDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\ExchangeGift.Web\App_Data'),
    [string]$BackupDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'backups')
)

$ErrorActionPreference = 'Stop'
$db = Join-Path $DataDir 'exchangegift.db'
if (-not (Test-Path $db)) { Write-Error "No database found at $db" }

New-Item -ItemType Directory -Force $BackupDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $BackupDir "exchangegift-$stamp.db"
Copy-Item $db $target
foreach ($ext in '-wal', '-shm') { if (Test-Path "$db$ext") { Copy-Item "$db$ext" "$target$ext" } }
$keys = Join-Path $DataDir 'keys'
if (Test-Path $keys) { Copy-Item $keys (Join-Path $BackupDir "keys-$stamp") -Recurse }
Write-Host "Backup written to $target" -ForegroundColor Green
