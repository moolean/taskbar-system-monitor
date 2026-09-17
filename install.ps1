[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'scripts\MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths

# A failed build must not take a working installation offline.
Stop-MonitorProcess -ExecutablePaths @($paths.Source)
& (Join-Path $PSScriptRoot 'build.ps1')
$report = Join-Path $PSScriptRoot 'dist\install-self-test.txt'
Invoke-MonitorCommand -Executable $paths.Source -Arguments @('--self-test', ('"--test-report=' + $report + '"')) -ReportPath $report

Stop-MonitorProcess -ExecutablePaths @($paths.Installed, $paths.LegacyInstalled)
New-Item -ItemType Directory -Path $paths.InstallDirectory -Force | Out-Null
# Copy out of a packaged installer's virtual AppData while it can still see the
# legacy file. Task Scheduler cannot see that private view. Keep the old backup.
if (-not (Test-Path -LiteralPath $paths.Settings) -and (Test-Path -LiteralPath $paths.LegacySettings)) {
    Copy-MonitorVerified -Source $paths.LegacySettings -Destination $paths.Settings
    Write-Host 'Migrated existing settings byte-for-byte; the legacy copy is retained.'
}
$settingsHash = if (Test-Path -LiteralPath $paths.Settings) { (Get-FileHash -LiteralPath $paths.Settings).Hash } else { $null }
$hadPrevious = Test-Path -LiteralPath $paths.Installed
if ($hadPrevious) { Copy-MonitorVerified -Source $paths.Installed -Destination $paths.Backup }
try {
    Copy-MonitorVerified -Source $paths.Source -Destination $paths.Staged
    Copy-MonitorVerified -Source $paths.Staged -Destination $paths.Installed
    Invoke-MonitorStartup -Executable $paths.Installed -Action enable | Out-Null
    $monitor = Start-MonitorIndependent
    if ($settingsHash -and $settingsHash -ne (Get-FileHash -LiteralPath $paths.Settings).Hash) {
        Write-Warning 'Configuration changed during launch (migration or user edit); settings were not overwritten by the installer.'
    }
    Write-Host "Installed and independently running: $($paths.Installed) (PID $($monitor.Id))" -ForegroundColor Green
    Write-Host 'Windows starts it at user logon. No Codex window, terminal, or resident script is required.'
} catch {
    $installFailure = $_
    if ($hadPrevious) {
        try {
            Stop-MonitorProcess -ExecutablePaths @($paths.Installed)
            Copy-MonitorVerified -Source $paths.Backup -Destination $paths.Installed
            $state = Invoke-MonitorStartup -Executable $paths.Source -Action status
            if ($state.Registered) { Start-MonitorIndependent -Controller $paths.Source | Out-Null }
            else { Start-Process -FilePath $paths.Installed -WindowStyle Normal | Out-Null }
            Write-Warning 'Upgrade failed; the previous executable was restored and restarted.'
        } catch { Write-Warning "Automatic recovery needs attention: $($_.Exception.Message). The previous EXE is retained at $($paths.Backup)." }
    }
    throw $installFailure
} finally {
    if (Test-Path -LiteralPath $paths.Staged) { Remove-Item -LiteralPath $paths.Staged -Force }
}
