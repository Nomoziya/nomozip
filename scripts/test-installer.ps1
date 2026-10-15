param([string]$ValidationRoot, [switch]$FrameworkDependent = $true, [ValidateSet('x64','arm64')][string]$Architecture = 'x64', [string]$PreviousApplicationDirectory, [ValidateSet('x64','arm64')][string]$PreviousArchitecture = 'x64')
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
if (-not $PreviousApplicationDirectory) {
    $PreviousApplicationDirectory = Join-Path $taskRoot $(if ($Architecture -eq 'arm64') { 'artifacts/light-app' } else { 'artifacts/self-contained-app' })
    $PreviousArchitecture = $Architecture
}
if (-not (Test-Path $taskFirst)) { & (Join-Path $PSScriptRoot 'build-installer.ps1') -OutputFile $taskFirst -Version '0.1.0.1' -ValidationRoot $taskValidation -FrameworkDependent:($Architecture -eq 'arm64') -ApplicationDirectory $PreviousApplicationDirectory -Architecture $PreviousArchitecture }
if (-not (Test-Path $taskUpgrade)) { & (Join-Path $PSScriptRoot 'build-installer.ps1') -OutputFile $taskUpgrade -Version '0.1.0.2' -ValidationRoot $taskValidation -FrameworkDependent:$FrameworkDependent -Architecture $Architecture }
try {
    $taskRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\nomozip-validation'
    New-Item -Path $taskRegistry -Force | Out-Null
    New-ItemProperty -LiteralPath $taskRegistry -Name InstallerLanguage -Value '2052' -PropertyType String -Force | Out-Null
    Invoke-Installer $taskFirst @('/S')
    $taskChecks.firstInstall = (Test-Path (Join-Path $taskApplication 'ArchivePassword.exe')) -and (Test-Path (Join-Path $taskValidation 'shortcuts/nomozip.lnk'))
    $taskRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\nomozip-validation'
    $taskChecks.oldVersionRegistered = (Get-ItemProperty -LiteralPath $taskRegistry).DisplayVersion -eq '0.1.0.1'
    $taskChecks.chineseInstallerLanguagePersisted = (Get-ItemProperty -LiteralPath $taskRegistry).InstallerLanguage -eq '2052'
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
    $taskAssociationRoot = 'HKCU:\Software\nomozip-validation\InstallerAssociations'
    $taskAssociationCommand = Join-Path $taskAssociationRoot 'nomozip.Archive\shell\open\command'
    $taskOriginalCommand = '"' + (Join-Path $taskApplication 'ArchivePassword.exe') + '" "%1"'
    New-Item -Force $taskAssociationCommand | Out-Null
    Set-Item -LiteralPath $taskAssociationCommand -Value $taskOriginalCommand
    $taskShellRoots = @('SystemFileAssociations\.7z\shell\nomozip','SystemFileAssociations\.zip\shell\nomozip','SystemFileAssociations\.rar\shell\nomozip','*\shell\nomozip','Directory\shell\nomozip',
        'CLSID\{c708b15d-0d3e-47ab-8fc8-c22d21ff1171}','CLSID\{c708b15d-0d3e-47ab-8fc8-c22d21ff1172}','CLSID\{c708b15d-0d3e-47ab-8fc8-c22d21ff1173}')
    foreach ($taskShellPath in $taskShellRoots) {
        $taskShellKey = Join-Path $taskAssociationRoot $taskShellPath
        New-Item -Force $taskShellKey | Out-Null
        New-ItemProperty -LiteralPath $taskShellKey -Name NomozipOwner -Value (Join-Path $taskApplication 'ArchivePassword.exe') -Force | Out-Null
        New-ItemProperty -LiteralPath $taskShellKey -Name SyntheticPayload -Value 'keep-this-registration' -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $taskRegistry -Name InstallerLanguage -Value '1033'
    # Test the same silent /UPDATE handoff used by the app, with an owned process
    # holding both GUI and CLI files until explicitly released. No real profile is used.
    $taskReady = Join-Path $taskValidation 'update-lock-ready'
    $taskRelease = Join-Path $taskValidation 'update-lock-release'
    $taskLockScript = @'
param($app,$ready,$release)
$a=[IO.File]::Open((Join-Path $app 'ArchivePassword.exe'),'Open','Read','Read')
$b=$null
try {
    if(Test-Path -LiteralPath (Join-Path $app 'nomozip-cli.exe')) {$b=[IO.File]::Open((Join-Path $app 'nomozip-cli.exe'),'Open','Read','Read')}
    [IO.File]::WriteAllText($ready,'ready')
    $until=[DateTime]::UtcNow.AddSeconds(90)
    while(-not(Test-Path -LiteralPath $release) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 50}
} finally {if($b){$b.Dispose()};$a.Dispose()}
'@
    $taskLockPath=Join-Path $taskValidation 'lock-update.ps1'
    [IO.File]::WriteAllText($taskLockPath,$taskLockScript)
    $taskLock=Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile','-File',('"'+$taskLockPath+'"'),('"'+$taskApplication+'"'),('"'+$taskReady+'"'),('"'+$taskRelease+'"')) -WindowStyle Hidden -PassThru
    $taskUpdateProcess=$null
    try {
        $taskDeadline=[DateTime]::UtcNow.AddSeconds(15)
        while(-not(Test-Path -LiteralPath $taskReady) -and [DateTime]::UtcNow -lt $taskDeadline){Start-Sleep -Milliseconds 50}
        if(-not(Test-Path -LiteralPath $taskReady)){throw 'Update lock process did not start.'}
        $taskBlockedProcess=Start-Process -FilePath $taskUpgrade -ArgumentList @('/S','/UPDATE',('/D='+$taskApplication)) -WindowStyle Hidden -PassThru
        if(-not $taskBlockedProcess.WaitForExit(35000)){$taskBlockedProcess.Kill();throw 'Blocked update did not time out.'}
        $taskChecks.automaticUpdateTimeoutLeavesRegisteredInstallation=$taskBlockedProcess.ExitCode -eq 2 -and (Get-ItemProperty -LiteralPath $taskRegistry).DisplayVersion -eq '0.1.0.1' -and (Get-FileHash $taskVault).Hash -eq $taskVaultHash
        $taskSetupLock=[IO.File]::Open($taskUpgrade,'Open','Read','Read')
        try {$taskUpdateProcess=Start-Process -FilePath $taskUpgrade -ArgumentList @('/S','/UPDATE',('/D='+$taskApplication)) -WindowStyle Hidden -PassThru}
        finally {$taskSetupLock.Dispose()}
        Start-Sleep -Milliseconds 500
        $taskChecks.automaticUpdateWaitsForApplicationExit=-not $taskUpdateProcess.HasExited -and (Get-ItemProperty -LiteralPath $taskRegistry).DisplayVersion -eq '0.1.0.1'
        [IO.File]::WriteAllText($taskRelease,'release')
        if(-not $taskUpdateProcess.WaitForExit(60000) -or $taskUpdateProcess.ExitCode -ne 0){throw 'Automatic installer handoff failed.'}
        $taskChecks.automaticUpdateCompletesAfterExit=(Get-Content -LiteralPath (Join-Path $taskValidation 'update-completed.txt') -Raw) -eq $taskApplication
        $taskChecks.automaticUpdateRestartsIsolatedApplication=Test-Path -LiteralPath (Join-Path $taskValidation 'data-update-restart/passwords.apv')
    } finally {
        [IO.File]::WriteAllText($taskRelease,'release')
        if(-not $taskLock.WaitForExit(5000)){$taskLock.Kill()}
        if($taskUpdateProcess -and -not $taskUpdateProcess.HasExited){$taskUpdateProcess.Kill()}
    }
    $taskWrongTarget=Join-Path $taskValidation 'unregistered-copy'
    $taskRejected=Start-Process -FilePath $taskUpgrade -ArgumentList @('/S','/UPDATE',('/D='+$taskWrongTarget)) -WindowStyle Hidden -Wait -PassThru
    $taskChecks.automaticUpdateRefusesUnregisteredTarget=$taskRejected.ExitCode -eq 2 -and -not(Test-Path -LiteralPath $taskWrongTarget)
    $taskChecks.englishInstallerLanguageSurvivesUpgradeCleanup = (Get-ItemProperty -LiteralPath $taskRegistry).InstallerLanguage -eq '1033'
    $taskChecks.upgradePreservedShellMenu = @($taskShellRoots | Where-Object {
        $taskValue = Get-ItemProperty -LiteralPath (Join-Path $taskAssociationRoot $_) -ErrorAction SilentlyContinue
        $taskValue.NomozipOwner -ne (Join-Path $taskApplication 'ArchivePassword.exe') -or $taskValue.SyntheticPayload -ne 'keep-this-registration'
    }).Count -eq 0
    $taskChecks.newVersionRegistered = (Get-ItemProperty -LiteralPath $taskRegistry).DisplayVersion -eq '0.1.0.2'
    $taskPayload = if ($FrameworkDependent) { 'artifacts/light-app' } else { 'artifacts/self-contained-app' }
    $taskChecks.upgradeReplacedOwnedPayload = (Get-FileHash (Join-Path $taskApplication '使用说明.txt')).Hash -eq (Get-FileHash (Join-Path $taskRoot "$taskPayload/使用说明.txt")).Hash
    $taskChecks.upgradePreservedFileEntry = (Get-Item -LiteralPath $taskAssociationCommand).GetValue('') -eq $taskOriginalCommand
    $taskInstalledBytes = (Get-ChildItem -LiteralPath $taskApplication -Recurse -File | Measure-Object Length -Sum).Sum
    if ($FrameworkDependent) { $taskChecks.upgradeRemovedBundledRuntime = -not (Test-Path (Join-Path $taskApplication 'coreclr.dll')) -and $taskInstalledBytes -lt 12MB }
    $taskChecks.upgradePreservedVault = (Get-FileHash $taskVault).Hash -eq $taskVaultHash
    $taskChecks.upgradePreservedSettings = (Get-FileHash $taskSettings).Hash -eq $taskSettingsHash
    $taskSmokeArguments = @('--validate-installed',$taskRoot,'--data-dir',$taskData)
    if ($FrameworkDependent) { $taskSmokeArguments += '--shared-runtime' }
    $taskSmoke = Start-Process -FilePath (Join-Path $taskApplication 'ArchivePassword.exe') -ArgumentList $taskSmokeArguments -WindowStyle Hidden -Wait -PassThru
    $taskChecks.installedOfflinePasswordExtraction = $taskSmoke.ExitCode -eq 0
    $taskCliInfo = & (Join-Path $taskApplication 'nomozip-cli.exe') --version | ConvertFrom-Json
    $taskChecks.installedConsoleRunsWithNativeArchitecture = $LASTEXITCODE -eq 0 -and $taskCliInfo.architecture -eq $Architecture
    $taskCliResult = & (Join-Path $taskApplication 'nomozip-cli.exe') test (Join-Path $taskRoot 'tests/fixtures/generated/plain.7z') --data-dir $taskData | ConvertFrom-Json
    $taskChecks.installedConsoleTestsActualArchive = $LASTEXITCODE -eq 0 -and $taskCliResult.exitCode -eq 0

    $taskSmokeReportName = if ($FrameworkDependent) { 'light-installed-smoke-validation.json' } else { 'installed-smoke-validation.json' }
    $taskSmokeReport = Get-Content -Raw (Join-Path $taskRoot "artifacts/$taskSmokeReportName") | ConvertFrom-Json
    $taskChecks.expectedRuntimeActuallyLoaded = $taskSmokeReport.runtimeLocal -eq (-not $FrameworkDependent)
    $taskChecks.preservedVaultActuallyReused = $taskSmokeReport.existingVaultReused
    $taskVaultHash = (Get-FileHash $taskVault).Hash
    $taskSettingsHash = (Get-FileHash $taskSettings).Hash
    New-Item -Force $taskAssociationCommand | Out-Null
    Set-Item -LiteralPath $taskAssociationCommand -Value ('"' + (Join-Path $taskApplication 'ArchivePassword.exe') + '" "%1"')
    Invoke-Installer (Join-Path $taskApplication 'Uninstall.exe') @('/S',('_?=' + $taskApplication))
    $taskChecks.uninstallRemovedProgramAndRegistration = -not (Test-Path (Join-Path $taskApplication 'ArchivePassword.exe')) -and -not (Test-Path $taskRegistry)
    $taskChecks.uninstallRemovedConsole = -not (Test-Path (Join-Path $taskApplication 'nomozip-cli.exe'))
    $taskChecks.defaultUninstallPreservedData = (Get-FileHash $taskVault).Hash -eq $taskVaultHash -and (Get-FileHash $taskSettings).Hash -eq $taskSettingsHash
    $taskChecks.uninstallPreservedUnrelatedFile = Test-Path (Join-Path $taskApplication 'keep-user-file.txt')
    $taskChecks.uninstallRemovedOwnedFileEntry = -not (Test-Path (Join-Path $taskAssociationRoot 'nomozip.Archive'))
    $taskChecks.uninstallRemovedOwnedShellMenu = @($taskShellRoots | Where-Object { Test-Path -LiteralPath (Join-Path $taskAssociationRoot $_) }).Count -eq 0
    Invoke-Installer $taskUpgrade @('/S')
    $taskChecks.reinstallPreservedVault = (Get-FileHash $taskVault).Hash -eq $taskVaultHash
    New-Item -Force $taskAssociationCommand | Out-Null
    Set-Item -LiteralPath $taskAssociationCommand -Value '"C:\synthetic-other-copy\ArchivePassword.exe" "%1"'
    foreach ($taskShellPath in $taskShellRoots) {
        $taskShellKey = Join-Path $taskAssociationRoot $taskShellPath; New-Item -Force $taskShellKey | Out-Null
        New-ItemProperty -LiteralPath $taskShellKey -Name NomozipOwner -Value 'C:\synthetic-other-copy\ArchivePassword.exe' -Force | Out-Null
    }
    Invoke-Installer (Join-Path $taskApplication 'Uninstall.exe') @('/S','/DELETEDATA=1',('_?=' + $taskApplication))
    $taskChecks.explicitDataDeletion = -not (Test-Path $taskData)
    $taskChecks.uninstallPreservedOtherCopyFileEntry = Test-Path $taskAssociationCommand
    $taskChecks.uninstallPreservedOtherCopyShellMenu = @($taskShellRoots | Where-Object { -not (Test-Path -LiteralPath (Join-Path $taskAssociationRoot $_)) }).Count -eq 0
    if ((Get-Item -LiteralPath $taskAssociationRoot).Name -ne 'HKEY_CURRENT_USER\Software\nomozip-validation\InstallerAssociations') { throw 'Unexpected registry cleanup target.' }
    Remove-Item -LiteralPath $taskAssociationRoot -Recurse -Force
} finally {
    $taskExpectedChecks = if ($FrameworkDependent) { 31 } else { 30 }
    $taskPassed = $taskChecks.Count -eq $taskExpectedChecks -and @($taskChecks.Values | Where-Object { -not $_ }).Count -eq 0
    $taskReportName = if ($FrameworkDependent) { 'light-installer-validation.json' } else { 'installer-validation.json' }
    [ordered]@{passed=$taskPassed;total=$taskChecks.Count;syntheticData=$true;sharedRuntime=$FrameworkDependent.IsPresent;previousArchitecture=$PreviousArchitecture;architecture=$Architecture;installedBytes=$taskInstalledBytes;validationRoot=$taskValidation;checks=$taskChecks} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskRoot "artifacts/$taskReportName") -Encoding utf8
}
if (-not $taskPassed) { throw 'Installer acceptance failed; inspect artifacts/installer-validation.json.' }
Write-Output 'Installer install/upgrade/offline extraction/uninstall acceptance passed.'
