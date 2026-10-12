param([string]$ValidationRoot)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskParent = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/installer-validation')).TrimEnd('\') + '\'
$taskValidation = if ($ValidationRoot) { [IO.Path]::GetFullPath($ValidationRoot) } else { Join-Path $taskParent ([Guid]::NewGuid().ToString('N')) }
if (-not $taskValidation.StartsWith($taskParent,[StringComparison]::OrdinalIgnoreCase)) { throw 'Installer validation must stay inside its workspace directory.' }
New-Item -ItemType Directory -Force $taskValidation | Out-Null
$taskApplication = Join-Path $taskValidation 'app'
$taskData = Join-Path $taskValidation 'data'
$taskFirst = Join-Path $taskValidation 'first-setup.exe'
$taskUpgrade = Join-Path $taskValidation 'upgrade-setup.exe'
$taskChecks = [ordered]@{}
function Assert-WorkspacePath([string]$Path) {
    if (-not ([IO.Path]::GetFullPath($Path)).StartsWith($taskParent,[StringComparison]::OrdinalIgnoreCase)) { throw 'Validation path escaped the workspace.' }
}
function Invoke-Installer([string]$Path,[string[]]$Arguments) {
    Assert-WorkspacePath $Path
    $taskProcess = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($taskProcess.ExitCode -ne 0) { throw "Installer returned $($taskProcess.ExitCode)." }
}
if (-not (Test-Path $taskFirst)) { & (Join-Path $PSScriptRoot 'build-installer.ps1') -OutputFile $taskFirst -Version '0.1.0.1' -ValidationRoot $taskValidation }
if (-not (Test-Path $taskUpgrade)) { & (Join-Path $PSScriptRoot 'build-installer.ps1') -OutputFile $taskUpgrade -Version '0.1.0.2' -ValidationRoot $taskValidation }
try {
    Invoke-Installer $taskFirst @('/S')
    $taskChecks.firstInstall = (Test-Path (Join-Path $taskApplication 'ArchivePassword.exe')) -and (Test-Path (Join-Path $taskValidation 'shortcuts/nomozip.lnk'))
    $taskRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\nomozip-validation'
    $taskChecks.oldVersionRegistered = (Get-ItemProperty -LiteralPath $taskRegistry).DisplayVersion -eq '0.1.0.1'
    New-Item -ItemType Directory -Force $taskData | Out-Null
    $taskDocument = @{version=1;passwords=@(@{id=[Guid]::NewGuid().ToString();name='synthetic previous install';secret='测试密碼 '' $ & 空格';notes='';autoTry=$true;createdAt=[DateTimeOffset]::UtcNow.ToString('o')});associations=@{}}
    $taskFakeSecret = [Text.Encoding]::UTF8.GetBytes(($taskDocument | ConvertTo-Json -Depth 4 -Compress))
    $taskEncrypted = [Security.Cryptography.ProtectedData]::Protect($taskFakeSecret,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $taskVault = Join-Path $taskData 'passwords.apv'
    [IO.File]::WriteAllBytes($taskVault, [byte[]]([Text.Encoding]::ASCII.GetBytes('APV1') + $taskEncrypted))
    $taskSettings = Join-Path $taskData 'settings.json'
    @{version=1;outputPath=(Join-Path $taskValidation 'output');autoTry=$true;conflictPolicy=1;engineVersion='26.04';updateRepository='Nomoziya/nomozip'} | ConvertTo-Json | Set-Content -LiteralPath $taskSettings -Encoding utf8
    $taskVaultHash = (Get-FileHash $taskVault -Algorithm SHA256).Hash
    $taskSettingsHash = (Get-FileHash $taskSettings -Algorithm SHA256).Hash
    'old release instruction' | Set-Content (Join-Path $taskApplication '使用说明.txt')
    'unrelated user file' | Set-Content (Join-Path $taskApplication 'keep-user-file.txt')
    Invoke-Installer $taskUpgrade @('/S')
    $taskChecks.newVersionRegistered = (Get-ItemProperty -LiteralPath $taskRegistry).DisplayVersion -eq '0.1.0.2'
    $taskChecks.upgradeReplacedOwnedPayload = (Get-FileHash (Join-Path $taskApplication '使用说明.txt')).Hash -eq (Get-FileHash (Join-Path $taskRoot 'artifacts/self-contained-app/使用说明.txt')).Hash
    $taskChecks.upgradePreservedVault = (Get-FileHash $taskVault).Hash -eq $taskVaultHash
    $taskChecks.upgradePreservedSettings = (Get-FileHash $taskSettings).Hash -eq $taskSettingsHash
    $taskSmoke = Start-Process -FilePath (Join-Path $taskApplication 'ArchivePassword.exe') -ArgumentList @('--validate-installed',$taskRoot,'--data-dir',$taskData) -WindowStyle Hidden -Wait -PassThru
    $taskChecks.installedOfflinePasswordExtraction = $taskSmoke.ExitCode -eq 0
    $taskSmokeReport = Get-Content -Raw (Join-Path $taskRoot 'artifacts/installed-smoke-validation.json') | ConvertFrom-Json
    $taskChecks.bundledRuntimeActuallyLoaded = $taskSmokeReport.runtimeLocal
    $taskChecks.preservedVaultActuallyReused = $taskSmokeReport.existingVaultReused
    $taskVaultHash = (Get-FileHash $taskVault).Hash
    $taskSettingsHash = (Get-FileHash $taskSettings).Hash
    Invoke-Installer (Join-Path $taskApplication 'Uninstall.exe') @('/S',('_?=' + $taskApplication))
    $taskChecks.uninstallRemovedProgramAndRegistration = -not (Test-Path (Join-Path $taskApplication 'ArchivePassword.exe')) -and -not (Test-Path $taskRegistry)
    $taskChecks.defaultUninstallPreservedData = (Get-FileHash $taskVault).Hash -eq $taskVaultHash -and (Get-FileHash $taskSettings).Hash -eq $taskSettingsHash
    $taskChecks.uninstallPreservedUnrelatedFile = Test-Path (Join-Path $taskApplication 'keep-user-file.txt')
    Invoke-Installer $taskUpgrade @('/S')
    $taskChecks.reinstallPreservedVault = (Get-FileHash $taskVault).Hash -eq $taskVaultHash
    Invoke-Installer (Join-Path $taskApplication 'Uninstall.exe') @('/S','/DELETEDATA=1',('_?=' + $taskApplication))
    $taskChecks.explicitDataDeletion = -not (Test-Path $taskData)
} finally {
    $taskPassed = $taskChecks.Count -eq 14 -and @($taskChecks.Values | Where-Object { -not $_ }).Count -eq 0
    [ordered]@{passed=$taskPassed;total=$taskChecks.Count;syntheticData=$true;validationRoot=$taskValidation;checks=$taskChecks} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskRoot 'artifacts/installer-validation.json') -Encoding utf8
}
if (-not $taskPassed) { throw 'Installer acceptance failed; inspect artifacts/installer-validation.json.' }
Write-Output 'Installer install/upgrade/offline extraction/uninstall acceptance passed.'
