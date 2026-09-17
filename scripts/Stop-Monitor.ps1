# Compatibility shim for older commands; lifecycle logic lives in one module.
Import-Module (Join-Path $PSScriptRoot 'MonitorTools.psm1') -Force -DisableNameChecking
function Stop-KnownMonitor {
    param([string[]]$ExecutablePaths)
    Stop-MonitorProcess -ExecutablePaths $ExecutablePaths
}
