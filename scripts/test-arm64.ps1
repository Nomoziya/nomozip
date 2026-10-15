# Native product acceptance only on disposable GitHub ARM64 Windows machines.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ARCH -ne 'ARM64' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'ARM64 integration acceptance requires the disposable native runner.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$taskFailures = [Collections.Generic.List[string]]::new()
$taskProjects = @('EngineSmoke','CoreValidation','UpdateValidation','CompressionValidation','AssociationValidation','BrowserValidation','MultiPasswordValidation','VaultTransferValidation','ShellValidation','ApplicationUpdateValidation','ExtractionToolsValidation','SelectionValidation','PasswordOrganizationValidation','FileHashValidation','EncodingValidation','OriginValidation','QueueValidation','PauseValidation','MasterVaultValidation','ArchiveEditingValidation','ConversionValidation','PreviewValidation','DragValidation','CliValidation','VolumeValidation','SfxValidation','FormatValidation','ArchitectureValidation','LocalizationValidation','AppearanceValidation')
foreach ($taskProject in $taskProjects + @('UpdatePackager')) {
    $taskPrefix = if ($taskProject -eq 'UpdatePackager') { 'tools' } else { 'tests' }
    & $taskDotnet build "$taskRoot/$taskPrefix/$taskProject/$taskProject.csproj" --nologo -m:1 -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Native test build failed: $taskProject" }
}
# The disposable test root presents native engines at the established fixture paths.
# Canonical tracked manifests are restored before source/release packaging.
foreach ($taskVersion in @('26.04','26.03')) {
    Copy-Item "$taskRoot/engines/arm64/$taskVersion/*" "$taskRoot/engines/$taskVersion" -Force
}
python "$taskRoot/tests/engine_validation.py"
if ($LASTEXITCODE -ne 0) { throw 'Native baseline engine matrix failed.' }
foreach ($taskProject in $taskProjects | Where-Object { $_ -notin @('EngineSmoke','UpdateValidation','CoreValidation','CliValidation') }) {
    & $taskDotnet "$taskRoot/tests/$taskProject/bin/Debug/net10.0-windows/$taskProject.dll" $taskRoot
    if ($LASTEXITCODE -ne 0) { $taskFailures.Add("data: $taskProject") }
}
$taskAssets=Join-Path $taskRoot 'artifacts/update-test-assets';New-Item -ItemType Directory -Force $taskAssets | Out-Null
$taskPackager=Join-Path $taskRoot 'tools/UpdatePackager/bin/Debug/net10.0-windows/UpdatePackager.dll'
& $taskDotnet $taskPackager keygen "$taskAssets/private.pem" "$taskAssets/public.pem"
if ($LASTEXITCODE -ne 0) { throw 'Synthetic key generation failed.' }
foreach ($taskVersion in @('26.03','26.04')) {
    & $taskDotnet $taskPackager pack "$taskRoot/engines/$taskVersion" $taskVersion "$taskRoot/artifacts/engine-validation.json" "$taskAssets/private.pem" "$taskAssets/ArchivePassword-engine-$taskVersion-win-arm64.zip" "$taskAssets/public.pem" "$taskRoot/artifacts/compression-validation.json"
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic native update signing failed.' }
}
foreach ($taskProject in @('CoreValidation','UpdateValidation')) {
    & $taskDotnet "$taskRoot/tests/$taskProject/bin/Debug/net10.0-windows/$taskProject.dll" $taskRoot
    if ($LASTEXITCODE -ne 0) { throw "Native acceptance failed: $taskProject" }
}
& $taskDotnet "$taskRoot/tests/CliValidation/bin/Debug/net10.0-windows/CliValidation.dll" $taskRoot "$taskRoot/artifacts/app"
if ($LASTEXITCODE -ne 0) { $taskFailures.Add('console entry') }
$taskApp=Join-Path $taskRoot 'artifacts/test-app';New-Item -ItemType Directory -Force $taskApp | Out-Null
Copy-Item "$taskRoot/artifacts/app/*" $taskApp -Recurse -Force
Copy-Item "$taskAssets/public.pem" "$taskApp/update-public-key.pem" -Force
& "$taskRoot/scripts/build-shell-test-client.ps1" -Architecture arm64 -Library -Source "$taskRoot/tests/ArchitectureValidation/notification.cpp" -Output "$taskApp/NativeNotificationProbe.dll"
$taskUiFixture=Get-ChildItem "$taskRoot/artifacts/pause-data" -Recurse -File -Filter 'longer-26.04.7z' | Select-Object -First 1
if (-not $taskUiFixture) { throw 'Native in-flight UI fixture was not prepared.' }
Copy-Item $taskUiFixture.FullName "$taskRoot/tests/fixtures/generated/large-ui.7z"
foreach ($taskFlag in @('validate','validate-vault-transfer','validate-password-organization','validate-file-hashes','validate-shell-ui','validate-application-updates','validate-extraction-tools','validate-selection','validate-encoding','validate-origin','validate-dpi','validate-queue','validate-concurrency','validate-pause','validate-notifications','validate-sfx','validate-formats','validate-volumes','validate-archive-editing','validate-conversion','validate-content-preview','validate-drag-out','validate-master-vault')) {
    $taskGui=Start-Process "$taskApp/ArchivePassword.exe" -ArgumentList @("--$taskFlag",$taskRoot) -WindowStyle Hidden -Wait -PassThru
    if ($taskGui.ExitCode -ne 0) { $taskFailures.Add("desktop: $taskFlag") }
}
$taskEnglishUpdate=Start-Process "$taskApp/ArchivePassword.exe" -ArgumentList @('--validate-application-updates',$taskRoot,'en') -WindowStyle Hidden -Wait -PassThru
if ($taskEnglishUpdate.ExitCode -ne 0) { $taskFailures.Add('desktop: English application updates') }
try { & "$taskRoot/scripts/test-localization-ui.ps1" -ApplicationDirectory $taskApp } catch { $taskFailures.Add("desktop: localization: $_") }
try { & "$taskRoot/scripts/test-theme-ui.ps1" -ApplicationDirectory $taskApp } catch { $taskFailures.Add("desktop: theme: $_") }
# ARM64 SDK builds AnyCPU fixture code; the separately verified x64 runtime actually writes old vaults.
$taskMetadata=Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$taskRuntime=($taskMetadata.releases | Where-Object { $_.runtime.version -eq '10.0.12' } | Select-Object -First 1).runtime.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -eq 'dotnet-runtime-win-x64.zip' } | Select-Object -First 1
if (-not $taskRuntime) { throw 'Pinned synthetic x64 runtime not found in official metadata.' }
$taskZip=Join-Path $taskRoot '.tools/cross-architecture-runtime.zip';Invoke-WebRequest $taskRuntime.url -OutFile $taskZip
if ((Get-FileHash $taskZip -Algorithm SHA512).Hash -ne $taskRuntime.hash) { throw 'Synthetic x64 runtime checksum mismatch.' }
$taskRuntimeRoot=Join-Path $taskRoot '.tools/cross-architecture-runtime';Expand-Archive -LiteralPath $taskZip -DestinationPath $taskRuntimeRoot
$taskVaultTest=Join-Path $taskRoot 'tests/ArchitectureValidation/bin/Debug/net10.0-windows/ArchitectureValidation.dll'
& "$taskRuntimeRoot/dotnet.exe" $taskVaultTest $taskRoot export-vault-x64
if ($LASTEXITCODE -ne 0) { throw 'Actual x64 vault export on ARM64 Windows failed.' }
& $taskDotnet $taskVaultTest $taskRoot import-vault-arm64
if ($LASTEXITCODE -ne 0) { throw 'Actual native vault reuse failed.' }
& "$taskRuntimeRoot/dotnet.exe" $taskVaultTest $taskRoot verify-vault-x64
if ($LASTEXITCODE -ne 0) { throw 'x64 reread of native edits failed.' }
if ($taskFailures.Count) { throw "Native acceptance failures: $($taskFailures -join ', ')" }
Write-Output 'Native ARM64 data, desktop, update and cross-architecture vault acceptance passed.'
