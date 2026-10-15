param([Parameter(Mandatory)][string]$OutputDirectory, [string]$ApplicationDirectory,
    [string]$Python = 'python', [string]$ValidationReport, [string]$PublishedSource, [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')).TrimEnd('\') + '\'
if (-not $taskOutput.StartsWith($taskArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Local release output must be a new directory under artifacts.' }
if (Test-Path -LiteralPath $taskOutput) { throw 'Keep prior packages immutable; choose a new output directory.' }
if (-not $ValidationReport) { $ValidationReport = $taskOutput + '-validation.json' }
$ValidationReport = [IO.Path]::GetFullPath($ValidationReport)
if ($ValidationReport.StartsWith($taskOutput.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the review report outside the four-asset release directory.' }
[xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskRoot 'src/Desktop/ArchivePassword.Desktop.csproj') -Raw
$taskVersion = [string]$taskProject.Project.PropertyGroup.Version
$taskFileVersion = [string]$taskProject.Project.PropertyGroup.FileVersion
if ($taskVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or $taskFileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Project release versions cannot be used as package names.' }
$taskBuild = Join-Path $taskRoot ('artifacts/release-build/' + [Guid]::NewGuid().ToString('N'))
if (-not $ApplicationDirectory) {
    $ApplicationDirectory = Join-Path $taskBuild 'payload'
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -SkipWorker -OutputDirectory $ApplicationDirectory -Architecture $Architecture
} else { $ApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory) }
$taskApp = Join-Path $ApplicationDirectory 'ArchivePassword.exe'
if (-not (Test-Path -LiteralPath $taskApp)) { throw 'Application payload is missing.' }
$taskBinary = (Get-Item -LiteralPath $taskApp).VersionInfo
if ($taskBinary.ProductVersion.Split('+')[0] -ne $taskVersion -or $taskBinary.FileVersion -ne $taskFileVersion) { throw 'Application payload version differs from the current source project.' }
# Both collectors run before building an installer, so private/source/runtime failures create no release assets.
& $Python -c 'import sys;from pathlib import Path;sys.path.insert(0,sys.argv[1]);from package_preview import application_files,source_files,validate_architecture;application_files(Path(sys.argv[2]));validate_architecture(Path(sys.argv[2]),sys.argv[4]);source_files(Path(sys.argv[3]))' $PSScriptRoot $ApplicationDirectory $taskRoot $Architecture
if ($LASTEXITCODE -ne 0) { throw 'Release input review failed.' }
$taskCli = Join-Path $ApplicationDirectory 'nomozip-cli.exe'
if (-not (Test-Path -LiteralPath $taskCli) -or (Get-Item -LiteralPath $taskCli).VersionInfo.FileVersion -ne $taskFileVersion -or (Get-Item -LiteralPath $taskCli).VersionInfo.ProductVersion.Split('+')[0] -ne $taskVersion) { throw 'Console entry is missing or its version differs from the source.' }
$taskInstallerName = "nomozip-$taskVersion-light-setup-win-$Architecture.exe"
$taskInstaller = Join-Path $taskBuild $taskInstallerName
& (Join-Path $PSScriptRoot 'build-installer.ps1') -ApplicationDirectory $ApplicationDirectory -OutputFile $taskInstaller -FrameworkDependent -Architecture $Architecture
& $Python (Join-Path $PSScriptRoot 'package_preview.py') --app-directory $ApplicationDirectory --output-directory $taskOutput --version "$taskVersion-light" --source-name "nomozip-source-$taskVersion.zip" --architecture $Architecture
if ($LASTEXITCODE -ne 0) { throw 'Lightweight archive packaging failed.' }
Copy-Item -LiteralPath $taskInstaller -Destination (Join-Path $taskOutput $taskInstallerName)
$taskHashes = Get-ChildItem -LiteralPath $taskOutput -File | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllText((Join-Path $taskOutput 'SHA256SUMS.txt'), ($taskHashes -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$taskArguments = @((Join-Path $PSScriptRoot 'review_release.py'), '--app-directory', $ApplicationDirectory, '--release-directory', $taskOutput, '--installer-path', $taskInstaller, '--version', $taskVersion, '--report', $ValidationReport, '--architecture', $Architecture)
if ($PublishedSource) { $taskArguments += @('--published-source', [IO.Path]::GetFullPath($PublishedSource)) }
& $Python @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Final release package review failed; inspect the report. Existing packages were retained.' }
Write-Output "Local lightweight assets: $taskOutput"
Write-Output "Package review: $ValidationReport"
