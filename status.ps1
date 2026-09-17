[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'scripts\MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
if (-not (Test-Path -LiteralPath $paths.Installed)) { Write-Output 'Not installed. Run install.ps1.'; return }
$state = Invoke-MonitorStartup -Executable $paths.Installed -Action status
$running = @(Get-MonitorProcess -ExecutablePaths @($paths.Installed))
[pscustomobject]@{
    Installed = $paths.Installed
    Version = (Get-Item -LiteralPath $paths.Installed).VersionInfo.FileVersion
    ProcessIds = @($running | Select-Object -ExpandProperty Id)
    LogonStartup = $state.AutoStart
    TaskName = $state.TaskName
    TaskState = $state.State
    LastTaskResult = $state.LastResult
    ExecutionTimeLimit = $state.ExecutionTimeLimit
    StopOnBattery = $state.StopOnBattery
    FailureRetries = $state.RestartCount
    LegacyRunEntry = $state.LegacyRunEntry
}
