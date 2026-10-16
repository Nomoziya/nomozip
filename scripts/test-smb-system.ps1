param([Parameter(Mandatory=$true)][string]$ApplicationDirectory)
$ErrorActionPreference='Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run SMB share setup only on a disposable GitHub Actions runner.' }
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskRun=Join-Path $taskRoot ('artifacts/smb-system-'+[Guid]::NewGuid().ToString('N'))
$taskReport=Join-Path $taskRoot 'artifacts/smb-system-validation.json'
$taskChecks=[ordered]@{}
$taskResults=[Collections.Generic.List[object]]::new()
$taskShare='nomozip-test-'+[Guid]::NewGuid().ToString('N')
$taskShareCreated=$false
$taskFailure=$null
$taskDialects=@()
$taskPathProbe=$null
function Check-Smb([string]$Name,[bool]$Value) { $taskChecks[$Name]=$Value;if(!$Value){throw "SMB acceptance failed: $Name"} }
function Invoke-SmbCli([string[]]$CliArguments,[int]$ExpectedExit=0) {
 $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $ApplicationDirectory 'nomozip-cli.exe'))
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($argument in $CliArguments){$info.ArgumentList.Add($argument)}
 $info.ArgumentList.Add('--data-dir');$info.ArgumentList.Add((Join-Path $taskRun 'profile'))
 $process=[Diagnostics.Process]::new();$process.StartInfo=$info
 try {
  if(!$process.Start()){throw 'CLI process did not start.'}
  $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
  if(!$process.WaitForExit(45000)){$process.Kill($true);throw 'CLI operation timed out.'}
  $result=$stdout.GetAwaiter().GetResult() | ConvertFrom-Json
  $errorText=$stderr.GetAwaiter().GetResult()
  $taskResults.Add(@{operation=$CliArguments[0];expectedExit=$ExpectedExit;actualExit=$process.ExitCode;jsonOutput=($null -ne $result)})
  if($process.ExitCode -ne $ExpectedExit){throw "CLI $($CliArguments[0]) exit $($process.ExitCode), expected $ExpectedExit. Result: $($result | ConvertTo-Json -Compress -Depth 6) $errorText"}
 } finally {$process.Dispose()}
}
try {
 New-Item -ItemType Directory -Force $taskRun,(Join-Path $taskRun 'share'),(Join-Path $taskRun 'profile') | Out-Null
 $taskSource=Join-Path $taskRun 'share/输入示例.txt'
 [IO.File]::WriteAllText($taskSource,'nomozip real SMB protocol acceptance: synthetic Unicode file.',[Text.UTF8Encoding]::new($false))
 $taskOriginal=(Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash
 Invoke-SmbCli @('create',$taskSource,'--output',(Join-Path $taskRun 'local-control.zip'),'--format','zip','--engine','26.03')
 Check-Smb 'same product local creation control' (Test-Path -LiteralPath (Join-Path $taskRun 'local-control.zip'))
 $taskUser=[Security.Principal.WindowsIdentity]::GetCurrent().Name
 New-SmbShare -Name $taskShare -Path (Join-Path $taskRun 'share') -FullAccess $taskUser -Temporary -CachingMode None | Out-Null
 $taskShareCreated=$true
 $taskUnc='\\localhost\'+$taskShare
 Check-Smb 'UNC file exact bytes' ((Get-FileHash -LiteralPath ($taskUnc+'\输入示例.txt') -Algorithm SHA256).Hash -eq $taskOriginal)
 $taskConnections=@(Get-SmbConnection | Where-Object ShareName -eq $taskShare)
 $taskDialects=@($taskConnections | ForEach-Object {[string]$_.Dialect})
 Check-Smb 'actual SMB client connection and negotiated dialect' ($taskConnections.Count -gt 0 -and $taskDialects.Count -gt 0)
 $taskPathProbe=(& (Join-Path $taskRoot 'artifacts/smb-path-probe.exe') ($taskUnc+'\输入示例.txt') ($taskUnc+'\native-create.probe')) | ConvertFrom-Json
 if($LASTEXITCODE -ne 0 -or $null -eq $taskPathProbe){throw "Native SMB path probe failed: exit $LASTEXITCODE"}
 foreach($taskCase in @(
  @{name='UNC source and local output';source=($taskUnc+'\输入示例.txt');output=(Join-Path $taskRun 'unc-read-control.zip')},
  @{name='local source and UNC output';source=$taskSource;output=($taskUnc+'\unc-write-control.zip')}
 )) {
  try {Invoke-SmbCli @('create',$taskCase.source,'--output',$taskCase.output,'--format','zip','--engine','26.03');$taskChecks[$taskCase.name]=$true}
  catch {$taskChecks[$taskCase.name]=$false;$taskResults.Add(@{diagnosticCase=$taskCase.name;error=$_.Exception.Message})}
 }
 foreach($taskVersion in @('26.03','26.04')) {
  foreach($taskFormat in @('zip','7z')) {
   $taskArchive=$taskUnc+'\archive-'+$taskVersion+'.'+$taskFormat
   $taskTarget=$taskUnc+'\result-'+$taskVersion+'-'+$taskFormat
   Invoke-SmbCli @('create',($taskUnc+'\输入示例.txt'),'--output',$taskArchive,'--format',$taskFormat,'--engine',$taskVersion)
   Invoke-SmbCli @('test',$taskArchive,'--engine',$taskVersion)
   Invoke-SmbCli @('extract',$taskArchive,'--output',$taskTarget,'--engine',$taskVersion)
   $taskExtracted=@(Get-ChildItem -LiteralPath $taskTarget -Recurse -File -Filter '输入示例.txt')
   Check-Smb "$taskVersion $taskFormat exact extracted file over UNC" ($taskExtracted.Count -eq 1 -and (Get-FileHash -LiteralPath $taskExtracted[0].FullName -Algorithm SHA256).Hash -eq $taskOriginal)
   Invoke-SmbCli @('extract',$taskArchive,'--output',$taskTarget,'--engine',$taskVersion) 2
   Invoke-SmbCli @('extract',$taskArchive,'--output',$taskTarget,'--engine',$taskVersion,'--on-conflict','skip') 1
   Check-Smb "$taskVersion $taskFormat conflict kept prior output" ((Get-FileHash -LiteralPath $taskExtracted[0].FullName -Algorithm SHA256).Hash -eq $taskOriginal)
   Invoke-SmbCli @('extract',$taskArchive,'--output',$taskTarget,'--engine',$taskVersion,'--on-conflict','replace')
   Check-Smb "$taskVersion $taskFormat replacement exact bytes" ((Get-FileHash -LiteralPath $taskExtracted[0].FullName -Algorithm SHA256).Hash -eq $taskOriginal)
  }
 }
 Check-Smb 'original network source preserved' ((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -eq $taskOriginal)
} catch {$taskFailure=$_.Exception.Message}
finally {
 if($taskShareCreated){try{Remove-SmbShare -Name $taskShare -Force -Confirm:$false}catch{$taskFailure='Temporary share cleanup failed.'}}
 $taskChecks['temporary share removed']=($null -eq (Get-SmbShare -Name $taskShare -ErrorAction SilentlyContinue))
 $taskPassed=(!$taskFailure -and @($taskChecks.Values | Where-Object {!$_}).Count -eq 0)
 @{passed=$taskPassed;scope='Real SMB loopback on disposable Windows runner; not a remote NAS, LAN outage, or visible Explorer test.';syntheticData=$true;dialects=$taskDialects;nativePathProbe=$taskPathProbe;checks=$taskChecks;cliOperations=$taskResults;failure=$taskFailure} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskReport -Encoding utf8
}
if(!$taskPassed){throw "SMB system acceptance failed: $taskFailure"}
Write-Output "Real SMB acceptance passed: $($taskChecks.Count) checks, $($taskResults.Count) CLI operations."
