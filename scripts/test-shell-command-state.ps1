param([Parameter(Mandatory=$true)][string]$LibraryPath, [Parameter(Mandatory=$true)][string]$ClientPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDirectory = Join-Path $taskRoot ('artifacts/shell-command-state/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
$taskFiles = @{}
foreach ($taskPair in @(@('7z','encrypted-header.7z'),@('zip','plain.zip'),@('rar','test_read_format_rar.rar'))) {
    $taskPath = Join-Path $taskDirectory ('source.' + $taskPair[0])
    Copy-Item -LiteralPath (Join-Path $taskRoot ('tests/fixtures/generated/' + $taskPair[1])) -Destination $taskPath
    $taskFiles[$taskPair[0]] = $taskPath
}
$taskPlain = Join-Path $taskDirectory 'plain.txt'; [IO.File]::WriteAllText($taskPlain,'synthetic')
$taskFolder = Join-Path $taskDirectory 'folder.7z'; New-Item -ItemType Directory -Path $taskFolder | Out-Null
$taskCases = @(
    @{name='7z';paths=@($taskFiles['7z']);state=0},
    @{name='ZIP';paths=@($taskFiles['zip']);state=0},
    @{name='RAR';paths=@($taskFiles['rar']);state=0},
    @{name='mixed archive formats';paths=@($taskFiles['7z'],$taskFiles['zip'],$taskFiles['rar']);state=0},
    @{name='plain file';paths=@($taskPlain);state=2},
    @{name='physical directory with archive suffix';paths=@($taskFolder);state=2},
    @{name='archive and unsupported file';paths=@($taskFiles['zip'],$taskPlain);state=2},
    @{name='archive and directory';paths=@($taskFiles['zip'],$taskFolder);state=2})
$taskChecks = [ordered]@{}
foreach ($taskAction in 1,2) {
    foreach ($taskCase in $taskCases) {
        $taskReport = Join-Path $taskDirectory ($taskChecks.Count.ToString() + '.json')
        $taskClsid = '{c708b15d-0d3e-47ab-8fc8-c22d21ff117' + $taskAction + '}'
        & $ClientPath $taskClsid $taskReport --modern-state $LibraryPath $taskCase.state @($taskCase.paths)
        $taskChecks[$taskCase.name + ' action ' + $taskAction] = $LASTEXITCODE -eq 0
    }
}
& $ClientPath '{c708b15d-0d3e-47ab-8fc8-c22d21ff1173}' (Join-Path $taskDirectory 'compress.json') --modern-state $LibraryPath 0 $taskFiles['zip'] $taskFolder $taskPlain
$taskChecks['compression supports files and directory'] = $LASTEXITCODE -eq 0
$taskResult = [ordered]@{passed=($taskChecks.Values -notcontains $false);total=$taskChecks.Count;checks=$taskChecks;realShellMetadata=$true;usesSystemRegistration=$false;invokesOperations=$false;directory=$taskDirectory}
$taskResult | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/shell-command-state-validation.json') -Encoding utf8
if (-not $taskResult.passed) { throw 'Native menu state validation failed.' }
Write-Output 'Native menu state validation passed (17 cases, real Shell items, no registration or invocation).'
