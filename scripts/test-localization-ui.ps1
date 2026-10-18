param([Parameter(Mandatory)][string]$ApplicationDirectory)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskApp=Join-Path ([IO.Path]::GetFullPath($ApplicationDirectory)) 'ArchivePassword.exe'
$taskChecks=[ordered]@{}
$taskLanguageFailed=$false
foreach($taskLanguage in @('zh-CN','en')){
    $taskProfile=Join-Path $taskRoot ('artifacts/localization-ui/'+$taskLanguage+'-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $taskProfile | Out-Null
    @{Version=1;Language=$taskLanguage} | ConvertTo-Json | Set-Content (Join-Path $taskProfile 'ui-language.json') -Encoding utf8
    $taskArguments=@('--validate-localization',('"'+$taskRoot+'"'),'stored','--data-dir',('"'+$taskProfile+'"'))
    $taskGui=Start-Process $taskApp -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    if(-not $taskGui.WaitForExit(300000)){$taskGui.Kill($true);$taskGui.WaitForExit();$taskLanguageFailed=$true;Write-Warning "Localized desktop acceptance timed out: $taskLanguage";continue}
    if($taskGui.ExitCode -ne 0){$taskLanguageFailed=$true;Write-Warning "Localized desktop acceptance failed: $taskLanguage";continue}
    $taskBefore=@{};foreach($taskName in @('settings.json','passwords.apv')){$taskBefore[$taskName]=(Get-FileHash (Join-Path $taskProfile $taskName)).Hash}
    $taskArguments[0]='--validate-language-restart'
    $taskRestart=Start-Process $taskApp -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    if(-not $taskRestart.WaitForExit(30000)){$taskRestart.Kill($true);$taskRestart.WaitForExit();$taskLanguageFailed=$true;Write-Warning "Localized desktop restart timed out: $taskLanguage";continue}
    $taskResult=Get-Content (Join-Path $taskProfile 'restart-result.json') -Raw | ConvertFrom-Json
    $taskExpected=if($taskLanguage -eq 'en'){'zh-CN'}else{'en'}
    $taskChecks["$taskLanguage separate process reads saved culture"]=$taskRestart.ExitCode -eq 0 -and $taskResult.passed -and $taskResult.culture -eq $taskExpected
    $taskChecks["$taskLanguage restart preserves settings and vault"]=@($taskBefore.Keys | Where-Object {(Get-FileHash (Join-Path $taskProfile $_)).Hash -ne $taskBefore[$_]}).Count -eq 0
}
$taskPassed=-not $taskLanguageFailed -and $taskChecks.Count -eq 4 -and @($taskChecks.Values | Where-Object {$_ -ne $true}).Count -eq 0
@{passed=$taskPassed;total=$taskChecks.Count;checks=$taskChecks;syntheticData=$true} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskRoot 'artifacts/localization-startup-validation.json') -Encoding utf8
if(-not $taskPassed){throw 'Language restart acceptance failed.'}
