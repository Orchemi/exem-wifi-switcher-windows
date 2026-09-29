#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [switch]$Enable,
    [switch]$GuiConfirmed
)

. (Join-Path -Path $PSScriptRoot -ChildPath 'Common.ps1')

Set-StrictMode -Version 2.0

$dataRoot = $null
$targetConfig = $null
$temporaryConfig = $null
$rollbackConfig = $null
$replacementCompleted = $false
$hadConfig = $false
$serviceWasRunning = $false
$serviceNeedsStop = $false
$serviceStopped = $false

function Get-OptionalPropertyValue {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $null
    }
    return [string]$property.Value
}

function Get-ConfigReviewLines {
    param([Parameter(Mandatory = $true)][object]$Config)

    Write-Output '아래 값은 이 컴퓨터의 로컬 확인용입니다. 출력 내용을 외부에 공유하지 마십시오.'
    $adapterId = Get-OptionalPropertyValue -Object $Config -Name 'adapterId'
    Write-Output ('대상 adapterId: {0}' -f $(if ([string]::IsNullOrWhiteSpace($adapterId)) { '(없음)' } else { $adapterId }))

    $profilesProperty = $Config.PSObject.Properties['profiles']
    if ($null -eq $profilesProperty -or $null -eq $profilesProperty.Value) {
        Write-Output '프로필: 없음'
        return
    }

    $profiles = @($profilesProperty.Value)
    if ($profiles.Count -eq 0) {
        Write-Output '프로필: 없음'
        return
    }
    foreach ($profile in $profiles) {
        $id = Get-OptionalPropertyValue -Object $profile -Name 'id'
        $ssidsProperty = $profile.PSObject.Properties['ssids']
        $ssids = if ($null -eq $ssidsProperty -or $null -eq $ssidsProperty.Value) { @() } else { @($ssidsProperty.Value) }
        $profileMode = Get-OptionalPropertyValue -Object $profile -Name 'mode'
        $line = '프로필 id={0}; SSID={1}; mode={2}' -f $id, ($ssids -join ', '), $profileMode
        if ($profileMode -eq 'static') {
            $address = Get-OptionalPropertyValue -Object $profile -Name 'address'
            $prefix = Get-OptionalPropertyValue -Object $profile -Name 'prefixLength'
            $gateway = Get-OptionalPropertyValue -Object $profile -Name 'gateway'
            $dnsProperty = $profile.PSObject.Properties['dnsServers']
            $dns = if ($null -eq $dnsProperty -or $null -eq $dnsProperty.Value) { @() } else { @($dnsProperty.Value) }
            $line += '; IPv4={0}/{1}; gateway={2}; DNS={3}' -f $address, $prefix, $gateway, ($dns -join ', ')
        }
        Write-Output $line
    }
}

function Restore-PreviousConfig {
    param(
        [string]$Target,
        [string]$Rollback,
        [bool]$HadPrevious
    )

    try {
        if ($HadPrevious -and (Test-Path -LiteralPath $Rollback -PathType Leaf)) {
            Assert-RegularFile -Path $Rollback
            if (Test-Path -LiteralPath $Target -PathType Leaf) {
                Assert-RegularFile -Path $Target
                [System.IO.File]::Replace((Get-CanonicalPath -Path $Rollback), (Get-CanonicalPath -Path $Target), $null, $true)
            }
            else {
                [System.IO.File]::Move((Get-CanonicalPath -Path $Rollback), (Get-CanonicalPath -Path $Target))
            }
            Protect-File -Path $Target
        }
        elseif (-not $HadPrevious -and (Test-Path -LiteralPath $Target -PathType Leaf)) {
            Assert-RegularFile -Path $Target
            Remove-Item -LiteralPath (Get-CanonicalPath -Path $Target) -Force -ErrorAction Stop
        }
    }
    catch {
        Write-Warning '이전 설정으로 되돌리지 못했습니다. 서비스는 시작하지 않은 상태로 남겨 두었습니다.'
    }
}

try {
    Assert-WindowsPlatform
    Assert-Administrator

    $sourceConfig = Get-CanonicalPath -Path $ConfigPath
    Assert-RegularFile -Path $sourceConfig

    $installRoot = Get-ProgramFilesRoot
    $dataRoot = Get-ProgramDataRoot
    Assert-ExistingDirectory -Path $installRoot
    Assert-ExistingDirectory -Path $dataRoot
    Assert-ProtectedAcl -Path $installRoot -AllowUsersRead
    Assert-ProtectedAcl -Path $dataRoot

    $targetConfig = Get-ExpectedConfigPath
    Assert-NoReparsePath -Path $targetConfig -AllowMissingLeaf
    $installedExe = Get-ExpectedExecutablePath
    Assert-RegularFile -Path $installedExe
    $networkScript = Join-Path -Path $installRoot -ChildPath 'scripts\network.ps1'
    Assert-RegularFile -Path $networkScript

    $service = Read-ServiceSafe
    if ($null -eq $service) {
        Fail '서비스가 설치되어 있지 않습니다. 먼저 install.ps1을 실행하십시오.'
    }
    $null = Assert-ExpectedServicePath -ExpectedExecutablePath $installedExe

    $configObject = Read-JsonObjectSafe -Path $sourceConfig
    $requestedMode = Get-OptionalPropertyValue -Object $configObject -Name 'mode'
    if ($Enable) {
        $effectiveMode = 'enforce'
        $confirmation = 'ENABLE'
        $modeDescription = 'enforce 모드로 저장하고, 서비스 재시작 시 승인된 IPv4 설정 변경을 허용'
    }
    else {
        $effectiveMode = 'observe'
        $confirmation = 'CONFIGURE'
        $modeDescription = 'observe 모드로 저장하고 네트워크 설정은 변경하지 않음'
    }
    $configObject = Set-ConfigMode -Config $configObject -Mode $effectiveMode
    $configJson = Convert-ConfigToJson -Config $configObject

    $snapshotPath = Get-ExpectedSnapshotPath
    $newAdapterId = Get-OptionalPropertyValue -Object $configObject -Name 'adapterId'
    if (Test-Path -LiteralPath $snapshotPath -PathType Any) {
        Assert-RegularFile -Path $snapshotPath
        $snapshot = Read-JsonObjectSafe -Path $snapshotPath
        $snapshotAdapterId = Get-OptionalPropertyValue -Object $snapshot -Name 'adapterId'
        if (-not [string]::IsNullOrWhiteSpace($snapshotAdapterId) -and
            -not [string]::IsNullOrWhiteSpace($newAdapterId) -and
            $snapshotAdapterId -ne $newAdapterId) {
            Fail '기존 snapshot.json의 어댑터와 다른 adapterId는 복구 안전성 때문에 사용할 수 없습니다.'
        }
    }

    $temporaryConfig = Join-Path -Path $dataRoot -ChildPath ('.config.validate.{0}.json' -f ([System.Guid]::NewGuid().ToString('N')))
    Write-TextFileAtomic -Path $temporaryConfig -Content $configJson
    Protect-File -Path $temporaryConfig
    Assert-ExecutableValidation -ExecutablePath $installedExe -ConfigPath $temporaryConfig

    $hadConfig = Test-Path -LiteralPath $targetConfig -PathType Any
    if ($hadConfig) {
        Assert-RegularFile -Path $targetConfig
    }
    $serviceWasRunning = ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running)
    $serviceNeedsStop = ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped)
    $service.Dispose()
    $service = $null

    $preview = @(
        ('보호된 설정을 원자적으로 교체: {0}' -f $targetConfig),
        ('요청된 mode: {0}' -f $(if ([string]::IsNullOrWhiteSpace($requestedMode)) { '지정되지 않음' } else { $requestedMode })),
        $modeDescription,
        '실패하면 기존 config.json을 복원하고 서비스는 자동으로 시작하지 않음'
    )
    if ($serviceNeedsStop) {
        $preview += '현재 실행 중인 서비스는 교체 중 잠시 중지하고 성공 시 이전 실행 상태로 복원'
    }
    else {
        $preview += '현재 중지된 서비스는 그대로 두고 자동으로 시작하지 않음'
    }
    if ($Enable) {
        $preview += 'enforce 모드는 실제 IP/DNS 변경을 허용하므로 동료가 대상 어댑터와 프로필을 직접 확인해야 함'
        $preview += @(Get-ConfigReviewLines -Config $configObject)
    }
    Confirm-Plan -Lines $preview -Confirmation $confirmation -GuiConfirmed:$GuiConfirmed

    if ($serviceNeedsStop) {
        Stop-WifiService
        $serviceStopped = $true
    }

    $rollbackConfig = Join-Path -Path $dataRoot -ChildPath ('.config.rollback.{0}.json' -f ([System.Guid]::NewGuid().ToString('N')))

    if ($hadConfig) {
        [System.IO.File]::Replace((Get-CanonicalPath -Path $temporaryConfig), (Get-CanonicalPath -Path $targetConfig), (Get-CanonicalPath -Path $rollbackConfig), $true)
        Protect-File -Path $rollbackConfig
    }
    else {
        [System.IO.File]::Move((Get-CanonicalPath -Path $temporaryConfig), (Get-CanonicalPath -Path $targetConfig))
    }
    $replacementCompleted = $true
    Protect-File -Path $targetConfig
    Assert-ExecutableValidation -ExecutablePath $installedExe -ConfigPath $targetConfig

    if ($serviceStopped -and $serviceWasRunning) {
        Start-WifiService
    }

    if (Test-Path -LiteralPath $rollbackConfig -PathType Any) {
        Assert-NoReparsePath -Path $rollbackConfig
        Remove-Item -LiteralPath (Get-CanonicalPath -Path $rollbackConfig) -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $temporaryConfig -PathType Any) {
        Assert-NoReparsePath -Path $temporaryConfig
        Remove-Item -LiteralPath (Get-CanonicalPath -Path $temporaryConfig) -Force -ErrorAction Stop
    }

    if ($Enable) {
        Write-Info '설정을 저장했습니다. enforce 모드가 허용되었으며, 서비스가 실행 중이면 다음 관측부터 적용을 시도합니다.'
    }
    else {
        Write-Info '설정을 저장했습니다. observe 모드이며 네트워크 설정은 변경하지 않습니다.'
    }
}
catch {
    $errorMessage = $_.Exception.Message
    if ($replacementCompleted) {
        Restore-PreviousConfig -Target $targetConfig -Rollback $rollbackConfig -HadPrevious $hadConfig
    }
    if ($serviceStopped) {
        try {
            Stop-WifiService -IgnoreMissing | Out-Null
        }
        catch {
        }
        # 설정 실패 뒤 enforce 서비스를 조용히 다시 시작하지 않는다.
        Write-Warning '설정 변경에 실패했으므로 서비스는 시작하지 않았습니다. 필요하면 관리자가 상태를 확인한 뒤 수동으로 시작하십시오.'
    }
    if ($null -ne $temporaryConfig -and (Test-Path -LiteralPath $temporaryConfig -PathType Any)) {
        Remove-Item -LiteralPath (Get-CanonicalPath -Path $temporaryConfig) -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $rollbackConfig -and (Test-Path -LiteralPath $rollbackConfig -PathType Any)) {
        Remove-Item -LiteralPath (Get-CanonicalPath -Path $rollbackConfig) -Force -ErrorAction SilentlyContinue
    }
    Write-Error $errorMessage -ErrorAction Continue
    exit 1
}
