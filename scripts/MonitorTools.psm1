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
        Invoke-MonitorCommand -Executable $Executable -Arguments @("--startup-control=$Action", ('"--startup-report=' + $report + '"')) -ReportPath $report -TimeoutMs 60000
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
        if ($process.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId) {
            throw 'The monitor is running in a different desktop session; it was not stopped.'
        }
        try {
            $exitSignal = [Threading.EventWaitHandle]::OpenExisting('Local\TaskbarSystemMonitor.moolean.ExitV2')
            try { $null = $exitSignal.Set() } finally { $exitSignal.Dispose() }
        } catch [Threading.WaitHandleCannotBeOpenedException] {
            # It may already have closed its editor/IPC and still be draining.
            Write-Verbose 'Exit endpoint is not open; waiting for the exact process.'
        }
        if (-not $process.WaitForExit(30000)) {
            throw 'The old process has not exited. No force-stop was attempted; check the editor and runtime.log before retrying.'
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

function Assert-MonitorReady {
    param([string]$Controller, [System.Diagnostics.Process]$Monitor)
    if ([version](Get-Item -LiteralPath $Controller).VersionInfo.FileVersion -ge [version]'3.9.1.0') {
        Invoke-MonitorCommand -Executable $Controller -Arguments ("--wait-for-bar=" + $Monitor.Id) -TimeoutMs 60000
    }
    if ($Monitor.HasExited) { throw 'The monitor exited before startup was verified.' }
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
    if (-not $Restart -and $state.State -eq 4 -and $running.Count -eq 1) {
        Assert-MonitorReady -Controller $Controller -Monitor $running[0]
        return $running[0]
    }
    if ($running.Count -gt 0) { Stop-MonitorProcess -ExecutablePaths @($paths.Installed) }
    # IgnoreNew may discard Run while the scheduler still drains the old action.
    $drainClock = [Diagnostics.Stopwatch]::StartNew()
    while ($state.State -eq 4) {
        $arrived = @(Get-MonitorProcess -ExecutablePaths @($paths.Installed))
        if ($arrived.Count -eq 1) {
            Assert-MonitorReady -Controller $Controller -Monitor $arrived[0]
            return $arrived[0]
        }
        if ($drainClock.Elapsed.TotalSeconds -gt 15) { throw 'The old task is still Running. A replacement has not been started.' }
        Start-Sleep -Milliseconds 250
        $state = Invoke-MonitorStartup -Executable $Controller -Action status
    }
    # A lost acknowledgement is not a failed launch: first look for the
    # actual installed window. Don't launch twice or roll back a healthy bar.
    $dispatchError = $null
    try { Start-ScheduledTask -TaskName $state.TaskName -ErrorAction Stop }
    catch { $dispatchError = $_ }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $running = @(Get-MonitorProcess -ExecutablePaths @($paths.Installed))
        if ($running.Count -eq 1) {
            if ($running[0].WaitForExit(2500)) { throw "Monitor exited during startup ($($running[0].ExitCode))." }
            Assert-MonitorReady -Controller $Controller -Monitor $running[0]
            return $running[0]
        }
        Start-Sleep -Milliseconds 100
    } while ($clock.ElapsedMilliseconds -lt 10000)
    $state = Invoke-MonitorStartup -Executable $Controller -Action status
    throw "Task did not launch the installed EXE. Scheduler result: $($state.LastResult). $dispatchError"
}

Export-ModuleMember -Function Get-MonitorPaths,Get-MonitorProcess,Invoke-MonitorCommand,Invoke-MonitorStartup,Stop-MonitorProcess,Copy-MonitorVerified,Start-MonitorIndependent
