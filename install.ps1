[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $projectRoot 'dist\TaskbarSystemMonitor.exe'
$installDirectory = Join-Path $env:LOCALAPPDATA 'TaskbarSystemMonitor'
$installedExe = Join-Path $installDirectory 'TaskbarSystemMonitor.exe'

if (-not (Test-Path -LiteralPath $sourceExe)) {
    & (Join-Path $projectRoot 'build.ps1')
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name 'TaskbarSystemMonitor' -Value "`"$installedExe`" --startup"

Start-Process -FilePath $installedExe

Write-Host 'Install complete. The monitor is running and starts with Windows.' -ForegroundColor Green
Write-Host "Installed to: $installedExe"
