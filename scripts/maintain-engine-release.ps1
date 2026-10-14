param([ValidateSet('Prepare','Sign','Draft','Publish')][string]$Mode = 'Prepare',
    [Parameter(Mandatory)][string]$CandidateDirectory, [ValidatePattern('^\d{2}\.\d{2}$')][string]$Version,
    [string]$EngineDirectory, [string]$Python = 'python', [string]$Dotnet, [string]$ToolsRoot,
    [string]$PrivateKey, [string]$ProtectedPrivateKey, [string]$GitHubCli = 'gh',
    [ValidatePattern('^[0-9a-fA-F]{40}$')][string]$TargetCommit, [switch]$ApprovePublish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $ToolsRoot) { $ToolsRoot = $taskRoot }
$ToolsRoot = [IO.Path]::GetFullPath($ToolsRoot)
if (-not $Dotnet) { $Dotnet = Join-Path $ToolsRoot '.tools/dotnet/dotnet.exe' }
$taskCandidate = [IO.Path]::GetFullPath($CandidateDirectory)
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')).TrimEnd('\') + '\'
if (-not $taskCandidate.StartsWith($taskArtifacts,[StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate workspace must remain under this source project artifacts directory.' }
if ($Mode -eq 'Publish' -and -not $ApprovePublish) { throw 'Review the concrete draft and supply -ApprovePublish only after maintainer approval.' }
$env:DOTNET_CLI_HOME = Join-Path $ToolsRoot '.tools/dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $ToolsRoot '.tools/nuget-http'
$env:NUGET_SCRATCH = Join-Path $ToolsRoot '.tools/nuget-scratch'
function Invoke-EngineTool([string[]]$Arguments) {
    & $Dotnet (Join-Path $ToolsRoot 'tools/UpdatePackager/bin/Debug/net10.0-windows/UpdatePackager.dll') @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Engine package tool rejected the candidate.' }
}
function Invoke-Channel([string[]]$Arguments) {
    $taskValue = & $GitHubCli @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'GitHub operation failed; existing assets were retained.' }
    return $taskValue
}
if ($Mode -eq 'Prepare') {
    if (Test-Path -LiteralPath $taskCandidate) { throw 'Keep prior candidates immutable; choose a new directory.' }
    foreach ($taskProject in @('EngineSmoke','CompressionValidation','SelectionValidation','EncodingValidation','PauseValidation','UpdatePackager')) {
        $taskFolder = if ($taskProject -eq 'UpdatePackager') { 'tools' } else { 'tests' }
        & $Dotnet build (Join-Path $ToolsRoot "$taskFolder/$taskProject/$taskProject.csproj") --nologo -m:1 -nr:false -v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'Maintenance tool build failed before candidate preparation.' }
    }
    $taskArguments = @((Join-Path $PSScriptRoot 'engine_release.py'),'prepare','--candidate',$taskCandidate,'--source-root',$taskRoot,'--tools-root',$ToolsRoot,'--dotnet',$Dotnet)
    if ($Version) { $taskArguments += @('--version',$Version) }
    if ($EngineDirectory) { $taskArguments += @('--engine-directory',[IO.Path]::GetFullPath($EngineDirectory)) }
    & $Python @taskArguments
    if ($LASTEXITCODE -ne 0) { throw 'Candidate preparation failed; review the isolated logs. No app was rebuilt.' }
    Write-Output "Prepared review: $(Join-Path $taskCandidate 'candidate-review.json')"
    return
}
# Repeat the bound review immediately before signing or any remote mutation.
& $Python (Join-Path $PSScriptRoot 'engine_release.py') review --candidate $taskCandidate --source-root $taskRoot --tools-root $ToolsRoot
if ($LASTEXITCODE -ne 0) { throw 'Prepared candidate review failed.' }
$taskReview = Get-Content -LiteralPath (Join-Path $taskCandidate 'candidate-review.json') -Raw | ConvertFrom-Json
$taskVersion = $taskReview.version
if ($Version -and $Version -ne $taskVersion) { throw 'Version differs from the reviewed candidate.' }
$taskPublic = Join-Path $taskRoot 'config/update-public-key.pem'
$taskPackageName = "ArchivePassword-engine-$taskVersion-win-x64.zip"
$taskPackage = Join-Path $taskCandidate "assets/$taskPackageName"
if ($Mode -eq 'Sign') {
    if (($PrivateKey -and $ProtectedPrivateKey) -or (-not $PrivateKey -and -not $ProtectedPrivateKey)) { throw 'Supply exactly one explicitly selected engine RSA key.' }
    if (Test-Path -LiteralPath (Join-Path $taskCandidate 'assets')) { throw 'Keep signed candidate assets immutable; choose a new candidate.' }
    $taskTemporaryKey = $null; $taskKeyBytes = $null
    try {
        if ($ProtectedPrivateKey) {
            $taskKeyBytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($ProtectedPrivateKey)), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
            $taskPrivateFolder = Join-Path $taskRoot 'artifacts/signing-temporary'; New-Item -ItemType Directory -Force $taskPrivateFolder | Out-Null
            $taskTemporaryKey = Join-Path $taskPrivateFolder ([Guid]::NewGuid().ToString('N') + '.pem')
            [IO.File]::WriteAllBytes($taskTemporaryKey,$taskKeyBytes); $PrivateKey = $taskTemporaryKey
        }
        Invoke-EngineTool @('pack-maintenance',(Join-Path $taskCandidate "workspace/engines/$taskVersion"),$taskVersion,
            (Join-Path $taskCandidate 'workspace/artifacts/engine-validation.json'),[IO.Path]::GetFullPath($PrivateKey),$taskPackage,$taskPublic,
            (Join-Path $taskCandidate 'workspace/artifacts/compression-validation.json'),(Join-Path $taskCandidate 'candidate-review.json'))
        Invoke-EngineTool @('inspect',$taskPackage,$taskPublic)
        $taskHash = (Get-FileHash -LiteralPath $taskPackage -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $taskCandidate 'assets/SHA256SUMS.txt'),"$taskHash  $taskPackageName`n",[Text.UTF8Encoding]::new($false))
        @{schema=1;version=$taskVersion;packageSha256=$taskHash;publicKeySha256=(Get-FileHash -LiteralPath $taskPublic).Hash.ToLowerInvariant();repository=$taskReview.repository} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskCandidate 'signed-review.json') -Encoding utf8
        $taskNotes = "Official 7-Zip $taskVersion engine for nomozip (Windows x64).`n`nValidated extraction/listing, mixed passwords, compression, selection, filename encoding and pause matrices. Import using nomozip engine update; existing engine rollback remains available.`n"
        [IO.File]::WriteAllText((Join-Path $taskCandidate 'release-notes.md'),$taskNotes,[Text.UTF8Encoding]::new($false))
    } finally {
        if ($taskTemporaryKey -and (Test-Path -LiteralPath $taskTemporaryKey)) { Remove-Item -LiteralPath $taskTemporaryKey }
        if ($taskKeyBytes) { [Array]::Clear($taskKeyBytes,0,$taskKeyBytes.Length) }
    }
    Write-Output "Signed local review: $(Join-Path $taskCandidate 'signed-review.json')"; return
}
$taskSigned = Get-Content -LiteralPath (Join-Path $taskCandidate 'signed-review.json') -Raw | ConvertFrom-Json
$taskHash = (Get-FileHash -LiteralPath $taskPackage).Hash.ToLowerInvariant()
if ($taskSigned.schema -ne 1 -or $taskSigned.version -ne $taskVersion -or $taskSigned.repository -ne $taskReview.repository -or $taskSigned.packageSha256 -ne $taskHash -or $taskSigned.publicKeySha256 -ne (Get-FileHash -LiteralPath $taskPublic).Hash.ToLowerInvariant()) { throw 'Signed candidate changed after review.' }
Invoke-EngineTool @('inspect',$taskPackage,$taskPublic)
$taskChecksum = Join-Path $taskCandidate 'assets/SHA256SUMS.txt'
if ([IO.File]::ReadAllText($taskChecksum).Trim() -ne "$taskHash  $taskPackageName") { throw 'Signed checksum differs from package.' }
$taskRepo = $taskReview.repository; $taskTag = "engine-$taskVersion"
if ($Mode -eq 'Draft') {
    if (-not $TargetCommit) { throw 'Supply the reviewed full source commit for the draft tag.' }
    $taskPages = (Invoke-Channel @('api',"repos/$taskRepo/releases",'--paginate','--slurp') | Out-String) | ConvertFrom-Json
    $taskExisting = @($taskPages | ForEach-Object { $_ } | Where-Object tag_name -eq $taskTag)
    if ($taskExisting.Count -gt 0) { throw 'Release tag already exists; inspect it instead of replacing or uploading assets.' }
    Invoke-Channel @('release','create',$taskTag,$taskPackage,$taskChecksum,'--repo',$taskRepo,'--draft','--latest=false','--target',$TargetCommit,'--title',"7-Zip $taskVersion engine",'--notes-file',(Join-Path $taskCandidate 'release-notes.md')) | Out-Null
}
function Read-ReviewedRemote([bool]$Draft) {
    $taskRemoteValue = (Invoke-Channel @('release','view',$taskTag,'--repo',$taskRepo,'--json','tagName,isDraft,isPrerelease,assets,url') | Out-String) | ConvertFrom-Json
    if ($taskRemoteValue.tagName -ne $taskTag -or $taskRemoteValue.isPrerelease -or $taskRemoteValue.isDraft -ne $Draft -or @($taskRemoteValue.assets).Count -ne 2 -or @($taskRemoteValue.assets | Where-Object {$_.name -notin @($taskPackageName,'SHA256SUMS.txt')}).Count -gt 0) { throw 'Only the exact reviewed two-asset release is accepted.' }
    $taskDownload = Join-Path $taskCandidate ('remote-review/' + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force $taskDownload | Out-Null
    Invoke-Channel @('release','download',$taskTag,'--repo',$taskRepo,'--pattern',$taskPackageName,'--pattern','SHA256SUMS.txt','--dir',$taskDownload) | Out-Null
    if ((Get-FileHash -LiteralPath (Join-Path $taskDownload $taskPackageName)).Hash.ToLowerInvariant() -ne $taskHash -or [IO.File]::ReadAllText((Join-Path $taskDownload 'SHA256SUMS.txt')) -ne [IO.File]::ReadAllText($taskChecksum)) { throw 'Remote assets differ from the signed local review.' }
    return $taskRemoteValue
}
$taskRemote = Read-ReviewedRemote $true
if ($Mode -eq 'Publish') {
    Invoke-Channel @('release','edit',$taskTag,'--repo',$taskRepo,'--draft=false','--prerelease=false','--latest') | Out-Null
    $taskLatest = (Invoke-Channel @('api',"repos/$taskRepo/releases/latest") | Out-String) | ConvertFrom-Json
    if ($taskLatest.tag_name -ne $taskTag -or $taskLatest.draft -or $taskLatest.prerelease) { throw 'Published release does not own the stable engine channel; inspect it manually.' }
    $taskRemote = Read-ReviewedRemote $false
}
@{schema=1;status=$(if($Mode -eq 'Publish'){'published'}else{'draft-verified'});tag=$taskTag;repository=$taskRepo;packageSha256=$taskHash;url=$taskRemote.url} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskCandidate ($Mode.ToLowerInvariant()+'-review.json')) -Encoding utf8
Write-Output "$Mode completed for $taskRepo / $taskTag"
