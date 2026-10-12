param([ValidatePattern('^\d{2}\.\d{2}$')][string]$Version = '26.04', [string]$PrivateKey, [switch]$Development)
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
if (-not $PrivateKey) { throw 'Supply a release private key, or use -Development for local preview packages.' }
$taskPublicKey = Join-Path $taskRoot 'config/update-public-key.pem'
if (-not (Test-Path $taskPublicKey)) { throw 'A product public key must be pinned in config/update-public-key.pem before signing.' }
$taskOutput = Join-Path $taskRoot "artifacts/updates/ArchivePassword-engine-$Version-win-x64.zip"
if (Test-Path $taskOutput) { throw 'An update package already exists. Keep it immutable; choose a different artifact directory or remove it explicitly.' }
& $taskDotnet $taskPackager pack (Join-Path $taskRoot "engines/$Version") $Version (Join-Path $taskRoot 'artifacts/engine-validation.json') $PrivateKey $taskOutput $taskPublicKey
if ($LASTEXITCODE -ne 0) { throw 'Signed update packaging failed.' }
