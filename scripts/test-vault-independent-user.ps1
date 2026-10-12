# This acceptance harness creates an account ONLY on a disposable GitHub VM.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Independent-user account testing is restricted to disposable GitHub-hosted Windows runners.'
}
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$taskFixture = Join-Path $taskRoot 'artifacts/vault-cross-machine'
$taskName = 'nvault-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$taskPassword = ConvertTo-SecureString ('Nz!' + [Guid]::NewGuid().ToString('N') + '7a') -AsPlainText -Force
$taskUser = $null
try {
    $taskUser = New-LocalUser -Name $taskName -Password $taskPassword -AccountNeverExpires -PasswordNeverExpires -Description 'Disposable nomozip vault acceptance'
    # Read the built product/fixtures; write only this repository's artifacts.
    & icacls $taskRoot /grant ('*' + $taskUser.SID.Value + ':(OI)(CI)RX') /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Test-user read permission setup failed.' }
    & icacls (Join-Path $taskRoot 'artifacts') /grant ('*' + $taskUser.SID.Value + ':(OI)(CI)M') /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Test-user artifact permission setup failed.' }
    $taskCredential = [PSCredential]::new(($env:COMPUTERNAME + '\' + $taskName), $taskPassword)
    $taskArguments = @(
        ('"' + (Join-Path $taskRoot 'tests/VaultTransferValidation/bin/Debug/net10.0-windows/VaultTransferValidation.dll') + '"'),
        ('"' + $taskRoot + '"'), 'cross-import', ('"' + $taskFixture + '"')
    )
    $taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') -ArgumentList $taskArguments `
        -Credential $taskCredential -LoadUserProfile -WorkingDirectory $taskRoot -WindowStyle Hidden -Wait -PassThru `
        -Environment @{DOTNET_CLI_HOME=(Join-Path $taskRoot 'artifacts/vault-independent-user-home'); DOTNET_CLI_TELEMETRY_OPTOUT='1'} `
        -RedirectStandardOutput (Join-Path $taskRoot 'artifacts/vault-independent-user-output.log') `
        -RedirectStandardError (Join-Path $taskRoot 'artifacts/vault-independent-user-error.log')
    Get-Content -LiteralPath (Join-Path $taskRoot 'artifacts/vault-independent-user-output.log')
    if ($taskProcess.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $taskRoot 'artifacts/vault-independent-user-error.log')
        throw 'Independent Windows user migration failed.'
    }
} finally {
    if ($taskUser) { Remove-LocalUser -SID $taskUser.SID }
    $taskPassword.Dispose()
}
