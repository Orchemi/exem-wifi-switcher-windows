# CI-only lifecycle smoke test. Uses a nonexistent adapter and observe mode; never changes networking.
#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackagePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or $env:GITHUB_ACTIONS -ne 'true') {
    throw 'This lifecycle test is restricted to an ephemeral Windows GitHub Actions runner.'
}
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$installed = Join-Path $env:ProgramFiles 'WifiProfileSwitcher'
$data = Join-Path $env:ProgramData 'WifiProfileSwitcher'
if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $data) -or (Get-Service WifiProfileSwitcher -ErrorAction SilentlyContinue)) {
    throw 'Refusing to test over an existing installation.'
}
function Read-Host {
    param([string]$Prompt)
    if ($Prompt -match '\b(INSTALL|CONFIGURE|UNINSTALL)\b') { return $Matches[1] }
    throw 'Unexpected confirmation; this test must never enable network writes.'
}
try {
    & (Join-Path $package 'scripts\install.ps1') -ConfigPath (Join-Path $package 'config.example.json')
    if ($LASTEXITCODE -ne 0) { throw 'Install failed' }
    # An attempted second installation must fail without deleting the first one.
    & (Join-Path $package 'scripts\install.ps1') -ConfigPath (Join-Path $package 'config.example.json')
    if ($LASTEXITCODE -eq 0 -or -not (Test-Path -LiteralPath (Join-Path $data 'config.json'))) {
        throw 'Reinstall refusal damaged the existing installation'
    }
    $exe = Join-Path $installed 'WifiProfileSwitcher.exe'
    & $exe validate --config (Join-Path $data 'config.json')
    if ($LASTEXITCODE -ne 0) { throw 'Installed config validation failed' }
    $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json
    if ($config.mode -ne 'observe') { throw 'Install must force observe' }
    Start-Service WifiProfileSwitcher
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $status = $null
    do {
        Start-Sleep -Milliseconds 500
        $statusPath = Join-Path $data 'status.json'
        if (Test-Path -LiteralPath $statusPath) { $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json }
    } while ($null -eq $status -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $status -or $status.mode -ne 'observe' -or $status.mutationAttempted) { throw 'Observe service status missing or unsafe' }
    & $exe status
    if ($LASTEXITCODE -ne 0) { throw 'Status failed' }
    Stop-Service WifiProfileSwitcher
    & (Join-Path $installed 'scripts\configure.ps1') -ConfigPath (Join-Path $package 'config.example.json')
    if ($LASTEXITCODE -ne 0) { throw 'Configure failed' }
    if ((Get-Service WifiProfileSwitcher).Status -ne 'Stopped') { throw 'Configure must preserve stopped service' }
    $configPath = Join-Path $data 'config.json'
    $beforeHash = (Get-FileHash -LiteralPath $configPath).Hash
    $badConfig = Join-Path $env:RUNNER_TEMP 'invalid-wifi-config.json'
    [IO.File]::WriteAllText($badConfig, '{"adapterId":"invalid"}')
    & (Join-Path $installed 'scripts\configure.ps1') -ConfigPath $badConfig
    if ($LASTEXITCODE -eq 0 -or (Get-FileHash -LiteralPath $configPath).Hash -ne $beforeHash) {
        throw 'Invalid configuration did not preserve the previous file'
    }
    Remove-Item -LiteralPath $badConfig
    & (Join-Path $installed 'scripts\uninstall.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Uninstall failed' }
    if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $data) -or (Get-Service WifiProfileSwitcher -ErrorAction SilentlyContinue)) {
        throw 'Uninstall left managed files or service'
    }
    Write-Output 'PASS: Windows protected install, observe service, configure, uninstall; no network changes'
} finally {
    # Cleanup only our service if a test fails. Keep diagnostic files in the ephemeral runner, never upload them.
    $service = Get-Service WifiProfileSwitcher -ErrorAction SilentlyContinue
    if ($null -ne $service) {
        Stop-Service WifiProfileSwitcher -ErrorAction SilentlyContinue
        & "$env:SystemRoot\System32\sc.exe" delete WifiProfileSwitcher | Out-Null
    }
}
