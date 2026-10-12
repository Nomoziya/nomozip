param([switch]$SkipEngineValidation)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot '.tools/nuget-http'
$env:NUGET_SCRATCH = Join-Path $taskRoot '.tools/nuget-scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
foreach ($taskProject in @('EngineSmoke','CoreValidation','UpdateValidation')) {
    & $taskDotnet build (Join-Path $taskRoot "tests/$taskProject/$taskProject.csproj") --nologo -m:1 -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "$taskProject build failed." }
}
$taskPython = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $taskPython -or $taskPython -like '*WindowsApps*') {
    $taskPython = (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe')
}
if (-not $SkipEngineValidation) {
    & $taskPython (Join-Path $taskRoot 'tests/engine_validation.py')
    if ($LASTEXITCODE -ne 0) { throw 'Engine validation failed.' }
}
& $taskDotnet build (Join-Path $taskRoot 'tools/UpdatePackager/UpdatePackager.csproj') --nologo -m:1 -nr:false -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Update packager build failed.' }
$taskPackager = Join-Path $taskRoot 'tools/UpdatePackager/bin/Debug/net10.0-windows/UpdatePackager.dll'
$taskTestAssets = Join-Path $taskRoot 'artifacts/update-test-assets'
New-Item -ItemType Directory -Force $taskTestAssets | Out-Null
$taskTestPrivate = Join-Path $taskTestAssets 'private.pem'
$taskTestPublic = Join-Path $taskTestAssets 'public.pem'
if (-not (Test-Path $taskTestPrivate)) {
    & $taskDotnet $taskPackager keygen $taskTestPrivate $taskTestPublic
    if ($LASTEXITCODE -ne 0) { throw 'Test signing key creation failed.' }
}
foreach ($taskVersion in @('26.03','26.04')) {
    $taskTestPackage = Join-Path $taskTestAssets "ArchivePassword-engine-$taskVersion-win-x64.zip"
    if (Test-Path $taskTestPackage) { Remove-Item -LiteralPath $taskTestPackage }
    & $taskDotnet $taskPackager pack (Join-Path $taskRoot "engines/$taskVersion") $taskVersion (Join-Path $taskRoot 'artifacts/engine-validation.json') $taskTestPrivate $taskTestPackage $taskTestPublic
    if ($LASTEXITCODE -ne 0) { throw 'Test update packaging failed.' }
}
& $taskDotnet (Join-Path $taskRoot 'tests/CoreValidation/bin/Debug/net10.0-windows/CoreValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Core validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/UpdateValidation/bin/Debug/net10.0-windows/UpdateValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Update validation failed.' }
$taskTestApp = Join-Path $taskRoot 'artifacts/test-app'
New-Item -ItemType Directory -Force $taskTestApp | Out-Null
Copy-Item (Join-Path $taskRoot 'artifacts/app/*') $taskTestApp -Recurse -Force
Copy-Item $taskTestPublic (Join-Path $taskTestApp 'update-public-key.pem') -Force
$taskApplication = Join-Path $taskTestApp 'ArchivePassword.exe'
$taskGui = Start-Process -FilePath $taskApplication -ArgumentList @('--validate', $taskRoot) -WindowStyle Hidden -Wait -PassThru
if ($taskGui.ExitCode -ne 0) { throw 'Desktop command validation failed.' }
Write-Output 'Desktop command validation passed.'
