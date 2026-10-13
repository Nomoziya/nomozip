param()
$ErrorActionPreference = 'Stop'
# Trust and package mutations are confined to a disposable hosted Windows VM.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Identity deployment acceptance is restricted to disposable GitHub-hosted Windows runners.'
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskId = [Guid]::NewGuid().ToString('N')
$taskName = 'Nomoziya.NomozipShellTest' + $taskId.Substring(0,16)
$taskPublisher = 'CN=' + $taskName
$taskOriginalAppHash = (Get-FileHash -LiteralPath (Join-Path $taskRoot 'artifacts/app/ArchivePassword.exe')).Hash
$taskOutput = & (Join-Path $PSScriptRoot 'build-shell-identity-prototype.ps1') -OutputDirectory ('artifacts/shell-identity/' + $taskId) -PackageName $taskName -Publisher $taskPublisher -IncludeValidationProbe
$taskTools = & (Join-Path $PSScriptRoot 'bootstrap-shell-package-tools.ps1')
$taskExternal = Join-Path $taskOutput 'external'
$taskValidationRoot = Join-Path $taskOutput 'acceptance'
New-Item -ItemType Directory -Path (Join-Path $taskValidationRoot 'artifacts') -Force | Out-Null
New-Item -ItemType Directory -Path $taskExternal -Force | Out-Null
Copy-Item -Path (Join-Path $taskRoot 'artifacts/app/*') -Destination $taskExternal -Recurse -Force
Copy-Item -LiteralPath (Join-Path $taskOutput 'NomoZipExplorerPrototype.dll') -Destination $taskExternal
[xml]$taskFusion = Get-Content -LiteralPath (Join-Path $taskRoot 'src/Desktop/app.manifest') -Raw
$taskIdentity = $taskFusion.CreateElement('msix','urn:schemas-microsoft-com:msix.v1')
$taskIdentity.SetAttribute('publisher',$taskPublisher); $taskIdentity.SetAttribute('packageName',$taskName); $taskIdentity.SetAttribute('applicationId','Nomozip')
$taskFusion.DocumentElement.AppendChild($taskIdentity) | Out-Null
$taskFusionPath = Join-Path $taskOutput 'app.manifest'; $taskFusion.Save($taskFusionPath)
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot '.tools/nuget-http'
$env:NUGET_SCRATCH = Join-Path $taskRoot '.tools/nuget-scratch'
& (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') publish (Join-Path $taskRoot 'src/Desktop/ArchivePassword.Desktop.csproj') -c Release -o $taskExternal --no-self-contained --nologo -m:1 -nr:false -v:minimal ("-p:ApplicationManifest=$taskFusionPath")
if ($LASTEXITCODE -ne 0) { throw 'Isolated identity-aware WPF build failed.' }
$taskProbe = Join-Path $taskExternal 'IdentityProbe.exe'
& g++ -std=c++17 -O2 -municode -static -static-libgcc -static-libstdc++ (Join-Path $taskRoot 'tests/ShellValidation/identity-probe.cpp') -lshell32 -lole32 -luuid -o $taskProbe
if ($LASTEXITCODE -ne 0) { throw 'Identity probe build failed.' }
$taskIdentity.SetAttribute('applicationId','Probe'); $taskProbeManifest = Join-Path $taskOutput 'probe.manifest'; $taskFusion.Save($taskProbeManifest)
& $taskTools['mt.exe'] -manifest $taskProbeManifest ("-outputresource:$taskProbe;#1") | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Identity probe manifest embedding failed.' }
[xml]$taskManifest = Get-Content -LiteralPath (Join-Path $taskOutput 'identity/AppxManifest.xml') -Raw
$taskManager = [Xml.XmlNamespaceManager]::new($taskManifest.NameTable); $taskManager.AddNamespace('com','http://schemas.microsoft.com/appx/manifest/com/windows10')
$taskServer = $taskManifest.SelectSingleNode('//com:ExeServer',$taskManager)
$taskServer.Arguments = '--shell-server --validate-shell-server "' + $taskValidationRoot + '"'
$taskManifest.Save((Join-Path $taskOutput 'identity/AppxManifest.xml'))
# Repack the fixture only, now pointing automatic COM activation at synthetic data.
$taskPackage = Join-Path $taskOutput 'nomozip.test.identity.msix'
& $taskTools['makeappx.exe'] pack /d (Join-Path $taskOutput 'identity') /p $taskPackage /nv | Set-Content -LiteralPath (Join-Path $taskOutput 'test-makeappx.log')
if ($LASTEXITCODE -ne 0) { throw 'Identity fixture packaging failed.' }
$taskChecks = [ordered]@{}
$taskCertificate = $null
$taskCleanupPassed = $false
$taskFailure = $null
$taskThumbprint = $null
try {
    $taskCertificate = New-SelfSignedCertificate -Type Custom -Subject $taskPublisher -KeyUsage DigitalSignature -FriendlyName 'Disposable nomozip identity acceptance' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
    $taskThumbprint = $taskCertificate.Thumbprint
    $taskPublicCertificate = Join-Path $taskOutput 'test-public.cer'
    Export-Certificate -Cert $taskCertificate -FilePath $taskPublicCertificate | Out-Null
    Import-Certificate -FilePath $taskPublicCertificate -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    foreach ($taskBinary in @($taskProbe,(Join-Path $taskExternal 'ArchivePassword.exe'),(Join-Path $taskExternal 'NomoZipExplorerPrototype.dll'),$taskPackage)) {
        & $taskTools['signtool.exe'] sign /fd SHA256 /sha1 $taskThumbprint /s My $taskBinary | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Disposable fixture signing failed.' }
    }
    Add-AppxPackage -Path $taskPackage -ExternalLocation $taskExternal
    $taskInstalled = Get-AppxPackage -Name $taskName
    $taskChecks.SignedSparseRegistration = $null -ne $taskInstalled -and $taskInstalled.Publisher -eq $taskPublisher
    foreach ($taskCase in @('ExtractHere','ExtractFolder','Compress')) {
        $taskPaths = @()
        foreach ($taskFolderName in @('A 中文 空格 $ &','B')) {
            $taskFolder = Join-Path $taskOutput ($taskCase + '-' + $taskFolderName); New-Item -ItemType Directory -Path $taskFolder -Force | Out-Null
            $taskArchive = Join-Path $taskFolder '归档.7z'; Copy-Item -LiteralPath (Join-Path $taskRoot 'tests/fixtures/generated/encrypted-header.7z') -Destination $taskArchive
            $taskPaths += $taskArchive
        }
        if ($taskCase -eq 'Compress') { $taskPaths += (Split-Path $taskPaths[0] -Parent) }
        $taskClsid = '{c708b15d-0d3e-47ab-8fc8-c22d21ff117' + (@('ExtractHere','ExtractFolder','Compress').IndexOf($taskCase) + 1) + '}'
        $taskClientReport = Join-Path $taskOutput ($taskCase + '-probe.json')
        $taskArguments = @($taskClientReport,$taskClsid) + $taskPaths
        # Every value is a generated Windows path or fixed GUID; quote as data.
        $taskQuotedArguments = @($taskArguments | ForEach-Object { '"' + $_ + '"' })
        $taskProcess = Start-Process -FilePath $taskProbe -ArgumentList $taskQuotedArguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput ($taskCase + '-probe-error.log')) -RedirectStandardOutput (Join-Path $taskOutput ($taskCase + '-probe-output.log'))
        try { if (-not $taskProcess.WaitForExit(30000)) { throw 'Packaged command timed out.' }; if ($taskProcess.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $taskOutput ($taskCase + '-probe-error.log')); throw "Packaged $taskCase probe failed ($($taskProcess.ExitCode))." } }
        finally { if (-not $taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id } }
        $taskClient = Get-Content -LiteralPath $taskClientReport -Raw | ConvertFrom-Json
        $taskChecks[$taskCase + 'IdentityAndPackagedCom'] = $taskClient.passed -and $taskClient.osIdentity -and $taskClient.packagedComActivation -and $taskClient.selected -eq $taskPaths.Count -and $taskClient.packageFullName.StartsWith($taskName + '_')
        $taskDesktopReport = Join-Path $taskValidationRoot ('artifacts/shell-server-' + $taskCase + '-validation.json')
        $taskDeadline = [DateTime]::UtcNow.AddSeconds(30)
        while (-not (Test-Path -LiteralPath $taskDesktopReport)) { if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'Automatically activated desktop did not finish.' }; Start-Sleep -Milliseconds 100 }
        $taskDesktop = Get-Content -LiteralPath $taskDesktopReport -Raw | ConvertFrom-Json
        $taskChecks[$taskCase + 'AutomaticDesktopSelection'] = $taskDesktop.passed -and $taskDesktop.selected -eq $taskPaths.Count -and $taskDesktop.syntheticData
        if ($taskCase -eq 'Compress') { $taskChecks.CompressionWaitsForConfirmation = -not (Test-Path -LiteralPath $taskDesktop.compressionOutput) }
        else {
            foreach ($taskArchive in $taskPaths) {
                $taskDestination = if ($taskCase -eq 'ExtractHere') { Split-Path $taskArchive -Parent } else { Join-Path (Split-Path $taskArchive -Parent) ([IO.Path]::GetFileNameWithoutExtension($taskArchive)) }
                $taskResult = Join-Path $taskDestination '中文示例.txt'
                $taskChecks[$taskCase + 'RealOutput' + $taskChecks.Count] = (Test-Path -LiteralPath $taskResult) -and (Get-FileHash -LiteralPath $taskResult).Hash -eq (Get-FileHash -LiteralPath (Join-Path $taskRoot 'tests/fixtures/generated/中文示例.txt')).Hash
            }
        }
    }
    $taskBefore = (Get-FileHash -LiteralPath (Join-Path $taskExternal 'ArchivePassword.exe')).Hash
    $taskManifest.Package.Identity.Version = '0.2.0.3'; $taskManifest.Save((Join-Path $taskOutput 'identity/AppxManifest.xml'))
    $taskUpgrade = Join-Path $taskOutput 'nomozip.upgrade.identity.msix'
    & $taskTools['makeappx.exe'] pack /d (Join-Path $taskOutput 'identity') /p $taskUpgrade /nv | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade fixture packaging failed.' }
    & $taskTools['signtool.exe'] sign /fd SHA256 /sha1 $taskThumbprint /s My $taskUpgrade | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade fixture signing failed.' }
    Add-AppxPackage -Path $taskUpgrade -ExternalLocation $taskExternal -ForceApplicationShutdown
    $taskChecks.IdentityUpgrade = (Get-AppxPackage -Name $taskName).Version -eq '0.2.0.3'
    $taskChecks.UpgradePreservesExternalApp = (Get-FileHash -LiteralPath (Join-Path $taskExternal 'ArchivePassword.exe')).Hash -eq $taskBefore
    Get-AppxPackage -Name $taskName | Remove-AppxPackage
    $taskChecks.IdentityRemoval = $null -eq (Get-AppxPackage -Name $taskName)
    $taskChecks.RemovalPreservesExternalApp = (Get-FileHash -LiteralPath (Join-Path $taskExternal 'ArchivePassword.exe')).Hash -eq $taskBefore
} catch { $taskFailure = $_.Exception.Message; $taskChecks.DeploymentException = $false }
finally {
    try {
        Get-AppxPackage -Name $taskName | Remove-AppxPackage
        if ($taskThumbprint) {
            foreach ($taskStore in @('TrustedPeople','My')) { $taskCertificatePath = "Cert:\CurrentUser\$taskStore\$taskThumbprint"; if (Test-Path -LiteralPath $taskCertificatePath) { if ($taskStore -eq 'My') { Remove-Item -LiteralPath $taskCertificatePath -DeleteKey } else { Remove-Item -LiteralPath $taskCertificatePath } } }
        }
        $taskCleanupPassed = $null -eq (Get-AppxPackage -Name $taskName)
    } catch { $taskFailure = 'Disposable cleanup failed: ' + $_.Exception.Message; $taskCleanupPassed = $false }
}
$taskChecks.DisposableCleanup = $taskCleanupPassed
$taskChecks.NoProductionPayloadMutation = (Get-FileHash -LiteralPath (Join-Path $taskRoot 'artifacts/app/ArchivePassword.exe')).Hash -eq $taskOriginalAppHash
$taskReport = [ordered]@{passed=($taskChecks.Values -notcontains $false);total=$taskChecks.Count;checks=$taskChecks;failure=$taskFailure;packageName=$taskName;temporaryVmOnly=$true;actualWindows11MenuTested=$false;productionSigningConfigured=$false;includedInProduct=$false;directory=$taskOutput}
$taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/shell-identity-validation.json') -Encoding utf8
if (-not $taskReport.passed) { throw 'Sparse identity deployment validation failed; see shell-identity-validation.json.' }
$taskReport | ConvertTo-Json -Depth 6
