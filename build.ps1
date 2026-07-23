[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceFile = Join-Path $projectRoot 'src\TaskbarSystemMonitor.cs'
$distDirectory = Join-Path $projectRoot 'dist'
$outputFile = Join-Path $distDirectory 'TaskbarSystemMonitor.exe'

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)

$compiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $compiler) {
    throw 'Windows C# compiler (csc.exe) was not found.'
}

if ($Clean -and (Test-Path -LiteralPath $distDirectory)) {
    Remove-Item -LiteralPath $distDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/codepage:65001',
    "/out:$outputFile",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    $sourceFile
)

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Build complete: $outputFile" -ForegroundColor Green
