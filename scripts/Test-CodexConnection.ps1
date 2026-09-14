param([switch]$Standalone)
$ErrorActionPreference = 'Stop'
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = (Get-Command codex.exe).Source
$start.Arguments = if ($Standalone) { 'app-server --stdio' } else { 'app-server proxy' }
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$probe = New-Object System.Diagnostics.Process
$probe.StartInfo = $start
try {
    $probe.Start() | Out-Null
    $errors = $probe.StandardError.ReadToEndAsync()
    $probe.StandardInput.WriteLine('{"id":1,"method":"initialize","params":{"clientInfo":{"name":"taskbar_monitor","title":"Taskbar Monitor","version":"3.0.0"}}}')
    $probe.StandardInput.Flush()
    $ready = $false
    while (-not $probe.HasExited) {
        $line = $probe.StandardOutput.ReadLineAsync()
        if (-not $line.Wait(15000)) { throw 'Codex read-only probe timed out' }
        if ($null -eq $line.Result) { break }
        $message = $line.Result | ConvertFrom-Json
        if ($message.id -eq 1) {
            if ($message.error) { throw 'Codex initialize failed' }
            $ready = $true
            $probe.StandardInput.WriteLine('{"method":"initialized"}')
            $probe.StandardInput.WriteLine('{"id":2,"method":"account/rateLimits/read"}')
            $probe.StandardInput.Flush()
        }
        if ($message.id -eq 2) {
            if ($message.error) { throw ('Codex quota read failed: ' + $message.error.code) }
            $message.result.rateLimitsByLimitId | ConvertTo-Json -Depth 8
            break
        }
    }
    if (-not $ready) { Write-Output 'No running Codex app-server connection.' }
} finally {
    if (-not $probe.HasExited) { $probe.StandardInput.Close(); if (-not $probe.WaitForExit(2000)) { $probe.Kill() } }
    $probe.Dispose()
}
