[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'TaskbarSystemMonitor'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Remove-ItemProperty -Path $runKey -Name 'TaskbarSystemMonitor' -ErrorAction SilentlyContinue

Get-Process -Name 'TaskbarSystemMonitor' -ErrorAction SilentlyContinue |
    Stop-Process -Force

if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Host 'Taskbar System Monitor has been uninstalled.' -ForegroundColor Green
