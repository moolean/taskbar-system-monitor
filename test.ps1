[CmdletBinding()]
param([switch]$Desktop)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $projectRoot 'build.ps1')
$exe = Join-Path $projectRoot 'dist\TaskbarSystemMonitor.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
if (-not $test.WaitForExit(30000)) { throw "Self-test timed out (PID $($test.Id))." }
if ($test.ExitCode -ne 0) { throw "Self-test failed: $($test.ExitCode). See error.log." }
Write-Host 'PASS: sampling, settings persistence, and 108 layout combinations.'
if ($Desktop) {
    $report = Join-Path $projectRoot 'dist\desktop-test.txt'
    $test = Start-Process -FilePath $exe -ArgumentList ('"--appbar-test=' + $report + '"') -WindowStyle Hidden -PassThru
    if (-not $test.WaitForExit(20000)) { throw "Desktop test timed out (PID $($test.Id))." }
    if ($test.ExitCode -ne 0) { throw "Desktop test failed: $($test.ExitCode). Code 21 means the monitor is already running; exit it first." }
    Get-Content -LiteralPath $report
}
