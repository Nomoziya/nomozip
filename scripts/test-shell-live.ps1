$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Live menu verification only runs on disposable GitHub VMs.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskApp = Join-Path $taskRoot 'artifacts/app/ArchivePassword.exe'
$taskClient = Join-Path $taskRoot 'artifacts/ShellMenuProbe.exe'
& g++ -std=c++17 -O2 -municode -static -static-libgcc -static-libstdc++ (Join-Path $taskRoot 'tests/ShellValidation/menu.cpp') -lshell32 -lole32 -luuid -o $taskClient
if ($LASTEXITCODE -ne 0) { throw 'Windows Shell menu probe compilation failed.' }
$taskTest = Join-Path $taskRoot 'tests/ShellValidation/bin/Debug/net10.0-windows/ShellValidation.dll'
$taskServer = $null
try {
    & (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') $taskTest $taskRoot $taskApp register-live
    if ($LASTEXITCODE -ne 0) { throw 'Disposable menu registration failed.' }
    $taskServer = Start-Process -FilePath $taskApp -ArgumentList @('--shell-server','--validate-shell-server',('"' + $taskRoot + '"')) -WindowStyle Hidden -PassThru
    $taskReady = Join-Path $taskRoot ('artifacts/shell-server-ready-' + $taskServer.Id + '.json')
    $taskClock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $taskReady) -and $taskClock.ElapsedMilliseconds -lt 15000) { Start-Sleep -Milliseconds 50 }
    if (-not (Test-Path $taskReady)) { throw 'Controlled Shell server did not become ready.' }
    & $taskClient (Join-Path $taskRoot 'tests/fixtures/generated/encrypted-header.7z') (Join-Path $taskRoot 'artifacts/shell-live-validation.json')
    if ($LASTEXITCODE -ne 0) { throw 'Actual Shell menu enumeration/invocation failed.' }
    if (-not $taskServer.WaitForExit(30000) -or $taskServer.ExitCode -ne 0) { throw 'Shell-dispatched desktop process failed.' }
    $taskResult = Get-Content -Raw (Join-Path $taskRoot 'artifacts/shell-server-Compress-validation.json') | ConvertFrom-Json
    if (-not $taskResult.passed -or $taskResult.selected -ne 1) { throw 'Actual Shell menu lost its selection.' }
} finally {
    if ($taskServer -and -not $taskServer.HasExited) { Stop-Process -Id $taskServer.Id }
    & (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') $taskTest $taskRoot $taskApp remove-live
}
