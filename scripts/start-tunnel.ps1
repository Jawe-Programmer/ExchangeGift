<#
.SYNOPSIS
  Opens a temporary public https address (Cloudflare Quick Tunnel) that forwards to the site on this PC.
.DESCRIPTION
  Prerequisite: install cloudflared once:   winget install --id Cloudflare.cloudflared
  The site must already be running (scripts\start-site.ps1). The https://*.trycloudflare.com address is
  printed below and CHANGES every time the tunnel is restarted. Close this window to stop sharing.
#>
param(
    [int]$Port = 5080
)

$ErrorActionPreference = 'Stop'

$cloudflared = Get-Command cloudflared -ErrorAction SilentlyContinue
if (-not $cloudflared) {
    Write-Host 'cloudflared is not installed. Run this once, then open a new terminal:' -ForegroundColor Yellow
    Write-Host '    winget install --id Cloudflare.cloudflared'
    exit 1
}

try {
    Invoke-WebRequest -Uri "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 5 | Out-Null
} catch {
    Write-Host "The site is not answering on http://localhost:$Port. Start it first with scripts\start-site.ps1" -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host 'Starting the tunnel. Look for the line containing https://....trycloudflare.com' -ForegroundColor Green
Write-Host 'Then paste that address into  Admin > Dashboard > Sharing address  so every copied link uses it.'
Write-Host 'Close this window (or press Ctrl+C) to stop sharing.'
Write-Host ''

& $cloudflared.Source tunnel --url "http://localhost:$Port"
