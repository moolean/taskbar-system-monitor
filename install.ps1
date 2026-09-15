[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $projectRoot 'dist\TaskbarSystemMonitor.exe'
$installDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TaskbarSystemMonitor'
$installedExe = Join-Path $installDirectory 'TaskbarSystemMonitor.exe'

. (Join-Path $projectRoot 'scripts\Stop-Monitor.ps1')
Stop-KnownMonitor -ExecutablePaths @($sourceExe, $installedExe)
& (Join-Path $projectRoot 'build.ps1')

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
$stagedExe = Join-Path $installDirectory 'TaskbarSystemMonitor.new.exe'
Copy-Item -LiteralPath $sourceExe -Destination $stagedExe -Force
if (Test-Path -LiteralPath $installedExe) {
    Copy-Item -LiteralPath $installedExe -Destination (Join-Path $installDirectory 'TaskbarSystemMonitor.previous.exe') -Force
}
# File.Replace / Move-Item can fail through MSIX LocalAppData virtualization.
# The process has exited and an upgrade backup exists; copy and verify instead.
Copy-Item -LiteralPath $stagedExe -Destination $installedExe -Force
if ((Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash) {
    throw 'Installed executable failed integrity verification. The previous EXE backup is retained.'
}
Remove-Item -LiteralPath $stagedExe -Force

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name 'TaskbarSystemMonitor' -Value "`"$installedExe`" --startup"

# This is the visible resource bar the user is installing, not a console helper.
$monitor = Start-Process -FilePath $installedExe -PassThru
if ($monitor.WaitForExit(2500)) { throw "Monitor exited during startup (code $($monitor.ExitCode)). See $installDirectory\error.log" }
Write-Host 'Installed and running. Startup registration is enabled for the current user.' -ForegroundColor Green
Write-Host "Installed to: $installedExe"
