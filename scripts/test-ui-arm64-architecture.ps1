# Reuse architecture acceptance without changing canonical engine fixtures or user profiles.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ARCH -ne 'ARM64' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Native architecture acceptance requires a disposable GitHub ARM64 Windows runner.'
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskWorkspace = Join-Path $taskRoot 'artifacts/arm64-ui-native-workspace'
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
if (Test-Path -LiteralPath $taskWorkspace) { throw 'Use a fresh isolated architecture workspace.' }
New-Item -ItemType Directory -Path "$taskWorkspace/artifacts/worker", "$taskWorkspace/tests/fixtures/generated" -Force | Out-Null
Copy-Item -LiteralPath "$taskRoot/artifacts/worker/ArchiveEngineWorker.exe" -Destination "$taskWorkspace/artifacts/worker/ArchiveEngineWorker.exe"
Copy-Item -LiteralPath "$taskRoot/resources/update-probe/encrypted-header.7z" -Destination "$taskWorkspace/tests/fixtures/generated/encrypted-header.7z"
foreach ($taskVersion in @('26.04','26.03')) {
    New-Item -ItemType Directory -Path "$taskWorkspace/engines/$taskVersion" -Force | Out-Null
    Copy-Item "$taskRoot/engines/arm64/$taskVersion/*" "$taskWorkspace/engines/$taskVersion"
}
& $taskDotnet build "$taskRoot/tests/ArchitectureValidation/ArchitectureValidation.csproj" --nologo -m:1 -nr:false -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Architecture acceptance build failed.' }
$taskValidation = Join-Path $taskRoot 'tests/ArchitectureValidation/bin/Debug/net10.0-windows/ArchitectureValidation.dll'
& $taskDotnet $taskValidation $taskWorkspace arm64
if ($LASTEXITCODE -ne 0) { throw 'Native ARM64 engine and architecture acceptance failed.' }

# An actual x64 .NET process writes the old synthetic DPAPI/master vault; native ARM64 reads
# and edits it; the x64 process then reads it again. No production password store is accessed.
$taskMetadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$taskRuntime = ($taskMetadata.releases | Where-Object { $_.runtime.version -eq '10.0.12' } | Select-Object -First 1).runtime.files |
    Where-Object { $_.rid -eq 'win-x64' -and $_.name -eq 'dotnet-runtime-win-x64.zip' } | Select-Object -First 1
if (-not $taskRuntime) { throw 'Pinned x64 runtime was not found in official metadata.' }
$taskZip = Join-Path $taskWorkspace 'x64-runtime.zip'
Invoke-WebRequest $taskRuntime.url -OutFile $taskZip
if ((Get-FileHash $taskZip -Algorithm SHA512).Hash -ne $taskRuntime.hash) { throw 'Synthetic x64 runtime checksum mismatch.' }
$taskRuntimeRoot = Join-Path $taskWorkspace 'x64-runtime'
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskRuntimeRoot
& "$taskRuntimeRoot/dotnet.exe" $taskValidation $taskWorkspace export-vault-x64
if ($LASTEXITCODE -ne 0) { throw 'Actual x64 vault creation failed.' }
& $taskDotnet $taskValidation $taskWorkspace import-vault-arm64
if ($LASTEXITCODE -ne 0) { throw 'Native ARM64 vault reuse failed.' }
& "$taskRuntimeRoot/dotnet.exe" $taskValidation $taskWorkspace verify-vault-x64
if ($LASTEXITCODE -ne 0) { throw 'x64 reread of native ARM64 edits failed.' }
