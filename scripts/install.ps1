#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$PackagePath,
    [switch]$GuiConfirmed
)

. (Join-Path -Path $PSScriptRoot -ChildPath 'Common.ps1')

Set-StrictMode -Version 2.0

$createdInstallRoot = $false
$createdDataRoot = $false
$createdService = $false
$installRoot = $null
$dataRoot = $null

function Remove-NewInstallData {
    param([string]$InstallRoot, [string]$DataRoot)

    if ($createdService) {
        try {
            $service = Read-ServiceSafe
            if ($null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
                Stop-Service -InputObject $service -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
        }
        finally {
            if ($null -ne $service) {
                $service.Dispose()
            }
        }
        try {
            $scPath = Join-Path -Path $env:SystemRoot -ChildPath 'System32\sc.exe'
            & $scPath 'delete' $script:WifiServiceName *> $null
        }
        catch {
        }
    }

    foreach ($path in @($InstallRoot, $DataRoot)) {
        if ([string]::IsNullOrEmpty($path)) {
            continue
        }
        if ($path -eq $InstallRoot -and -not $createdInstallRoot) {
            continue
        }
        if ($path -eq $DataRoot -and -not $createdDataRoot) {
            continue
        }
        if (Test-Path -LiteralPath $path -PathType Container) {
            try {
                Assert-SafeTree -Path $path
                Remove-Item -LiteralPath (Get-CanonicalPath -Path $path) -Recurse -Force -ErrorAction SilentlyContinue
            }
            catch {
            }
        }
    }
}

try {
    Assert-WindowsPlatform
    Assert-Administrator

    $packageRoot = if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        Split-Path -Parent $PSScriptRoot
    }
    else {
        Get-CanonicalPath -Path $PackagePath
    }
    Assert-ExistingDirectory -Path $packageRoot
    Assert-NoReparsePath -Path $packageRoot

    $installRoot = Get-ProgramFilesRoot
    $dataRoot = Get-ProgramDataRoot
    Assert-NewPath -Path $installRoot
    Assert-NewPath -Path $dataRoot

    $existingService = Get-Service -Name $script:WifiServiceName -ErrorAction SilentlyContinue
    if ($null -ne $existingService) {
        Fail '같은 이름의 서비스가 이미 있습니다. 기존 설치를 덮어쓰지 않습니다.'
    }

    $sourceExe = Join-Path -Path $packageRoot -ChildPath 'WifiProfileSwitcher.exe'
    Assert-RegularFile -Path $sourceExe

    $sourceScriptsRoot = Join-Path -Path $packageRoot -ChildPath 'scripts'
    Assert-ExistingDirectory -Path $sourceScriptsRoot
    $runtimeScriptNames = @('network.ps1', 'Common.ps1', 'install.ps1', 'configure.ps1', 'uninstall.ps1', 'service-control.ps1')
    foreach ($scriptName in $runtimeScriptNames) {
        Assert-RegularFile -Path (Join-Path -Path $sourceScriptsRoot -ChildPath $scriptName)
    }

    $sourceConfig = if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        Join-Path -Path $packageRoot -ChildPath 'config.example.json'
    }
    else {
        Get-CanonicalPath -Path $ConfigPath
    }
    Assert-RegularFile -Path $sourceConfig
    $configObject = Read-JsonObjectSafe -Path $sourceConfig
    $configObject = Set-ConfigMode -Config $configObject -Mode observe
    $configJson = Convert-ConfigToJson -Config $configObject

    $packageFiles = @(Get-ChildItem -LiteralPath $packageRoot -File -Force -ErrorAction Stop | Where-Object {
            $_.Name -eq 'WifiProfileSwitcher.exe' -or
            $_.Extension -eq '.dll' -or
            $_.Name -eq 'WifiProfileSwitcher.deps.json' -or
            $_.Name -eq 'WifiProfileSwitcher.runtimeconfig.json' -or
            $_.Name -in @('LICENSE', 'DOTNET-LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'WINDOWS-SDK-NOTICE.txt')
        })

    $preview = @(
        ('보호된 설치 디렉터리 생성: {0}' -f $installRoot),
        ('보호된 설정 디렉터리 생성: {0}' -f $dataRoot),
        '패키지의 실행 파일과 런타임 파일을 보호 디렉터리에 복사',
        '제공된 설정을 observe 모드로 강제하여 저장',
        'WifiProfileSwitcher 서비스를 LocalSystem·자동(지연 시작)으로 등록',
        '설치 직후 서비스는 시작하지 않음. 실제 IP 변경은 발생하지 않음',
        '런타임 PowerShell은 조직 승인 범위의 자식 프로세스에만 -ExecutionPolicy RemoteSigned를 사용하며 영구 정책·GPO는 변경하지 않음',
        '패키지와 설정 값은 실행 전에 동료가 직접 검토해야 함'
    )
    Confirm-Plan -Lines $preview -Confirmation INSTALL -GuiConfirmed:$GuiConfirmed

    New-Item -ItemType Directory -Path $installRoot -Force:$false -ErrorAction Stop | Out-Null
    $createdInstallRoot = $true
    Protect-Directory -Path $installRoot -AllowUsersRead

    New-Item -ItemType Directory -Path $dataRoot -Force:$false -ErrorAction Stop | Out-Null
    $createdDataRoot = $true
    Protect-Directory -Path $dataRoot

    foreach ($file in $packageFiles) {
        $destination = Join-Path -Path $installRoot -ChildPath $file.Name
        Copy-SafeFile -Source $file.FullName -Destination $destination
        Protect-File -Path $destination -AllowUsersRead
    }

    $destinationScriptsRoot = Join-Path -Path $installRoot -ChildPath 'scripts'
    New-Item -ItemType Directory -Path $destinationScriptsRoot -Force:$false -ErrorAction Stop | Out-Null
    Protect-Directory -Path $destinationScriptsRoot -AllowUsersRead
    foreach ($scriptName in $runtimeScriptNames) {
        $source = Join-Path -Path $sourceScriptsRoot -ChildPath $scriptName
        $destination = Join-Path -Path $destinationScriptsRoot -ChildPath $scriptName
        Copy-SafeFile -Source $source -Destination $destination
        Protect-File -Path $destination -AllowUsersRead
    }

    $configPathOnDisk = Get-ExpectedConfigPath
    Write-TextFileAtomic -Path $configPathOnDisk -Content $configJson
    Protect-File -Path $configPathOnDisk

    $installedExe = Get-ExpectedExecutablePath
    Assert-ExecutableValidation -ExecutablePath $installedExe -ConfigPath $configPathOnDisk

    $serviceBinaryPath = '"{0}" service' -f (Get-CanonicalPath -Path $installedExe)
    New-Service -Name $script:WifiServiceName -BinaryPathName $serviceBinaryPath -DisplayName 'Wifi Profile Switcher' -Description '연결된 Wi-Fi에 따라 IPv4 프로필을 관찰하고 승인된 경우 전환합니다.' -StartupType Automatic -ErrorAction Stop | Out-Null
    $createdService = $true

    $scPath = Join-Path -Path $env:SystemRoot -ChildPath 'System32\sc.exe'
    & $scPath 'config' $script:WifiServiceName 'start=' 'delayed-auto' *> $null
    if ($LASTEXITCODE -ne 0) {
        Fail '서비스를 지연 자동 시작으로 설정하지 못했습니다.'
    }

    Write-Info '설치를 완료했습니다. 현재 설정은 observe 모드이며 서비스는 아직 시작하지 않았습니다.'
    Write-Info '설정 변경은 보호된 설치본의 scripts\configure.ps1을 사용하십시오.'
}
catch {
    $errorMessage = $_.Exception.Message
    Remove-NewInstallData -InstallRoot $installRoot -DataRoot $dataRoot
    Write-Error $errorMessage -ErrorAction Continue
    exit 1
}
