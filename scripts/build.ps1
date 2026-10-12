param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$SkipWorker,
    [switch]$SelfContained, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot '.tools/nuget-http'
$env:NUGET_SCRATCH = Join-Path $taskRoot '.tools/nuget-scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if (-not $SkipWorker) { & (Join-Path $PSScriptRoot 'build-worker.ps1') }
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot 'artifacts/app' }
$taskPublishArguments = @('publish', (Join-Path $taskRoot 'src/Desktop/ArchivePassword.Desktop.csproj'), '-c', $Configuration, '-o', $taskOutput, '--nologo', '-m:1', '-nr:false', '-v:minimal')
if ($SelfContained) { $taskPublishArguments += @('-r','win-x64','--self-contained','true') }
else { $taskPublishArguments += '--no-self-contained' }
& (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') @taskPublishArguments
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Copy-Item (Join-Path $taskRoot 'artifacts/worker/ArchiveEngineWorker.exe') $taskOutput -Force
Copy-Item (Join-Path $taskRoot 'engines') $taskOutput -Recurse -Force
Copy-Item (Join-Path $taskRoot 'config/update-channel.json') $taskOutput -Force
if (Test-Path (Join-Path $taskRoot 'config/update-public-key.pem')) { Copy-Item (Join-Path $taskRoot 'config/update-public-key.pem') $taskOutput -Force }
$taskProbe = Join-Path $taskOutput 'update-probe'
New-Item -ItemType Directory -Force $taskProbe | Out-Null
Copy-Item (Join-Path $taskRoot 'resources/update-probe/encrypted-header.7z') $taskProbe -Force
$taskLicenses = Join-Path $taskOutput 'licenses'
New-Item -ItemType Directory -Force $taskLicenses | Out-Null
Copy-Item (Join-Path $taskRoot '.tools/json-LICENSE.MIT') (Join-Path $taskLicenses 'nlohmann-json-MIT.txt') -Force
Copy-Item (Join-Path $taskRoot '.tools/7zip-src/DOC/License.txt') (Join-Path $taskLicenses '7zip-License.txt') -Force
Copy-Item (Join-Path $taskRoot '.tools/7zip-src/DOC/unRarLicense.txt') (Join-Path $taskLicenses '7zip-unRar-License.txt') -Force
Copy-Item (Join-Path $taskRoot 'THIRD_PARTY.md') $taskLicenses -Force
Copy-Item (Join-Path $taskRoot 'docs/QUICK_START.txt') (Join-Path $taskOutput '使用说明.txt') -Force
if (-not $SelfContained) {
    $taskInstructions = Join-Path $taskOutput '使用说明.txt'
    $taskText = [IO.File]::ReadAllText($taskInstructions)
    $taskText = $taskText.Replace('完整独立版携带 .NET 10 Windows Desktop Runtime，无需另行安装；轻量版使用系统共享运行库。','此为轻量版，需 .NET 10 Windows Desktop Runtime x64（10.0.12 或更高的 10.0 补丁版）；不需要 SDK。运行库下载：https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0')
    [IO.File]::WriteAllText($taskInstructions, $taskText, [Text.UTF8Encoding]::new($false))
}
if (Test-Path (Join-Path $taskRoot '.tools/nsis/ucrt64/share/licenses/nsis/COPYING')) {
    Copy-Item (Join-Path $taskRoot '.tools/nsis/ucrt64/share/licenses/nsis/COPYING') (Join-Path $taskLicenses 'NSIS-COPYING.txt') -Force
}
Copy-Item (Join-Path $taskRoot '.tools/dotnet/LICENSE.txt') (Join-Path $taskLicenses 'Microsoft-Dotnet-LICENSE.txt') -Force
Copy-Item (Join-Path $taskRoot '.tools/nuget/system.security.cryptography.protecteddata/10.0.12/THIRD-PARTY-NOTICES.TXT') (Join-Path $taskLicenses 'ProtectedData-NOTICES.txt') -Force
$taskCompilerRoot = Split-Path (Split-Path (Get-Command g++ -ErrorAction Stop).Source -Parent) -Parent
foreach ($taskRuntime in @('gcc','mingw-w64','winpthreads')) {
    $taskRuntimeLicense = Join-Path $taskCompilerRoot "licenses/$taskRuntime"
    if (Test-Path $taskRuntimeLicense) { Copy-Item $taskRuntimeLicense $taskLicenses -Recurse -Force }
}
Write-Output "Desktop application: $(Join-Path $taskOutput 'ArchivePassword.exe')"
