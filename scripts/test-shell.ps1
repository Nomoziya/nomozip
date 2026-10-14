param([string]$ApplicationDirectory = 'artifacts/app', [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskApp = Join-Path ([IO.Path]::GetFullPath((Join-Path $taskRoot $ApplicationDirectory))) 'ArchivePassword.exe'
$taskValidation = Join-Path $taskRoot ('artifacts/shell-validation/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $taskValidation | Out-Null
$taskClient = Join-Path $taskValidation 'ShellClient.exe'
& (Join-Path $PSScriptRoot 'build-shell-test-client.ps1') -Source (Join-Path $taskRoot 'tests/ShellValidation/client.cpp') -Output $taskClient -Architecture $Architecture
$taskChecks = [ordered]@{}
foreach ($taskCase in @('ExtractHere','ExtractFolder','Compress','LargeCompress')) {
    $taskAction = if ($taskCase -eq 'LargeCompress') { 'Compress' } else { $taskCase }
    $taskPaths = @()
    foreach ($taskParent in @("$taskAction-A 中文 ' &", "$taskAction-B")) {
        $taskFolder = Join-Path $taskValidation $taskParent
        New-Item -ItemType Directory -Force $taskFolder | Out-Null
        $taskArchive = Join-Path $taskFolder '归档 空格 $ &.7z'
        Copy-Item -LiteralPath (Join-Path $taskRoot 'tests/fixtures/generated/encrypted-header.7z') -Destination $taskArchive
        $taskPaths += $taskArchive
    }
    if ($taskAction -eq 'Compress') { $taskPaths += (Split-Path $taskPaths[0] -Parent) }
    if ($taskCase -eq 'LargeCompress') {
        $taskPaths = @()
        foreach ($taskNumber in 1..300) {
            $taskSource = Join-Path $taskValidation ('长名称-' + $taskNumber.ToString('000') + '-' + ('x' * 95) + '.txt')
            [IO.File]::WriteAllText($taskSource, 'synthetic bulk selection'); $taskPaths += $taskSource
        }
        $taskList = Join-Path $taskValidation 'large-paths.json'
        [IO.File]::WriteAllText($taskList, ($taskPaths | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    }
    $taskErrorLog = Join-Path $taskValidation ($taskCase + '-server-error.log')
    $taskOutputLog = Join-Path $taskValidation ($taskCase + '-server-output.log')
    $taskServer = Start-Process -FilePath $taskApp -ArgumentList @('--shell-server','--validate-shell-server',('"' + $taskRoot + '"')) -WindowStyle Hidden -PassThru -RedirectStandardError $taskErrorLog -RedirectStandardOutput $taskOutputLog
    try {
        # The server already publishes this marker after all three class factories register.
        # Wait for this process, as in test-shell-live.ps1, before asking COM to activate it.
        $taskReady = Join-Path $taskRoot ('artifacts/shell-server-ready-' + $taskServer.Id + '.json')
        $taskDeadline = [DateTime]::UtcNow.AddSeconds(20)
        while (-not (Test-Path -LiteralPath $taskReady)) {
            if ($taskServer.HasExited) {
                $taskDetails = if (Test-Path -LiteralPath $taskErrorLog) { [IO.File]::ReadAllText($taskErrorLog) } else { '' }
                throw "Desktop $taskAction server exited before readiness ($($taskServer.ExitCode)): $taskDetails"
            }
            if ([DateTime]::UtcNow -ge $taskDeadline) { throw "Desktop $taskAction server did not register its COM factories within 20 seconds." }
            Start-Sleep -Milliseconds 100
        }
        $taskIndex = @('ExtractHere','ExtractFolder','Compress').IndexOf($taskAction) + 1
        $taskClsid = '{c708b15d-0d3e-47ab-8fc8-c22d21ff117' + $taskIndex + '}'
        if ($taskCase -eq 'LargeCompress') { & $taskClient $taskClsid (Join-Path $taskValidation 'large-client.json') --paths-file $taskList }
        else { & $taskClient $taskClsid (Join-Path $taskValidation ($taskAction + '-client.json')) @taskPaths }
        if ($LASTEXITCODE -ne 0) { throw "Native $taskAction invocation failed." }
        if (-not $taskServer.WaitForExit(30000)) { throw "Desktop $taskAction acceptance timed out." }
        if ($taskServer.ExitCode -ne 0) { throw "Desktop $taskAction process failed." }
        $taskEvidence = Get-Content -Raw (Join-Path $taskRoot ('artifacts/shell-server-' + $taskAction + '-validation.json')) | ConvertFrom-Json
        $taskChecks[$taskCase + 'FullSelection'] = $taskEvidence.passed -and $taskEvidence.selected -eq $taskPaths.Count
        if ($taskCase -eq 'LargeCompress') { $taskChecks.SelectionExceedsCommandLineLimit = ($taskPaths -join ' ').Length -gt 32767 }
        if ($taskAction -ne 'Compress') {
            foreach ($taskArchive in $taskPaths) {
                $taskDestination = if ($taskAction -eq 'ExtractHere') { Split-Path $taskArchive -Parent } else { Join-Path (Split-Path $taskArchive -Parent) ([IO.Path]::GetFileNameWithoutExtension($taskArchive)) }
                $taskExtracted = Join-Path $taskDestination '中文示例.txt'
                $taskChecks[$taskAction + 'RealOutput' + $taskChecks.Count] = (Test-Path $taskExtracted) -and (Get-FileHash $taskExtracted).Hash -eq (Get-FileHash (Join-Path $taskRoot 'tests/fixtures/generated/中文示例.txt')).Hash
            }
        } else {
            $taskChecks.CompressionAwaitsConfirmation = -not (Test-Path $taskEvidence.compressionOutput)
        }
    } finally {
        if (-not $taskServer.HasExited) { Stop-Process -Id $taskServer.Id }
    }
}
$taskPassed = @($taskChecks.Values | Where-Object { -not $_ }).Count -eq 0
[ordered]@{passed=$taskPassed;total=$taskChecks.Count;realNativeCom=$true;isolatedData=$true;checks=$taskChecks} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskRoot 'artifacts/shell-native-validation.json') -Encoding utf8
if (-not $taskPassed) { throw 'Shell command acceptance failed.' }
Write-Output 'Native multi-selection and real desktop shell flows passed.'
