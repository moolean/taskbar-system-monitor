[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
if (-not (Test-Path -LiteralPath $paths.Installed -PathType Leaf)) { throw 'Run install.ps1 before creating the desktop shortcut.' }
$state = Invoke-MonitorStartup -Executable $paths.Installed -Action status
if (-not $state.Registered -or $state.Executable -ne $paths.Installed) { throw 'The independent launcher is not registered for this installation. Run install.ps1 first.' }
$desktopDir = [Environment]::GetFolderPath('DesktopDirectory')
if (-not $desktopDir -or -not (Test-Path -LiteralPath $desktopDir -PathType Container)) { throw 'Windows desktop folder is unavailable.' }
$shellLink = New-Object -ComObject WScript.Shell
$launchArgs = '--startup-control=start'
try {
    # Never overwrite an unrelated shortcut, including on a redirected desktop.
    foreach ($name in @('任务栏监控.lnk', '任务栏监控（启动）.lnk')) {
        $shortcutPath = Join-Path $desktopDir $name
        $exists = Test-Path -LiteralPath $shortcutPath
        $link = $shellLink.CreateShortcut($shortcutPath)
        try {
            if ($exists) {
                if ($link.TargetPath -ne $paths.Installed -or $link.Arguments -ne $launchArgs) { continue }
            } else {
                $link.TargetPath = $paths.Installed
                $link.Arguments = $launchArgs
                $link.WorkingDirectory = $paths.InstallDirectory
                $link.IconLocation = $paths.Installed + ',0'
                $link.Description = '启动任务栏监控（通过 Windows 计划任务独立运行）'
                $link.Save()
            }
        } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
        $saved = $shellLink.CreateShortcut($shortcutPath)
        try {
            if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf) -or $saved.TargetPath -ne $paths.Installed -or $saved.Arguments -ne $launchArgs) { throw 'Shortcut verification failed.' }
        } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($saved) }
        Write-Output $shortcutPath
        return
    }
    throw 'Both shortcut names already belong to other targets. No shortcut was overwritten.'
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shellLink) }
