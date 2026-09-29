[CmdletBinding()]
param([ValidateSet('win-x64')][string]$Runtime = 'win-x64')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'package.ps1') -Runtime $Runtime
    if ($LASTEXITCODE -ne 0) { throw 'Runtime package failed' }
    $version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    $zip = Join-Path $root "dist/wifi-profile-switcher-$Runtime-$version.zip"
    & dotnet restore src/WifiProfileSwitcher.Setup -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw 'Setup restore failed' }
    $resolved = & dotnet msbuild src/WifiProfileSwitcher.Setup -t:ResolveReferences "-p:RuntimeIdentifier=$Runtime" -p:SelfContained=true -getItem:ResolvedRuntimePack
    if ($LASTEXITCODE -ne 0) { throw 'Setup runtime resolution failed' }
    $packs = ($resolved -join "`n" | ConvertFrom-Json).Items.ResolvedRuntimePack
    $desktop = @($packs | Where-Object FrameworkName -eq 'Microsoft.WindowsDesktop.App')[0]
    $desktopLicense = Join-Path $desktop.PackageDirectory 'LICENSE'
    if (-not (Test-Path -LiteralPath $desktopLicense)) { throw 'Desktop runtime license missing' }
    $destination = Join-Path $root "dist/setup-$Runtime"
    & dotnet publish src/WifiProfileSwitcher.Setup -c Release -r $Runtime --self-contained true -o $destination `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        "-p:SetupPayload=$zip" "-p:DesktopLicense=$desktopLicense"
    if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed' }
    # Inspect the application assembly before its compressed single-file embedding.
    $assembly = Join-Path $root "src/WifiProfileSwitcher.Setup/obj/Release/net10.0-windows10.0.19041.0/$Runtime/WifiProfileSwitcher-Setup.dll"
    $bytes = [IO.File]::ReadAllBytes($assembly)
    $text = [Text.Encoding]::UTF8.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
    $homePattern = '/' + 'Users/' + '|[A-Za-z]:\\' + 'Users\\'
    if ($text -match $homePattern) { throw 'Builder path found in setup assembly' }
    $source = Join-Path $destination 'WifiProfileSwitcher-Setup.exe'
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Setup executable missing' }
    $setup = Join-Path $root "dist/WifiProfileSwitcher-Setup-$version.exe"
    Copy-Item -LiteralPath $source -Destination $setup -Force
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$setup.sha256", "$hash  $([IO.Path]::GetFileName($setup))`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Setup: $([IO.Path]::GetFileName($setup))"
    Write-Output "SHA-256: $hash"
} finally { Pop-Location }
