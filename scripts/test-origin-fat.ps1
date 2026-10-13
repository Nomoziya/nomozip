$ErrorActionPreference = 'Stop'
# Disk formatting is restricted to a fresh VHD on an ephemeral GitHub-hosted Windows VM.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') { throw 'Virtual filesystem validation is CI-only; no local disks are changed.' }
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$taskFixture = Join-Path $taskRoot ('artifacts/origin-fat/' + [guid]::NewGuid().ToString('N'))
$taskImagePath = [IO.Path]::GetFullPath((Join-Path $taskFixture 'origin-fixture.vhd'))
if (-not $taskImagePath.StartsWith(([IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/origin-fat')).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase) -or $taskImagePath.Contains('"') -or (Test-Path -LiteralPath $taskImagePath)) { throw 'VHD path must be a new owned fixture.' }
New-Item -ItemType Directory -Path $taskFixture | Out-Null
$taskBeforeNumbers = @(Get-Disk | ForEach-Object Number)
$taskCreateScript = Join-Path $taskFixture 'create-vhd.txt'
# DiskPart only creates an image file. All partitioning/formatting uses a verified image-associated CIM object.
('create vdisk file="' + $taskImagePath + '" maximum=64 type=expandable') | Set-Content -LiteralPath $taskCreateScript -Encoding ascii
$taskCreate = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/diskpart.exe') -ArgumentList @('/s', ('"' + $taskCreateScript + '"')) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $taskFixture 'create.log') -RedirectStandardError (Join-Path $taskFixture 'create-error.log')
if ($taskCreate.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $taskImagePath)) { throw 'Owned VHD creation failed.' }
$taskMounted = $false
try {
    $taskImage = Mount-DiskImage -ImagePath $taskImagePath -StorageType VHD -PassThru
    $taskMounted = $true
    if (-not $taskImage.Attached -or [IO.Path]::GetFullPath($taskImage.ImagePath) -ne $taskImagePath) { throw 'Mounted image identity mismatch.' }
    $taskDisks = @($taskImage | Get-Disk)
    if ($taskDisks.Count -ne 1) { throw 'Image must resolve to exactly one disk.' }
    $taskOwnedDisk = $taskDisks[0]
    $taskOwnedDisk | Select-Object Number, Size, BusType, PartitionStyle, IsBoot, IsSystem | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskFixture 'disk-identity.json')
    if ($taskOwnedDisk.Number -in $taskBeforeNumbers -or $taskOwnedDisk.IsBoot -or $taskOwnedDisk.IsSystem -or $taskOwnedDisk.Size -ne 64MB -or $taskOwnedDisk.PartitionStyle -ne 'RAW' -or $taskOwnedDisk.BusType -notin @('File Backed Virtual','Virtual',14,15)) { throw 'Refusing to initialize an unverified disk.' }
    if ($taskOwnedDisk.IsOffline) { $taskOwnedDisk | Set-Disk -IsOffline $false }
    if ($taskOwnedDisk.IsReadOnly) { $taskOwnedDisk | Set-Disk -IsReadOnly $false }
    $taskInitialized = $taskOwnedDisk | Initialize-Disk -PartitionStyle MBR -PassThru
    $taskPartition = New-Partition -DiskNumber $taskInitialized.Number -UseMaximumSize -AssignDriveLetter
    if ($taskPartition.DiskNumber -ne $taskOwnedDisk.Number -or -not $taskPartition.DriveLetter) { throw 'Owned partition identity mismatch.' }
    $taskVolume = $taskPartition | Format-Volume -FileSystem exFAT -NewFileSystemLabel 'NomoOriginTest' -Force -Confirm:$false
    if ($taskVolume.FileSystem -ne 'exFAT' -or $taskVolume.DriveLetter -ne $taskPartition.DriveLetter) { throw 'Expected an owned exFAT test volume.' }
    $taskOutput = Join-Path ($taskVolume.DriveLetter.ToString() + ':\') ('nomozip-origin-' + [guid]::NewGuid().ToString('N'))
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
    & (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') (Join-Path $taskRoot 'tests/OriginValidation/bin/Debug/net10.0-windows/OriginValidation.dll') $taskRoot '26.04,26.03' $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Actual exFAT extraction validation failed.' }
} finally {
    if ($taskMounted) { Dismount-DiskImage -ImagePath $taskImagePath }
}
