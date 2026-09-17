$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$scripts = @(Get-ChildItem -LiteralPath $project -Filter '*.ps1') + @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in '.ps1','.psm1' })
foreach ($script in $scripts) {
    $tokens = $null; $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
}
Import-Module (Join-Path $PSScriptRoot 'MonitorTools.psm1') -Force -DisableNameChecking
$paths = Get-MonitorPaths
if ([IO.Path]::GetFullPath($paths.Project) -ne [IO.Path]::GetFullPath($project)) { throw 'Project path resolution failed.' }
if ((Split-Path -Leaf $paths.Installed) -ne 'TaskbarSystemMonitor.exe') { throw 'Installed executable path is invalid.' }
if ($paths.Installed -eq $paths.Source) { throw 'Build and installed runtime must be separate.' }
Write-Host "PASS: $($scripts.Count) PowerShell scripts parse; shared paths are consistent."
