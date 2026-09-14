[CmdletBinding()]
param([switch]$PurgeSettings)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TaskbarSystemMonitor'
$installedExe = Join-Path $installDirectory 'TaskbarSystemMonitor.exe'
. (Join-Path $projectRoot 'scripts\Stop-Monitor.ps1')
Stop-KnownMonitor -ExecutablePaths @($installedExe)

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TaskbarSystemMonitor' -ErrorAction SilentlyContinue
# Only remove known product files, never recursively delete this directory.
$names = @('TaskbarSystemMonitor.exe', 'TaskbarSystemMonitor.previous.exe', 'TaskbarSystemMonitor.new.exe')
if ($PurgeSettings) { $names += @('settings.xml', 'settings.xml.tmp', 'error.log', 'runtime.log') }
foreach ($name in $names) {
    $target = Join-Path $installDirectory $name
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
}
Write-Host 'Monitor and startup entry removed. Settings are preserved unless -PurgeSettings was specified.' -ForegroundColor Green
