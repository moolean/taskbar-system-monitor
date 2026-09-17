[CmdletBinding()]
param([switch]$Standalone)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
if (-not (Test-Path -LiteralPath $paths.Source)) { & (Join-Path $paths.Project 'build.ps1') }
# Reuse exactly the application's read-only source. -Standalone is retained for
# older callers; there is no longer a proxy mode tied to a running desktop host.
$report = Join-Path $paths.Project 'dist\codex-connection.txt'
Invoke-MonitorCommand -Executable $paths.Source -Arguments ('"--codex-test=' + $report + '"') -ReportPath $report
Get-Content -LiteralPath $report
