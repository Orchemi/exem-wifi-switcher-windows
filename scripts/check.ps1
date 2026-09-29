[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$failures = [Collections.Generic.List[string]]::new()
$sourceFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | Where-Object {
    $_.FullName -notmatch '[\\/](\.git|bin|obj|dist|artifacts)[\\/]'
})
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($root.Length + 1)
    if ($file.Extension -eq '.ps1') {
        $tokens = $null; $errors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { $failures.Add("PowerShell syntax: $relative") }
    }
    if ($file.Name -in @('config.json', 'snapshot.json', '.DS_Store') -or $file.Extension -in @('.pfx', '.pem', '.key', '.log')) {
        $failures.Add("Forbidden file: $relative")
    }
    $text = [IO.File]::ReadAllText($file.FullName)
    # SDK/TFM/package versions are not addresses. Remove only the known build metadata contexts.
    if ($file.Extension -eq '.csproj') {
        $text = $text -replace '<TargetFramework>[^<]+</TargetFramework>', '' -replace 'Version="[0-9.]+"', ''
    }
    if ($file.Name -eq 'global.json') { $text = $text -replace '"version":\s*"[0-9.]+"', '' }
    if ($relative -replace "\\", "/" -eq ".github/workflows/ci.yml") { $text = $text -replace "dotnet-version: '10[.]0[.]x'", "" }
    $private = '\b(?:10\.[0-9]{1,3}\.|192\.168\.|172\.(?:1[6-9]|2[0-9]|3[01])\.)'
    $mac = '(?i)(?:[0-9a-f]{2}:){5}[0-9a-f]{2}'
    $homePattern = '/' + 'Users/' + '[^/\s]+' + '|[A-Za-z]:\\' + 'Users\\[^\\\s]+'
    $secrets = '(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----)'
    if ($text -match $private -or $text -match $mac -or $text -match $homePattern -or $text -match $secrets) {
        # Never print matched values: a failed privacy check must not publish the secret in CI logs.
        $failures.Add("Privacy review required: $relative")
    }
}
if ($failures.Count) { $failures | Write-Output; exit 1 }
Write-Output "PASS: PowerShell syntax and source privacy scan ($($sourceFiles.Count) files)"
