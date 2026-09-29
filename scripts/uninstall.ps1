#requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$GuiConfirmed
)

. (Join-Path -Path $PSScriptRoot -ChildPath 'Common.ps1')

Set-StrictMode -Version 2.0

try {
    Assert-WindowsPlatform
    Assert-Administrator

    $installRoot = Get-ProgramFilesRoot
    $dataRoot = Get-ProgramDataRoot
    $installExists = Test-Path -LiteralPath $installRoot -PathType Container
    $dataExists = Test-Path -LiteralPath $dataRoot -PathType Container
    $service = Get-Service -Name $script:WifiServiceName -ErrorAction SilentlyContinue

    if (-not $installExists -and -not $dataExists -and $null -eq $service) {
        Write-Info '설치된 항목이 없습니다.'
        exit 0
    }

    if ($installExists) {
        Assert-ProtectedAcl -Path $installRoot -AllowUsersRead
        Assert-SafeTree -Path $installRoot
        Assert-RegularFile -Path (Get-ExpectedExecutablePath)
        Assert-RegularFile -Path (Join-Path -Path $installRoot -ChildPath 'scripts\network.ps1')
    }
    if ($dataExists) {
        Assert-ProtectedAcl -Path $dataRoot
        Assert-SafeTree -Path $dataRoot
    }

    $expectedExe = Get-ExpectedExecutablePath
    if ($null -ne $service) {
        $null = Assert-ExpectedServicePath -ExpectedExecutablePath $expectedExe
    }

    $preview = @(
        ('서비스 중지 및 등록 해제: {0}' -f $script:WifiServiceName),
        ('설치 파일 삭제: {0}' -f $installRoot),
        ('설정·상태·백업 삭제: {0}' -f $dataRoot),
        '현재 네트워크의 IP/DNS 설정은 변경하지 않음',
        '제거 전에 필요하면 서비스 중지 후 recover-dhcp 또는 restore를 관리자가 직접 실행해야 함',
        '제거 후에는 자동 전환 서비스가 없어짐'
    )
    Confirm-Plan -Lines $preview -Confirmation UNINSTALL -GuiConfirmed:$GuiConfirmed

    if ($null -ne $service) {
        Stop-WifiService -IgnoreMissing
        $service.Dispose()
        $service = $null
        $scPath = Join-Path -Path $env:SystemRoot -ChildPath 'System32\sc.exe'
        & $scPath 'delete' $script:WifiServiceName *> $null
        if ($LASTEXITCODE -ne 0) {
            Fail '서비스 등록 해제에 실패했습니다. 파일은 삭제하지 않았습니다.'
        }

        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while ((Test-WifiServiceExists) -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds 1
        }
        if (Test-WifiServiceExists) {
            Fail '서비스가 아직 제거되지 않았습니다. 파일은 삭제하지 않았습니다.'
        }
    }

    if ($installExists) {
        Assert-SafeTree -Path $installRoot
        Remove-Item -LiteralPath (Get-CanonicalPath -Path $installRoot) -Recurse -Force -ErrorAction Stop
    }
    if ($dataExists) {
        Assert-SafeTree -Path $dataRoot
        Remove-Item -LiteralPath (Get-CanonicalPath -Path $dataRoot) -Recurse -Force -ErrorAction Stop
    }

    Write-Info '제거를 완료했습니다. 현재 IP/DNS 설정은 그대로 남아 있습니다.'
    Write-Info '필요하면 제거 전에 실행 파일의 recover-dhcp --confirm 또는 restore --confirm을 사용하십시오.'
}
catch {
    $errorMessage = $_.Exception.Message
    Write-Error $errorMessage -ErrorAction Continue
    exit 1
}
