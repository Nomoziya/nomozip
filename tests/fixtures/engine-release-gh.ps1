# Offline GitHub CLI fixture. All state, downloads and requests remain beside this script.
$taskArguments = @($args)
$taskStatePath = Join-Path $PSScriptRoot 'gh-state.json'
$taskState = Get-Content -LiteralPath $taskStatePath -Raw | ConvertFrom-Json
$taskTag = $taskState.tag
$taskPackage = "ArchivePassword-engine-26.04-win-$($taskState.architecture).zip"
[IO.File]::AppendAllText((Join-Path $PSScriptRoot 'gh-calls.jsonl'), (ConvertTo-Json -InputObject $taskArguments -Compress) + "`n")
$global:LASTEXITCODE = 0
if ($taskState.apiFailure) { $global:LASTEXITCODE = 1; return }
if ($taskArguments[0] -eq 'api') {
    if ($taskArguments[1].EndsWith('/latest')) {
        @{tag_name=$(if($taskState.wrongLatest){'v0.1.0'}else{$taskTag});draft=$false;prerelease=$false} | ConvertTo-Json -Compress
    } else { if($taskState.exists){ConvertTo-Json -InputObject @(@(@{tag_name=$taskTag})) -Depth 4 -Compress}else{'[[]]'} }
    return
}
switch ($taskArguments[1]) {
    'create' { $taskState.exists=$true; $taskState|ConvertTo-Json|Set-Content -LiteralPath $taskStatePath -Encoding utf8 }
    'view' {
        $taskAssets=@(@{name=$taskPackage},@{name='SHA256SUMS.txt'})
        if($taskState.extra){$taskAssets+=@{name='unexpected.zip'}}
        @{tagName=$taskTag;isDraft=(-not $taskState.published);isPrerelease=$false;assets=$taskAssets;url="https://github.com/Synthetic/nomozip-tests/releases/tag/$taskTag"}|ConvertTo-Json -Depth 5
    }
    'download' {
        $taskDestination=$taskArguments[[array]::IndexOf($taskArguments,'--dir')+1]
        foreach($taskName in @($taskPackage,'SHA256SUMS.txt')) {Copy-Item -LiteralPath (Join-Path $taskState.candidate "assets/$taskName") -Destination (Join-Path $taskDestination $taskName)}
        if($taskState.corrupt){[IO.File]::AppendAllText((Join-Path $taskDestination $taskPackage),'changed')}
    }
    'edit' { $taskState.published=$true; $taskState|ConvertTo-Json|Set-Content -LiteralPath $taskStatePath -Encoding utf8 }
    default {$global:LASTEXITCODE=1}
}
