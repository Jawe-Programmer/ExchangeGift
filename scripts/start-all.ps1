<#
.SYNOPSIS
  One-click event start: opens the site and the temporary tunnel, each in its own window.
#>
param(
    [int]$Port = 5080
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$here\start-site.ps1`"", '-Port', $Port

Write-Host "Waiting for the site to come up on http://localhost:$Port ..."
$up = $false
for ($i = 0; $i -lt 90 -and -not $up; $i++) {
    Start-Sleep -Seconds 1
    try { Invoke-WebRequest -Uri "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 2 | Out-Null; $up = $true } catch { }
}
if (-not $up) { Write-Error 'The site did not start. Check the site window for the error message.' }

Start-Process "http://localhost:$Port/admin"
Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$here\start-tunnel.ps1`"", '-Port', $Port

Write-Host ''
Write-Host 'Site and tunnel windows are open.' -ForegroundColor Green
Write-Host 'Copy the https://....trycloudflare.com address from the tunnel window into Admin > Dashboard > Sharing address.'
