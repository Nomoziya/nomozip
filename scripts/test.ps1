param([switch]$SkipEngineValidation)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot '.tools/nuget-http'
$env:NUGET_SCRATCH = Join-Path $taskRoot '.tools/nuget-scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
foreach ($taskProject in @('EngineSmoke','CoreValidation','UpdateValidation','CompressionValidation','AssociationValidation')) {
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
& $taskDotnet (Join-Path $taskRoot 'tests/CompressionValidation/bin/Debug/net10.0-windows/CompressionValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Compression validation failed.' }
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
    & $taskDotnet $taskPackager pack (Join-Path $taskRoot "engines/$taskVersion") $taskVersion (Join-Path $taskRoot 'artifacts/engine-validation.json') $taskTestPrivate $taskTestPackage $taskTestPublic (Join-Path $taskRoot 'artifacts/compression-validation.json')
    if ($LASTEXITCODE -ne 0) { throw 'Test update packaging failed.' }
}
$taskRejectedReport = Join-Path $taskTestAssets 'mismatched-compression-report.json'
$taskWrongEvidence = Get-Content -Raw (Join-Path $taskRoot 'artifacts/compression-validation.json') | ConvertFrom-Json
$taskWrongEvidence.engineHashes.'26.04' = '0' * 64
$taskWrongEvidence | ConvertTo-Json -Depth 8 | Set-Content $taskRejectedReport -Encoding utf8
$taskRejectedPackage = Join-Path $taskTestAssets 'must-not-be-signed.zip'
if (Test-Path $taskRejectedPackage) { throw 'A previously rejected signing artifact exists; inspect it before continuing.' }
$taskSigner = [Diagnostics.ProcessStartInfo]::new($taskDotnet)
$taskSigner.UseShellExecute = $false; $taskSigner.CreateNoWindow = $true
$taskSigner.RedirectStandardOutput = $true; $taskSigner.RedirectStandardError = $true
foreach ($taskArgument in @($taskPackager,'pack',(Join-Path $taskRoot 'engines/26.04'),'26.04',(Join-Path $taskRoot 'artifacts/engine-validation.json'),$taskTestPrivate,$taskRejectedPackage,$taskTestPublic,$taskRejectedReport)) { $taskSigner.ArgumentList.Add($taskArgument) }
$taskRejection = [Diagnostics.Process]::Start($taskSigner)
$taskOutputDrain = $taskRejection.StandardOutput.ReadToEndAsync(); $taskErrorDrain = $taskRejection.StandardError.ReadToEndAsync()
$taskRejection.WaitForExit(); $taskOutputDrain.GetAwaiter().GetResult() | Out-Null; $taskErrorDrain.GetAwaiter().GetResult() | Out-Null
$taskSigningPassed = $taskRejection.ExitCode -ne 0 -and -not (Test-Path $taskRejectedPackage)
@{passed=$taskSigningPassed;compressionEvidenceHashRejection=$taskSigningPassed} | ConvertTo-Json | Set-Content (Join-Path $taskRoot 'artifacts/signing-validation.json') -Encoding utf8
if (-not $taskSigningPassed) { throw 'A mismatched compression report was accepted by the signing tool.' }
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
