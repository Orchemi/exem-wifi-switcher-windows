#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Stop')]
    [string]$Action,
    [switch]$GuiConfirmed
)

. (Join-Path -Path $PSScriptRoot -ChildPath 'Common.ps1')

Set-StrictMode -Version 2.0

try {
    # This script is intentionally limited to the service installed by this
    # product. It never accepts a service name or executable path from a GUI.
    Assert-WindowsPlatform
    Assert-Administrator

    $installRoot = Get-ProgramFilesRoot
    $dataRoot = Get-ProgramDataRoot
    Assert-ExistingDirectory -Path $installRoot
    Assert-ExistingDirectory -Path $dataRoot
    Assert-ProtectedAcl -Path $installRoot -AllowUsersRead
    Assert-ProtectedAcl -Path $dataRoot

    $installedExe = Get-ExpectedExecutablePath
    Assert-RegularFile -Path $installedExe
    $serviceInfo = Assert-ExpectedServicePath -ExpectedExecutablePath $installedExe
    if ($null -eq $serviceInfo) {
        Fail '서비스가 설치되어 있지 않습니다.'
    }

    $configPath = Get-ExpectedConfigPath
    Assert-RegularFile -Path $configPath
    if ($Action -eq 'Start') {
        # Validate the protected config before starting a service that may
        # change IPv4 or DNS. The executable emits only stable result codes.
        Assert-ExecutableValidation -ExecutablePath $installedExe -ConfigPath $configPath
    }

    $service = Read-ServiceSafe
    if ($null -eq $service) {
        Fail '서비스를 확인할 수 없습니다.'
    }
    try {
        $isRunning = $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped
    }
    finally {
        $service.Dispose()
    }

    if ($Action -eq 'Start') {
        $preview = @(
            'WifiProfileSwitcher 서비스를 시작',
            '보호된 config.json을 사용하여 현재 Wi-Fi를 관찰',
            'enforce 설정이면 일치하는 SSID에서 IPv4/DNS 변경을 시도할 수 있음',
            '서비스 실행 중에는 중복 실행을 허용하지 않음'
        )
        Confirm-Plan -Lines $preview -Confirmation START -GuiConfirmed:$GuiConfirmed
        if (-not $isRunning) {
            Start-WifiService
        }
        Write-Info '서비스가 실행 중입니다.'
    }
    else {
        $preview = @(
            'WifiProfileSwitcher 서비스를 중지',
            '서비스가 중지되면 SSID 감지와 자동 IPv4/DNS 전환을 멈춤',
            '현재 네트워크 설정은 그대로 둠'
        )
        Confirm-Plan -Lines $preview -Confirmation STOP -GuiConfirmed:$GuiConfirmed
        if ($isRunning) {
            Stop-WifiService
        }
        Write-Info '서비스가 중지되어 있습니다.'
    }

    exit 0
}
catch {
    # Do not surface PowerShell exception text to the GUI. The host receives
    # the exit code and can show its own stable, localized error message.
    Write-Error '서비스 상태를 변경하지 못했습니다.' -ErrorAction Continue
    exit 1
}
