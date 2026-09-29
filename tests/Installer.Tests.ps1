#requires -Version 5.1

[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

foreach ($name in @('Common.ps1', 'install.ps1', 'configure.ps1', 'uninstall.ps1')) {
    $path = Join-Path $root ('scripts\' + $name)
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    Assert-Condition -Condition ($errors.Count -eq 0) -Message ('PowerShell 구문 오류: ' + $name)
}

. (Join-Path $root 'scripts\Common.ps1')
$probeRoot = Join-Path $root ('.installer-probe-' + [System.Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $probeRoot -Force:$false | Out-Null
    $probeFile = Join-Path $probeRoot 'probe.json'
    [System.IO.File]::WriteAllText($probeFile, '{}')
    Push-Location $probeRoot
    try {
        Assert-Condition -Condition ((Get-CanonicalPath -Path 'probe.json') -eq $probeFile) -Message '상대 경로가 PowerShell 작업 위치를 따르지 않습니다.'
    } finally { Pop-Location }
    Assert-RegularFile -Path $probeFile
    Assert-NoReparsePath -Path $probeFile

    # 기존 설치는 정리 작업보다 먼저 거부되어야 한다.
    $marker = Join-Path $probeRoot 'keep.txt'
    [System.IO.File]::WriteAllText($marker, 'keep')
    $rejected = $false
    try {
        Assert-NewPath -Path $probeRoot
    }
    catch {
        $rejected = $true
    }
    Assert-Condition -Condition $rejected -Message '기존 경로 거부가 동작하지 않았습니다.'
    Assert-Condition -Condition (Test-Path -LiteralPath $marker -PathType Leaf) -Message '기존 경로 거부 중 파일이 삭제되었습니다.'

    $observe = '{"mode":"enforce"}' | ConvertFrom-Json
    $observe = Set-ConfigMode -Config $observe -Mode observe
    Assert-Condition -Condition ($observe.mode -eq 'observe') -Message '관찰 모드 강제가 동작하지 않습니다.'

    $atomic = Join-Path $probeRoot 'atomic.json'
    Write-TextFileAtomic -Path $atomic -Content '{"ok":true}'
    Assert-Condition -Condition (([IO.File]::ReadAllText($atomic)) -eq '{"ok":true}') -Message '원자적 설정 저장 결과가 다릅니다.'

    Write-Output 'PASS: 설치 스크립트 정적·경로 안전 검사'
}
finally {
    if (Test-Path -LiteralPath $probeRoot -PathType Container) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
