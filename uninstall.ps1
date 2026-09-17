[CmdletBinding()]
param([switch]$PurgeSettings)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'scripts\MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
Stop-MonitorProcess -ExecutablePaths @($paths.Installed)
# Use the new controller even if the installed executable is old or missing.
& (Join-Path $PSScriptRoot 'build.ps1')
Invoke-MonitorStartup -Executable $paths.Source -Action remove | Out-Null
$names = @('TaskbarSystemMonitor.exe', 'TaskbarSystemMonitor.previous.exe', 'TaskbarSystemMonitor.new.exe')
if ($PurgeSettings) { $names += @('settings.xml', 'settings.xml.tmp', 'error.log', 'runtime.log') }
foreach ($name in $names) {
    $target = Join-Path $paths.InstallDirectory $name
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
}
Write-Host 'Monitor and its scheduled task removed. Settings are preserved unless -PurgeSettings was specified.' -ForegroundColor Green
