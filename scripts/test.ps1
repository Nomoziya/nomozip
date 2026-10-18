param([switch]$SkipEngineValidation,[switch]$SkipDesktop)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot '.tools/nuget-http'
$env:NUGET_SCRATCH = Join-Path $taskRoot '.tools/nuget-scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
foreach ($taskProject in @('EngineSmoke','CoreValidation','UpdateValidation','CompressionValidation','AssociationValidation','BrowserValidation','MultiPasswordValidation','VaultTransferValidation','ShellValidation','ApplicationUpdateValidation','ExtractionToolsValidation','SelectionValidation','PasswordOrganizationValidation','FileHashValidation','EncodingValidation','OriginValidation','QueueValidation','PauseValidation','MasterVaultValidation','ArchiveEditingValidation','ConversionValidation','PreviewValidation','DragValidation','CheckpointValidation','CliValidation','VolumeValidation','SfxValidation','FormatValidation','ArchitectureValidation','LocalizationValidation','AppearanceValidation')) {
    & $taskDotnet build (Join-Path $taskRoot "tests/$taskProject/$taskProject.csproj") --nologo -m:1 -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "$taskProject build failed." }
}
& $taskDotnet (Join-Path $taskRoot 'tests/MasterVaultValidation/bin/Debug/net10.0-windows/MasterVaultValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Master password vault validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/AppearanceValidation/bin/Debug/net10.0-windows/AppearanceValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Appearance preferences validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/LocalizationValidation/bin/Debug/net10.0-windows/LocalizationValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Localization resources and preferences validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/QueueValidation/bin/Debug/net10.0-windows/QueueValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Controlled queue scheduling validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/PauseValidation/bin/Debug/net10.0-windows/PauseValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Cooperative pause validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/VaultTransferValidation/bin/Debug/net10.0-windows/VaultTransferValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Encrypted vault transfer validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/PasswordOrganizationValidation/bin/Debug/net10.0-windows/PasswordOrganizationValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Password organization validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/FileHashValidation/bin/Debug/net10.0-windows/FileHashValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'SHA-256 file validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/ShellValidation/bin/Debug/net10.0-windows/ShellValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Shell registration validation failed.' }
& (Join-Path $PSScriptRoot 'test-shell.ps1')
& (Join-Path $PSScriptRoot 'test-shell-modern-prototype.ps1')
$taskPython = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $taskPython -or $taskPython -like '*WindowsApps*') {
    $taskPython = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
}
if (-not $SkipEngineValidation) {
    & $taskPython (Join-Path $taskRoot 'tests/engine_validation.py')
    if ($LASTEXITCODE -ne 0) { throw 'Engine validation failed.' }
}
& $taskDotnet (Join-Path $taskRoot 'tests/SfxValidation/bin/Debug/net10.0-windows/SfxValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'SFX validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/FormatValidation/bin/Debug/net10.0-windows/FormatValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Additional creation format validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/VolumeValidation/bin/Debug/net10.0-windows/VolumeValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Split volume validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/PreviewValidation/bin/Debug/net10.0-windows/PreviewValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Archive preview validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/CheckpointValidation/bin/Debug/net10.0-windows/CheckpointValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Cross-process checkpoint validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/DragValidation/bin/Debug/net10.0-windows/DragValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Archive drag preparation validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/ConversionValidation/bin/Debug/net10.0-windows/ConversionValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Archive conversion validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/ArchiveEditingValidation/bin/Debug/net10.0-windows/ArchiveEditingValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Archive editing validation failed.' }
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
$taskWithoutListing = Get-Content -Raw (Join-Path $taskRoot 'artifacts/engine-validation.json') | ConvertFrom-Json
$taskWithoutListing.cases = @($taskWithoutListing.cases | Where-Object { $_.name -notmatch ' lists | listing' })
$taskWithoutListing.total = $taskWithoutListing.cases.Count
$taskWithoutListing.passed = $taskWithoutListing.total
$taskListingReport = Join-Path $taskTestAssets 'missing-listing-report.json'
$taskWithoutListing | ConvertTo-Json -Depth 8 | Set-Content $taskListingReport -Encoding utf8
$taskListingPackage = Join-Path $taskTestAssets 'missing-listing-must-not-be-signed.zip'
if (Test-Path $taskListingPackage) { throw 'A previously rejected listing artifact exists; inspect it before continuing.' }
$taskListingSigner = [Diagnostics.ProcessStartInfo]::new($taskDotnet)
$taskListingSigner.UseShellExecute = $false; $taskListingSigner.CreateNoWindow = $true
$taskListingSigner.RedirectStandardOutput = $true; $taskListingSigner.RedirectStandardError = $true
foreach ($taskArgument in @($taskPackager,'pack',(Join-Path $taskRoot 'engines/26.04'),'26.04',$taskListingReport,$taskTestPrivate,$taskListingPackage,$taskTestPublic,(Join-Path $taskRoot 'artifacts/compression-validation.json'))) { $taskListingSigner.ArgumentList.Add($taskArgument) }
$taskListingRejection = [Diagnostics.Process]::Start($taskListingSigner)
$taskListingOutput = $taskListingRejection.StandardOutput.ReadToEndAsync(); $taskListingError = $taskListingRejection.StandardError.ReadToEndAsync()
$taskListingRejection.WaitForExit(); $taskListingOutput.GetAwaiter().GetResult() | Out-Null; $taskListingError.GetAwaiter().GetResult() | Out-Null
$taskListingRejected = $taskListingRejection.ExitCode -ne 0 -and -not (Test-Path $taskListingPackage)
@{passed=($taskSigningPassed -and $taskListingRejected);compressionEvidenceHashRejection=$taskSigningPassed;missingListingEvidenceRejection=$taskListingRejected} | ConvertTo-Json | Set-Content (Join-Path $taskRoot 'artifacts/signing-validation.json') -Encoding utf8
if (-not $taskListingRejected) { throw 'A compatibility report without archive listing evidence was accepted.' }
$taskWithoutMultiple = Get-Content -Raw (Join-Path $taskRoot 'artifacts/engine-validation.json') | ConvertFrom-Json
$taskWithoutMultiple.cases = @($taskWithoutMultiple.cases | Where-Object { $_.name -notmatch ' combines mixed-password ' })
$taskWithoutMultiple.total = $taskWithoutMultiple.cases.Count
$taskWithoutMultiple.passed = $taskWithoutMultiple.total
$taskMultipleReport = Join-Path $taskTestAssets 'missing-multiple-password-report.json'
$taskWithoutMultiple | ConvertTo-Json -Depth 8 | Set-Content $taskMultipleReport -Encoding utf8
$taskMultiplePackage = Join-Path $taskTestAssets 'missing-multiple-password-must-not-be-signed.zip'
if (Test-Path $taskMultiplePackage) { throw 'A previously rejected multiple-password artifact exists; inspect it before continuing.' }
$taskMultipleSigner = [Diagnostics.ProcessStartInfo]::new($taskDotnet)
$taskMultipleSigner.UseShellExecute = $false; $taskMultipleSigner.CreateNoWindow = $true
$taskMultipleSigner.RedirectStandardOutput = $true; $taskMultipleSigner.RedirectStandardError = $true
foreach ($taskArgument in @($taskPackager,'pack',(Join-Path $taskRoot 'engines/26.04'),'26.04',$taskMultipleReport,$taskTestPrivate,$taskMultiplePackage,$taskTestPublic,(Join-Path $taskRoot 'artifacts/compression-validation.json'))) { $taskMultipleSigner.ArgumentList.Add($taskArgument) }
$taskMultipleRejection = [Diagnostics.Process]::Start($taskMultipleSigner)
$taskMultipleOutput = $taskMultipleRejection.StandardOutput.ReadToEndAsync(); $taskMultipleError = $taskMultipleRejection.StandardError.ReadToEndAsync()
$taskMultipleRejection.WaitForExit(); $taskMultipleOutput.GetAwaiter().GetResult() | Out-Null; $taskMultipleError.GetAwaiter().GetResult() | Out-Null
$taskMultipleRejected = $taskMultipleRejection.ExitCode -ne 0 -and -not (Test-Path $taskMultiplePackage)
@{passed=($taskSigningPassed -and $taskListingRejected -and $taskMultipleRejected);compressionEvidenceHashRejection=$taskSigningPassed;missingListingEvidenceRejection=$taskListingRejected;missingMultiplePasswordEvidenceRejection=$taskMultipleRejected} | ConvertTo-Json | Set-Content (Join-Path $taskRoot 'artifacts/signing-validation.json') -Encoding utf8
if (-not $taskMultipleRejected) { throw 'A compatibility report without multiple-password evidence was accepted.' }
& $taskDotnet (Join-Path $taskRoot 'tests/CoreValidation/bin/Debug/net10.0-windows/CoreValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Core validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/MultiPasswordValidation/bin/Debug/net10.0-windows/MultiPasswordValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Multiple password validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/BrowserValidation/bin/Debug/net10.0-windows/BrowserValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Archive browser validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/UpdateValidation/bin/Debug/net10.0-windows/UpdateValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Update validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/ApplicationUpdateValidation/bin/Debug/net10.0-windows/ApplicationUpdateValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Application update validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/ExtractionToolsValidation/bin/Debug/net10.0-windows/ExtractionToolsValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Extraction tools validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/SelectionValidation/bin/Debug/net10.0-windows/SelectionValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Selection validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/EncodingValidation/bin/Debug/net10.0-windows/EncodingValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'ZIP encoding validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/OriginValidation/bin/Debug/net10.0-windows/OriginValidation.dll') $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Origin metadata validation failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/ArchitectureValidation/bin/Debug/net10.0-windows/ArchitectureValidation.dll') $taskRoot x64
if ($LASTEXITCODE -ne 0) { throw 'Native architecture validation failed.' }
$taskTestApp = Join-Path $taskRoot 'artifacts/test-app'
New-Item -ItemType Directory -Force $taskTestApp | Out-Null
Copy-Item (Join-Path $taskRoot 'artifacts/app/*') $taskTestApp -Recurse -Force
Copy-Item $taskTestPublic (Join-Path $taskTestApp 'update-public-key.pem') -Force
$taskApplication = Join-Path $taskTestApp 'ArchivePassword.exe'
& $taskDotnet (Join-Path $taskRoot 'tests/CliValidation/bin/Debug/net10.0-windows/CliValidation.dll') $taskRoot $taskTestApp
if ($LASTEXITCODE -ne 0) { throw 'Console and batch entry validation failed.' }
if(-not $SkipDesktop){
& (Join-Path $PSScriptRoot 'prepare-pause-ui-fixture.ps1')
$taskDesktopFailures=[Collections.Generic.List[string]]::new()
function Invoke-DesktopAcceptance([string[]]$TaskArguments,[string]$TaskDescription) {
    Write-Output "Desktop starting: $TaskDescription"
    $taskGui=Start-Process -FilePath $taskApplication -ArgumentList $TaskArguments -WindowStyle Hidden -PassThru
    if(-not $taskGui.WaitForExit(300000)) {
        $taskGui.Kill($true);$taskGui.WaitForExit();$taskDesktopFailures.Add("$TaskDescription timed out");Write-Warning "$TaskDescription timed out";return
    }
    if($taskGui.ExitCode -ne 0){$taskDesktopFailures.Add($TaskDescription);Write-Warning "Desktop failed: $TaskDescription"}
    else{Write-Output "Desktop passed: $TaskDescription"}
}
Invoke-DesktopAcceptance -TaskArguments @('--validate', $taskRoot) -TaskDescription 'Desktop command validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-vault-transfer', $taskRoot) -TaskDescription 'Desktop vault transfer command validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-password-organization', $taskRoot) -TaskDescription 'Desktop password organization validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-file-hashes', $taskRoot) -TaskDescription 'Desktop SHA-256 validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-shell-ui', $taskRoot) -TaskDescription 'Desktop Shell settings and compression validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-application-updates', $taskRoot) -TaskDescription 'Desktop application update validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-application-updates', $taskRoot, 'en') -TaskDescription 'English desktop application update validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-extraction-tools', $taskRoot) -TaskDescription 'Desktop extraction tools validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-selection', $taskRoot) -TaskDescription 'Desktop selected extraction validation'

Invoke-DesktopAcceptance -TaskArguments @('--validate-encoding', $taskRoot) -TaskDescription 'Desktop ZIP encoding validation'

Invoke-DesktopAcceptance -TaskArguments @('--validate-origin', $taskRoot) -TaskDescription 'Desktop origin metadata validation'

Invoke-DesktopAcceptance -TaskArguments @('--validate-dpi', $taskRoot) -TaskDescription 'Desktop per-monitor DPI validation'

Invoke-DesktopAcceptance -TaskArguments @('--validate-queue', $taskRoot) -TaskDescription 'Desktop task queue validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-concurrency', $taskRoot) -TaskDescription 'Desktop controlled concurrency validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-pause', $taskRoot) -TaskDescription 'Desktop pause and resume validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-notifications', $taskRoot) -TaskDescription 'Desktop result notification validation'

Invoke-DesktopAcceptance -TaskArguments @('--validate-sfx', $taskRoot) -TaskDescription 'SFX desktop validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-formats', $taskRoot) -TaskDescription 'Additional creation format desktop validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-volumes', $taskRoot) -TaskDescription 'Split volume desktop validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-checkpoints', $taskRoot) -TaskDescription 'Extraction checkpoint desktop validation'
try { & (Join-Path $PSScriptRoot 'test-visual-refresh.ps1') -ApplicationDirectory (Split-Path $taskApplication -Parent) } catch { $taskDesktopFailures.Add($_.ToString()) }
Invoke-DesktopAcceptance -TaskArguments @('--validate-drag-out', $taskRoot) -TaskDescription 'Archive drag desktop validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-content-preview', $taskRoot) -TaskDescription 'Archive preview desktop validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-conversion', $taskRoot) -TaskDescription 'Archive conversion desktop validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-archive-editing', $taskRoot) -TaskDescription 'Archive editing desktop validation'
Invoke-DesktopAcceptance -TaskArguments @('--validate-master-vault', $taskRoot) -TaskDescription 'Master password desktop validation'

try { & (Join-Path $taskRoot "scripts/test-localization-ui.ps1") -ApplicationDirectory (Split-Path $taskApplication -Parent) } catch { $taskDesktopFailures.Add($_.ToString()) }

try { & (Join-Path $taskRoot "scripts/test-theme-ui.ps1") -ApplicationDirectory (Split-Path $taskApplication -Parent) } catch { $taskDesktopFailures.Add($_.ToString()) }
if ($taskDesktopFailures.Count) { throw "Desktop acceptance failures: $($taskDesktopFailures -join '; ')" }
}
