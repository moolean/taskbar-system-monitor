Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-MonitorPaths {
    $project = Split-Path -Parent $PSScriptRoot
    $install = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.local\TaskbarSystemMonitor'
    $legacy = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TaskbarSystemMonitor'
    [pscustomobject]@{
        Project = $project
        Source = Join-Path $project 'dist\TaskbarSystemMonitor.exe'
        InstallDirectory = $install
        Installed = Join-Path $install 'TaskbarSystemMonitor.exe'
        Backup = Join-Path $install 'TaskbarSystemMonitor.previous.exe'
        Staged = Join-Path $install 'TaskbarSystemMonitor.new.exe'
        Settings = Join-Path $install 'settings.xml'
        LegacyInstalled = Join-Path $legacy 'TaskbarSystemMonitor.exe'
        LegacySettings = Join-Path $legacy 'settings.xml'
    }
}

function Get-MonitorProcess {
    param([Parameter(Mandatory)][string[]]$ExecutablePaths)
    $resolved = @($ExecutablePaths | ForEach-Object { [IO.Path]::GetFullPath($_) })
    Get-Process -Name TaskbarSystemMonitor -ErrorAction SilentlyContinue |
        Where-Object { $resolved -contains $_.Path }
}

function Invoke-MonitorCommand {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [int]$TimeoutMs = 30000,
        [string]$ReportPath
    )
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Executable not found: $Executable" }
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit($TimeoutMs)) {
            # This is only our short-lived maintenance/test child, never the
            # independently running editor. Do not leave a failed helper behind.
            $process.Kill(); $process.WaitForExit(3000) | Out-Null
            throw "Command timed out (PID $($process.Id)): $Arguments"
        }
        if ($process.ExitCode -ne 0) {
            $detail = if ($ReportPath -and (Test-Path -LiteralPath $ReportPath)) { Get-Content -LiteralPath $ReportPath -Raw } else { '' }
            throw "Monitor command failed ($($process.ExitCode)): $detail"
        }
    } finally { $process.Dispose() }
}

function Invoke-MonitorStartup {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][ValidateSet('enable','disable','start','remove','status')][string]$Action
    )
    if ([version](Get-Item -LiteralPath $Executable).VersionInfo.FileVersion -lt [version]'3.7.0.0') {
        throw 'This command requires monitor 3.7.0 or newer. Run install.ps1 first.'
    }
    $report = Join-Path ([IO.Path]::GetTempPath()) ('monitor-startup-' + [guid]::NewGuid().ToString('N') + '.json')
    try {
        Invoke-MonitorCommand -Executable $Executable -Arguments @("--startup-control=$Action", ('"--startup-report=' + $report + '"')) -ReportPath $report
        Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    } finally {
        if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
    }
}

function Stop-MonitorProcess {
    param([Parameter(Mandatory)][string[]]$ExecutablePaths)
    foreach ($process in @(Get-MonitorProcess -ExecutablePaths $ExecutablePaths)) {
        if ($process.HasExited) { continue }
        # Exit IPC flushes notes and unregisters the AppBar; never force-kill.
        Invoke-MonitorCommand -Executable $process.Path -Arguments '--exit' -TimeoutMs 5000
        if (-not $process.WaitForExit(8000)) {
            throw 'Monitor could not exit safely. Resolve any unsaved-note error in its window, then retry.'
        }
    }
}

function Copy-MonitorVerified {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    if ((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash) {
        throw "Integrity verification failed: $Destination"
    }
}

function Start-MonitorIndependent {
    param([string]$Controller, [switch]$Restart)
    $paths = Get-MonitorPaths
    if (-not $Controller) { $Controller = $paths.Installed }
    $state = Invoke-MonitorStartup -Executable $Controller -Action status
    if (-not $state.Registered -or -not [string]::Equals($state.Executable, $paths.Installed, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The independent launcher is missing or targets another location. Run install.ps1.'
    }
    $running = @(Get-MonitorProcess -ExecutablePaths @($paths.Installed))
    if (-not $Restart -and $state.State -eq 4 -and $running.Count -eq 1) { return $running[0] }
    if ($running.Count -gt 0) { Stop-MonitorProcess -ExecutablePaths @($paths.Installed) }
    Invoke-MonitorStartup -Executable $Controller -Action start | Out-Null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $running = @(Get-MonitorProcess -ExecutablePaths @($paths.Installed))
        if ($running.Count -eq 1) {
            if ($running[0].WaitForExit(2500)) { throw "Monitor exited during startup ($($running[0].ExitCode))." }
            return $running[0]
        }
        Start-Sleep -Milliseconds 100
    } while ($clock.ElapsedMilliseconds -lt 10000)
    $state = Invoke-MonitorStartup -Executable $Controller -Action status
    throw "Task did not launch the installed EXE. Scheduler result: $($state.LastResult)."
}

Export-ModuleMember -Function Get-MonitorPaths,Get-MonitorProcess,Invoke-MonitorCommand,Invoke-MonitorStartup,Stop-MonitorProcess,Copy-MonitorVerified,Start-MonitorIndependent
