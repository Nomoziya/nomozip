param([ValidatePattern('^\d{2}\.\d{2}$')][string]$Version = '26.04', [string]$PrivateKey,
    [string]$ProtectedPrivateKey, [string]$OutputDirectory, [switch]$Development, [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot '.tools/nuget-http'
$env:NUGET_SCRATCH = Join-Path $taskRoot '.tools/nuget-scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
& $taskDotnet build (Join-Path $taskRoot 'tools/UpdatePackager/UpdatePackager.csproj') --nologo -m:1 -nr:false -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Update packager build failed.' }
$taskPackager = Join-Path $taskRoot 'tools/UpdatePackager/bin/Debug/net10.0-windows/UpdatePackager.dll'
if ($Development) {
    if ($PrivateKey) { throw 'Do not combine -Development and -PrivateKey.' }
    $PrivateKey = Join-Path $taskRoot '.tools/update-signing/development-private.pem'
    $taskPublic = Join-Path $taskRoot 'config/update-public-key.pem'
    if (-not (Test-Path $PrivateKey)) {
        & $taskDotnet $taskPackager keygen $PrivateKey $taskPublic
        if ($LASTEXITCODE -ne 0) { throw 'Signing key creation failed.' }
    }
}
if ($ProtectedPrivateKey -and ($PrivateKey -or $Development)) { throw 'Supply only one private key option.' }
if (-not $PrivateKey -and -not $ProtectedPrivateKey) { throw 'Supply a release private key, a protected private key, or use -Development for local preview packages.' }
$taskPublicKey = Join-Path $taskRoot 'config/update-public-key.pem'
if (-not (Test-Path $taskPublicKey)) { throw 'A product public key must be pinned in config/update-public-key.pem before signing.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'artifacts/updates' }
$taskOutput = Join-Path $OutputDirectory "ArchivePassword-engine-$Version-win-$Architecture.zip"
if (Test-Path $taskOutput) { throw 'An update package already exists. Keep it immutable; choose a different artifact directory or remove it explicitly.' }
$taskKeyBytes = $null
$taskTemporaryKey = $null
try {
    if ($ProtectedPrivateKey) {
        $taskKeyBytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ProtectedPrivateKey)), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        $taskKeyDirectory = Join-Path $taskRoot '.tools/signing-temporary'
        New-Item -ItemType Directory -Force $taskKeyDirectory | Out-Null
        $taskTemporaryKey = Join-Path $taskKeyDirectory ([Guid]::NewGuid().ToString('N') + '.pem')
        [IO.File]::WriteAllBytes($taskTemporaryKey, $taskKeyBytes)
        $PrivateKey = $taskTemporaryKey
    }
    & $taskDotnet $taskPackager pack (Join-Path $taskRoot $(if ($Architecture -eq 'arm64') { "engines/arm64/$Version" } else { "engines/$Version" })) $Version (Join-Path $taskRoot 'artifacts/engine-validation.json') $PrivateKey $taskOutput $taskPublicKey (Join-Path $taskRoot 'artifacts/compression-validation.json')
    if ($LASTEXITCODE -ne 0) { throw 'Signed update packaging failed.' }
} finally {
    if ($taskTemporaryKey -and (Test-Path -LiteralPath $taskTemporaryKey)) { Remove-Item -LiteralPath $taskTemporaryKey }
    if ($taskKeyBytes) { [Array]::Clear($taskKeyBytes, 0, $taskKeyBytes.Length) }
}
