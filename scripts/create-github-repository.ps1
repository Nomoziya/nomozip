param([string]$Repository = 'Nomoziya/nomozip')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskGh = Join-Path $taskRoot '.tools/github-cli/bin/gh.exe'
if (-not (Test-Path -LiteralPath $taskGh)) { throw 'The project-local official GitHub CLI must be downloaded first.' }
if ($Repository -ne 'Nomoziya/nomozip') { throw 'Only the user-approved repository is supported by this script.' }
$env:GH_CONFIG_DIR = Join-Path $taskRoot '.tools/gh-config'
$env:GH_PROMPT_DISABLED = '1'
$taskPriorToken = $env:GH_TOKEN
$taskTokenBytes = $null
try {
    $taskCredentialPath = Join-Path $env:GH_CONFIG_DIR 'github-token.dpapi'
    if (-not $env:GH_TOKEN -and (Test-Path -LiteralPath $taskCredentialPath)) {
        $taskTokenBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
            [System.IO.File]::ReadAllBytes($taskCredentialPath), $null,
            [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        $env:GH_TOKEN = [System.Text.Encoding]::UTF8.GetString($taskTokenBytes)
    }
$taskLogin = & $taskGh api user --jq .login
if ($LASTEXITCODE -ne 0) { throw 'Complete GitHub CLI browser authorization before creating the repository.' }
if ($taskLogin.Trim() -ne 'Nomoziya') { throw 'The authenticated account does not match the approved repository owner.' }
$taskExisting = & $taskGh repo view $Repository --json nameWithOwner,isPrivate,url 2>$null
if ($LASTEXITCODE -eq 0) {
    $taskRepository = $taskExisting | ConvertFrom-Json
    if ($taskRepository.isPrivate) { throw 'An existing private repository was found; it has not been changed.' }
    Write-Output ('Public repository already exists: ' + $taskRepository.url)
    return
}
& $taskGh repo create $Repository --public --description 'Windows archive extraction app with an encrypted password vault and updatable official 7-Zip engines.'
if ($LASTEXITCODE -ne 0) { throw 'GitHub repository creation failed.' }
$taskVerified = & $taskGh repo view $Repository --json nameWithOwner,isPrivate,url
if ($LASTEXITCODE -ne 0) { throw 'Repository creation returned, but its visibility could not be verified.' }
$taskRepository = $taskVerified | ConvertFrom-Json
if ($taskRepository.isPrivate -or $taskRepository.nameWithOwner -ne $Repository) { throw 'Repository verification did not match the requested public repository.' }
Write-Output ('Verified public repository: ' + $taskRepository.url)
} finally {
    $env:GH_TOKEN = $taskPriorToken
    if ($taskTokenBytes) { [Array]::Clear($taskTokenBytes, 0, $taskTokenBytes.Length) }
}
