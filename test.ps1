[CmdletBinding()]
param([switch]$Desktop)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'scripts\MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $PSScriptRoot 'scripts\Test-Scripts.ps1')
$report = Join-Path $PSScriptRoot 'dist\self-test-error.txt'
Invoke-MonitorCommand -Executable $paths.Source -Arguments @('--self-test', ('"--test-report=' + $report + '"')) -ReportPath $report
Write-Host 'PASS: startup policy, sampling, work reorder/persistence, compact layout, palette contrast, autosave/undo, English labels, quota parsing, and migration safety.'
if ($Desktop) {
    $report = Join-Path $PSScriptRoot 'dist\desktop-test.txt'
    Invoke-MonitorCommand -Executable $paths.Source -Arguments ('"--appbar-test=' + $report + '"') -TimeoutMs 20000 -ReportPath $report
    Get-Content -LiteralPath $report
}
