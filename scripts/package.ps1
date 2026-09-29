[CmdletBinding()]
param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Source check failed' }
    & dotnet test tests/WifiProfileSwitcher.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
    & pwsh -NoProfile -File tests/NetworkBackend.Tests.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Network backend tests failed' }
    & pwsh -NoProfile -File tests/Installer.Tests.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Installer tests failed' }
    $destination = Join-Path $root "dist/$Runtime"
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    & dotnet publish src/WifiProfileSwitcher.Windows -c Release -r $Runtime --self-contained true -o $destination -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
    # Explicit allowlist: no local config, diagnostics, backup, or build workspace is copied.
    foreach ($name in @('Common.ps1', 'install.ps1', 'configure.ps1', 'uninstall.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destination 'scripts')
    }
    foreach ($name in @('config.example.json', 'README.md', 'RULES.md', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $root $name) -Destination $destination
    }
    New-Item -ItemType Directory -Path (Join-Path $destination 'docs') -Force | Out-Null
    foreach ($name in @('TESTING.md', 'PERMISSIONS.md')) {
        Copy-Item -LiteralPath (Join-Path $root "docs/$name") -Destination (Join-Path $destination 'docs')
    }
    # Redistribute the runtime's own notices alongside its binaries.
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $destination 'WifiProfileSwitcher.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimeVersion = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' })[0].version
    $cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
    $runtimePackage = Join-Path $cache "microsoft.netcore.app.runtime.$Runtime/$runtimeVersion"
    Copy-Item -LiteralPath (Join-Path $runtimePackage 'LICENSE.TXT') -Destination (Join-Path $destination 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $runtimePackage 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $destination 'THIRD-PARTY-NOTICES.txt')
    [IO.File]::WriteAllText((Join-Path $destination 'WINDOWS-SDK-NOTICE.txt'), "Microsoft.Windows.SDK.NET and WinRT runtime components`nCopyright Microsoft Corporation. All rights reserved.`nWindows SDK terms: https://aka.ms/WinSDKLicenseURL`nC#/WinRT: https://github.com/microsoft/CsWinRT/blob/master/LICENSE`n", [Text.UTF8Encoding]::new($false))
    $forbidden = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object {
        $_.Name -in @('config.json', 'snapshot.json', 'status.json', '.DS_Store') -or $_.Extension -in @('.pdb', '.log', '.pfx', '.key')
    })
    if ($forbidden.Count) { throw 'Forbidden package content' }
    foreach ($textFile in @(Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object { $_.Extension -in @('.json', '.md', '.ps1', '.txt') })) {
        $content = [IO.File]::ReadAllText($textFile.FullName)
        # Generated dependency metadata contains framework versions, never user settings.
        if ($textFile.Name -like '*.deps.json' -or $textFile.Name -like '*.runtimeconfig.json') {
            $content = $content -replace '(?<![0-9])[0-9]+[.][0-9]+[.][0-9]+(?:[.][0-9]+)?', ''
        }
        $privatePattern = '\b(?:10\.[0-9]{1,3}\.|192\.168\.|172\.(?:1[6-9]|2[0-9]|3[01])\.)'
        $homePattern = '/' + 'Users/' + '|[A-Za-z]:\\' + 'Users\\'
        $secretPattern = '(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----)'
        if ($content -match $privatePattern -or $content -match $homePattern -or $content -match $secretPattern -or
            $content -match '(?i)(?:[0-9a-f]{2}:){5}[0-9a-f]{2}') {
            throw 'Privacy check failed for package text (values withheld)'
        }
    }
    # Our managed assemblies must not embed a builder home path. Dependency binaries originate from NuGet/.NET.
    foreach ($assembly in @(Get-ChildItem -LiteralPath $destination -Filter 'WifiProfileSwitcher*' -File | Where-Object { $_.Extension -in @('.dll', '.exe') })) {
        $bytes = [IO.File]::ReadAllBytes($assembly.FullName)
        $binaryText = [Text.Encoding]::UTF8.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
        $homePattern = '/' + 'Users/' + '|[A-Za-z]:\\' + 'Users\\'
        if ($binaryText -match $homePattern) { throw 'Builder path found in application binary' }
        # SupportedOSPlatform attributes embed this SDK version, not an IP address.
        $binaryText = $binaryText -replace '10[.]0[.]19041[.]0', ''
        # The exe is Microsoft's generated apphost (it contains runtime version strings). Our code lives in the DLLs.
        if (($assembly.Extension -eq '.dll' -and $binaryText -match $privatePattern) -or $binaryText -match $secretPattern) { throw 'Privacy check failed for application binary' }
    }
    $zip = Join-Path $root "dist/wifi-profile-switcher-$Runtime-0.1.0-alpha.1.zip"
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Prototype package: $([IO.Path]::GetFileName($zip))"
    Write-Output "SHA-256: $hash"
} finally { Pop-Location }
