[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
if (-not (Test-Path -LiteralPath $paths.Source)) { & (Join-Path $paths.Project 'build.ps1') }
$report = Join-Path $paths.Project 'dist\mouse-battery-test.txt'
Invoke-MonitorCommand -Executable $paths.Source -Arguments ('"--mouse-test=' + $report + '"') -TimeoutMs 10000 -ReportPath $report
Get-Content -LiteralPath $report
