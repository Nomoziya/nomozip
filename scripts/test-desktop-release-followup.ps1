param([ValidateSet('x64','arm64')][string]$Architecture = 'x64',
    [string[]]$Flags=@('validate','validate-vault-transfer','validate-password-organization','validate-file-hashes','validate-shell-ui','validate-application-updates','validate-extraction-tools','validate-selection','validate-encoding','validate-origin','validate-dpi','validate-queue','validate-concurrency','validate-pause','validate-notifications','validate-sfx','validate-formats','validate-volumes','validate-checkpoints','validate-drag-out','validate-content-preview','validate-conversion','validate-archive-editing','validate-master-vault'),
    [switch]$SkipPrepare,[switch]$SkipLanguages)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Release follow-up requires disposable CI.'}
$taskRoot=Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools/dotnet-home'
$taskDotnet=Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if(-not $SkipPrepare){
foreach($taskProject in @('tests/EngineSmoke/EngineSmoke.csproj','tests/CompressionValidation/CompressionValidation.csproj','tests/CheckpointValidation/CheckpointValidation.csproj','tests/EncodingValidation/EncodingValidation.csproj','tools/UpdatePackager/UpdatePackager.csproj')){
    & $taskDotnet build (Join-Path $taskRoot $taskProject) --nologo -m:1 -nr:false
    if($LASTEXITCODE -ne 0){throw "Follow-up fixture build failed: $taskProject"}
}
if($Architecture -eq 'arm64'){foreach($taskVersion in @('26.03','26.04')){Copy-Item "$taskRoot/engines/arm64/$taskVersion/*" "$taskRoot/engines/$taskVersion" -Force}}
python "$taskRoot/tests/engine_validation.py"
if($LASTEXITCODE -ne 0){throw 'Follow-up real-engine fixtures failed.'}
& $taskDotnet "$taskRoot/tests/CompressionValidation/bin/Debug/net10.0-windows/CompressionValidation.dll" $taskRoot
if($LASTEXITCODE -ne 0){throw 'Follow-up compression evidence failed.'}
& $taskDotnet "$taskRoot/tests/EncodingValidation/bin/Debug/net10.0-windows/EncodingValidation.dll" $taskRoot
if($LASTEXITCODE -ne 0){throw 'Follow-up filename encoding fixtures failed.'}
$taskAssets=Join-Path $taskRoot 'artifacts/update-test-assets'
New-Item -ItemType Directory -Force $taskAssets | Out-Null
$taskPackager=Join-Path $taskRoot 'tools/UpdatePackager/bin/Debug/net10.0-windows/UpdatePackager.dll'
& $taskDotnet $taskPackager keygen "$taskAssets/private.pem" "$taskAssets/public.pem"
if($LASTEXITCODE -ne 0){throw 'Synthetic signing fixture failed.'}
foreach($taskVersion in @('26.03','26.04')){
    & $taskDotnet $taskPackager pack "$taskRoot/engines/$taskVersion" $taskVersion "$taskRoot/artifacts/engine-validation.json" "$taskAssets/private.pem" "$taskAssets/ArchivePassword-engine-$taskVersion-win-$Architecture.zip" "$taskAssets/public.pem" "$taskRoot/artifacts/compression-validation.json"
    if($LASTEXITCODE -ne 0){throw 'Synthetic signed update fixture failed.'}
}
$taskApp=Join-Path $taskRoot 'artifacts/test-app'
New-Item -ItemType Directory -Force $taskApp | Out-Null
Copy-Item "$taskRoot/artifacts/app/*" $taskApp -Recurse -Force
Copy-Item "$taskAssets/public.pem" "$taskApp/update-public-key.pem" -Force
& "$taskRoot/scripts/build-shell-test-client.ps1" -Architecture $Architecture -Library -Source "$taskRoot/tests/ArchitectureValidation/notification.cpp" -Output "$taskApp/NativeNotificationProbe.dll"
& "$taskRoot/scripts/prepare-pause-ui-fixture.ps1"
}
$taskApp=Join-Path $taskRoot 'artifacts/test-app'
$taskFailures=[Collections.Generic.List[string]]::new()
foreach($taskFlag in $Flags){
    foreach($taskLanguage in $(if($taskFlag -eq 'validate-application-updates'){@('zh-CN','en')}else{@('zh-CN')})){
        Write-Output "Follow-up starting: $taskFlag $taskLanguage"
        $taskProcess=Start-Process "$taskApp/ArchivePassword.exe" -ArgumentList @("--$taskFlag",$taskRoot,$taskLanguage) -WindowStyle Hidden -PassThru
        if(-not $taskProcess.WaitForExit(300000)){$taskProcess.Kill($true);$taskProcess.WaitForExit();$taskFailures.Add("$taskFlag $taskLanguage timeout");continue}
        if($taskProcess.ExitCode -ne 0){$taskFailures.Add("$taskFlag $taskLanguage");Write-Warning "Follow-up failed: $taskFlag $taskLanguage"}else{Write-Output "Follow-up passed: $taskFlag $taskLanguage"}
    }
}
if(-not $SkipLanguages){
    try{& "$taskRoot/scripts/test-localization-ui.ps1" -ApplicationDirectory $taskApp}catch{$taskFailures.Add("localization: $_")}
    try{& "$taskRoot/scripts/test-theme-ui.ps1" -ApplicationDirectory $taskApp}catch{$taskFailures.Add("theme: $_")}
}
if($taskFailures.Count){throw "Release follow-up failures: $($taskFailures -join '; ')"}
