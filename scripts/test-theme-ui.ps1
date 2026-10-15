param([Parameter(Mandatory)][string]$ApplicationDirectory)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskApp=Join-Path ([IO.Path]::GetFullPath($ApplicationDirectory)) 'ArchivePassword.exe'
$taskChecks=[ordered]@{}
foreach($taskLanguage in @('zh-CN','en')){
    $taskProfile=Join-Path $taskRoot ('artifacts/theme-ui/'+$taskLanguage+'-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $taskProfile | Out-Null
    @{Version=1;Language=$taskLanguage} | ConvertTo-Json | Set-Content (Join-Path $taskProfile 'ui-language.json') -Encoding utf8
    @{Version=1;Mode='dark'} | ConvertTo-Json | Set-Content (Join-Path $taskProfile 'appearance.json') -Encoding utf8
    $taskArguments=@('--validate-theme',('"'+$taskRoot+'"'),'stored','--data-dir',('"'+$taskProfile+'"'))
    $taskProcess=Start-Process $taskApp -ArgumentList $taskArguments -WindowStyle Hidden -Wait -PassThru
    if($taskProcess.ExitCode -ne 0){throw "Dark theme desktop acceptance failed: $taskLanguage"}
    $taskBefore=@{};foreach($taskName in @('settings.json','passwords.apv','ui-language.json','appearance.json')){$taskBefore[$taskName]=(Get-FileHash (Join-Path $taskProfile $taskName)).Hash}
    $taskArguments[0]='--validate-theme-restart'
    $taskRestart=Start-Process $taskApp -ArgumentList $taskArguments -WindowStyle Hidden -Wait -PassThru
    $taskResult=Get-Content (Join-Path $taskProfile 'theme-restart-result.json') -Raw | ConvertFrom-Json
    $taskChecks["$taskLanguage independent process restores dark mode"]=$taskRestart.ExitCode -eq 0 -and $taskResult.passed -and $taskResult.mode -eq 'dark' -and ($taskResult.highContrast -or $taskResult.dark)
    $taskChecks["$taskLanguage restart preserves existing profile files"]=@($taskBefore.Keys | Where-Object {(Get-FileHash (Join-Path $taskProfile $_)).Hash -ne $taskBefore[$_]}).Count -eq 0
}
$taskPassed=@($taskChecks.Values | Where-Object {$_ -ne $true}).Count -eq 0
@{passed=$taskPassed;total=$taskChecks.Count;checks=$taskChecks;syntheticData=$true} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskRoot 'artifacts/theme-startup-validation.json') -Encoding utf8
if(-not $taskPassed){throw 'Theme restart acceptance failed.'}
