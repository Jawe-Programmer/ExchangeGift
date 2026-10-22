<#
.SYNOPSIS
  Starts the Exchange Gift website on this PC (http://localhost:5080 by default).
.DESCRIPTION
  Applies database migrations automatically on start. Keep this window open while the event is running.
  The administrator password is read from .NET User Secrets (Admin:Password) or the Admin__Password
  environment variable - see README.md. It is never stored in this script.
#>
param(
    [int]$Port = 5080
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ExchangeGift.Web'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'The .NET SDK was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download and try again.'
}

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://localhost:$Port"

Write-Host ''
Write-Host "Starting the gift exchange site on http://localhost:$Port" -ForegroundColor Green
Write-Host "Admin sign-in:  http://localhost:$Port/admin   (user name: Administrator)"
Write-Host 'Leave this window open. Press Ctrl+C to stop the site.'
Write-Host ''

dotnet run --project $project -c Release --no-launch-profile
