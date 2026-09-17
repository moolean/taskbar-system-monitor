[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'scripts\MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
Stop-MonitorProcess -ExecutablePaths @($paths.Installed, $paths.LegacyInstalled, $paths.Source)
Write-Host 'Stopped safely. User-logon startup remains enabled unless disabled in the tray menu.'
