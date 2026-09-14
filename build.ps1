[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName
$manifestFile = Join-Path $projectRoot 'app.manifest'
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

New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null
# Always compile all sources. -Clean remains accepted for existing callers;
# never delete a whole output directory that might contain user files.
$stagedOutput = Join-Path $distDirectory ('TaskbarSystemMonitor.build-' + [guid]::NewGuid().ToString('N') + '.exe')

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/codepage:65001',
    "/out:$stagedOutput",
    "/win32manifest:$manifestFile",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Xml.Linq.dll'
)

$arguments += $sourceFiles

try {
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
    Move-Item -LiteralPath $stagedOutput -Destination $outputFile -Force
} finally {
    if (Test-Path -LiteralPath $stagedOutput) { Remove-Item -LiteralPath $stagedOutput -Force }
}

Write-Host "Build complete: $outputFile" -ForegroundColor Green
