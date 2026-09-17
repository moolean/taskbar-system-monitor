[CmdletBinding()]
param([switch]$Restart)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'scripts\MonitorTools.psm1') -Force -DisableNameChecking
$monitor = Start-MonitorIndependent -Restart:$Restart
Write-Host "Monitor is independently running (PID $($monitor.Id))." -ForegroundColor Green
