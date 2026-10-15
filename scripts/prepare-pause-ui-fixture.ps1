$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskParent=[IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/pause-ui-fixture')).TrimEnd('\')+'\'
$taskWork=Join-Path $taskParent ([Guid]::NewGuid().ToString('N'))
if(-not ([IO.Path]::GetFullPath($taskWork)).StartsWith($taskParent,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected pause fixture directory.'}
$taskSource=Join-Path $taskRoot 'tests/fixtures/generated/large.bin'
$taskDestination=Join-Path $taskRoot 'tests/fixtures/generated/large-ui.7z'
New-Item -ItemType Directory -Force $taskWork | Out-Null
try {
    # Reuse the real 64 MiB source via hard links, without sixteen disk copies.
    # Independent blocks and a longer output window keep both GUI workers active
    # while the actual pause request reaches them on a busy runner.
    $taskInputs=foreach($taskIndex in 0..15){
        $taskName=if($taskIndex -eq 0){'large.bin'}else{"extra-$taskIndex.bin"}
        $taskLink=Join-Path $taskWork $taskName
        New-Item -ItemType HardLink -Path $taskLink -Target $taskSource | Out-Null
        $taskLink
    }
    $taskArchive=Join-Path $taskWork 'pause-ui.7z'
    $taskArguments=@('a','-t7z','-mx=1','-md=4m','-ms=off','-mmt=2','-y',$taskArchive)+@($taskInputs)
    & (Join-Path $taskRoot 'engines/26.04/7z.exe') @taskArguments | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Non-solid pause UI fixture creation failed.'}
    Copy-Item -LiteralPath $taskArchive -Destination $taskDestination -Force
} finally {
    $taskResolved=[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $taskWork).Path)
    if(-not $taskResolved.StartsWith($taskParent,[StringComparison]::OrdinalIgnoreCase)){throw 'Pause fixture cleanup escaped its directory.'}
    Remove-Item -LiteralPath $taskResolved -Recurse -Force
}
