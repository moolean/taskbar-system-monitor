function Stop-KnownMonitor {
    param([string[]]$ExecutablePaths)
    $targets = @(Get-Process -Name 'TaskbarSystemMonitor' -ErrorAction SilentlyContinue |
        Where-Object { $ExecutablePaths -contains $_.Path })
    foreach ($target in $targets) {
        if ($target.HasExited) { continue }
        if ([version]$target.FileVersion -ge [version]'2.0.0.0') {
            $signal = Start-Process -FilePath $target.Path -ArgumentList '--exit' -WindowStyle Hidden -PassThru
            if (-not $signal.WaitForExit(3000)) { throw 'Exit command did not complete; close the monitor from its tray menu and retry.' }
        } else {
            $target.CloseMainWindow() | Out-Null
        }
        if (-not $target.WaitForExit(5000)) {
            throw 'The old monitor is still running. Use its tray menu > Exit, then retry. It was not forcibly terminated so it can restore taskbar space.'
        }
    }
}
