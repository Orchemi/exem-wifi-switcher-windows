#requires -Version 5.1

Set-StrictMode -Version 2.0

$script:WifiServiceName = 'WifiProfileSwitcher'
$script:WifiProductName = 'WifiProfileSwitcher'
$script:WifiInstallDirectoryName = 'WifiProfileSwitcher'
$script:WifiDataDirectoryName = 'WifiProfileSwitcher'

function Write-Info {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host $Message
}

function Fail {
    param([Parameter(Mandatory = $true)][string]$Message)

    throw $Message
}

function Assert-WindowsPlatform {
    $platform = [System.Environment]::OSVersion.Platform.ToString()
    if ($platform -ne 'Win32NT') {
        Fail '이 스크립트는 Windows에서만 실행할 수 있습니다. macOS에서는 구문 검사만 수행할 수 있습니다.'
    }
}

function Test-IsAdministrator {
    if ([System.Environment]::OSVersion.Platform.ToString() -ne 'Win32NT') {
        return $false
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Administrator {
    if (-not (Test-IsAdministrator)) {
        Fail '관리자 권한이 필요합니다. 관리자 PowerShell에서 다시 실행하십시오. 권한 상승을 자동으로 시도하지 않습니다.'
    }
}

function Get-ProgramFilesRoot {
    if ([string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        Fail 'Program Files 경로를 확인할 수 없습니다.'
    }

    return [System.IO.Path]::GetFullPath((Join-Path -Path $env:ProgramFiles -ChildPath $script:WifiInstallDirectoryName))
}

function Get-ProgramDataRoot {
    if ([string]::IsNullOrWhiteSpace($env:ProgramData)) {
        Fail 'ProgramData 경로를 확인할 수 없습니다.'
    }

    return [System.IO.Path]::GetFullPath((Join-Path -Path $env:ProgramData -ChildPath $script:WifiDataDirectoryName))
}

function Get-ExpectedExecutablePath {
    return Join-Path -Path (Get-ProgramFilesRoot) -ChildPath 'WifiProfileSwitcher.exe'
}

function Get-ExpectedConfigPath {
    return Join-Path -Path (Get-ProgramDataRoot) -ChildPath 'config.json'
}

function Get-ExpectedStatusPath {
    return Join-Path -Path (Get-ProgramDataRoot) -ChildPath 'status.json'
}

function Get-ExpectedSnapshotPath {
    return Join-Path -Path (Get-ProgramDataRoot) -ChildPath 'snapshot.json'
}

function Get-CanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        return [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path))
    }
    catch {
        Fail '경로를 확인할 수 없습니다.'
    }
}

function Test-ReparsePoint {
    param([Parameter(Mandatory = $true)][System.IO.FileSystemInfo]$Item)

    return (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Get-PathChain {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = Get-CanonicalPath -Path $Path
    $currentPath = $fullPath
    $existing = $null

    while (-not [string]::IsNullOrEmpty($currentPath)) {
        $existing = Get-Item -LiteralPath $currentPath -Force -ErrorAction SilentlyContinue
        if ($null -ne $existing) {
            break
        }

        $parentPath = [System.IO.Path]::GetDirectoryName($currentPath)
        if ([string]::IsNullOrEmpty($parentPath) -or $parentPath -eq $currentPath) {
            break
        }
        $currentPath = $parentPath
    }

    while ($null -ne $existing) {
        Write-Output $existing
        if ($existing -is [System.IO.FileInfo]) {
            $existing = $existing.Directory
        }
        else {
            $existing = $existing.Parent
        }
    }
}

function Assert-NoReparsePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$AllowMissingLeaf
    )

    $fullPath = Get-CanonicalPath -Path $Path
    $leafExists = Test-Path -LiteralPath $fullPath -PathType Any
    if (-not $leafExists -and -not $AllowMissingLeaf) {
        Fail '지정한 경로가 존재하지 않습니다.'
    }

    $chain = @(Get-PathChain -Path $fullPath)
    foreach ($item in $chain) {
        if (Test-ReparsePoint -Item $item) {
            Fail '재분석 지점(심볼릭 링크·정션)이 포함된 경로는 사용할 수 없습니다.'
        }
    }
}

function Assert-RegularFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-NoReparsePath -Path $Path
    $item = Get-Item -LiteralPath (Get-CanonicalPath -Path $Path) -Force -ErrorAction Stop
    if (-not ($item -is [System.IO.FileInfo])) {
        Fail '파일이 아닌 경로는 사용할 수 없습니다.'
    }
}

function Assert-ExistingDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-NoReparsePath -Path $Path
    $item = Get-Item -LiteralPath (Get-CanonicalPath -Path $Path) -Force -ErrorAction Stop
    if (-not ($item -is [System.IO.DirectoryInfo])) {
        Fail '디렉터리가 아닌 경로는 사용할 수 없습니다.'
    }
}

function Assert-NewPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = Get-CanonicalPath -Path $Path
    if (Test-Path -LiteralPath $fullPath -PathType Any) {
        Fail '기존 설치 또는 기존 대상 경로를 덮어쓰지 않습니다. 먼저 기존 설치를 제거하거나 별도 시험 PC를 사용하십시오.'
    }
    Assert-NoReparsePath -Path $fullPath -AllowMissingLeaf
}

function Get-SidValue {
    param([Parameter(Mandatory = $true)][System.Security.Principal.IdentityReference]$IdentityReference)

    try {
        $sid = $IdentityReference.Translate([System.Security.Principal.SecurityIdentifier])
        return $sid.Value
    }
    catch {
        Fail 'ACL의 계정 SID를 확인할 수 없습니다.'
    }
}

function Assert-ProtectedAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$AllowUsersRead
    )

    Assert-ExistingDirectory -Path $Path
    $acl = Get-Acl -LiteralPath (Get-CanonicalPath -Path $Path) -ErrorAction Stop
    if (-not $acl.AreAccessRulesProtected) {
        Fail '보호 디렉터리의 ACL 상속이 차단되어 있지 않습니다.'
    }

    try {
        $ownerSid = $acl.GetOwner([System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        Fail '보호 디렉터리 소유자의 SID를 확인할 수 없습니다.'
    }
    if ($ownerSid -ne 'S-1-5-18' -and $ownerSid -ne 'S-1-5-32-544') {
        Fail '보호 디렉터리 소유자가 SYSTEM 또는 Administrators가 아닙니다.'
    }

    $allowed = @('S-1-5-18', 'S-1-5-32-544')
    if ($AllowUsersRead) {
        $allowed += 'S-1-5-32-545'
    }

    foreach ($rule in $acl.Access) {
        $sid = Get-SidValue -IdentityReference $rule.IdentityReference
        if ($allowed -notcontains $sid) {
            Fail '보호 디렉터리에 허용되지 않은 ACL 항목이 있습니다.'
        }
        if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) {
            Fail '보호 디렉터리에 명시적 거부 ACL은 허용하지 않습니다.'
        }

        if ($sid -eq 'S-1-5-32-545') {
            $writeMask = [System.Security.AccessControl.FileSystemRights]::Write -bor
                [System.Security.AccessControl.FileSystemRights]::AppendData -bor
                [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor
                [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
                [System.Security.AccessControl.FileSystemRights]::Delete -bor
                [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
                [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
                [System.Security.AccessControl.FileSystemRights]::TakeOwnership
            if (([int]$rule.FileSystemRights -band [int]$writeMask) -ne 0) {
                Fail '일반 사용자에게 보호 디렉터리 쓰기 권한이 있습니다.'
            }
        }
    }
}

function Invoke-Icacls {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $icaclsPath = Join-Path -Path $env:SystemRoot -ChildPath 'System32\icacls.exe'
    if (-not (Test-Path -LiteralPath $icaclsPath -PathType Leaf)) {
        Fail 'icacls.exe를 찾을 수 없습니다.'
    }

    $allArguments = @($Path) + $Arguments
    & $icaclsPath @allArguments *> $null
    if ($LASTEXITCODE -ne 0) {
        Fail '보호 ACL 설정에 실패했습니다.'
    }
}

function Protect-Directory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$AllowUsersRead
    )

    Assert-ExistingDirectory -Path $Path
    $rules = @(
        '*S-1-5-18:(OI)(CI)(F)',
        '*S-1-5-32-544:(OI)(CI)(F)'
    )
    if ($AllowUsersRead) {
        $rules += '*S-1-5-32-545:(OI)(CI)(RX)'
    }
    Invoke-Icacls -Path (Get-CanonicalPath -Path $Path) -Arguments (@('/inheritance:r', '/setowner', '*S-1-5-32-544', '/grant:r') + $rules)
    Assert-ProtectedAcl -Path $Path -AllowUsersRead:$AllowUsersRead
}

function Protect-File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$AllowUsersRead
    )

    Assert-RegularFile -Path $Path
    $rules = @(
        '*S-1-5-18:F',
        '*S-1-5-32-544:F'
    )
    if ($AllowUsersRead) {
        $rules += '*S-1-5-32-545:RX'
    }
    Invoke-Icacls -Path (Get-CanonicalPath -Path $Path) -Arguments (@('/inheritance:r', '/setowner', '*S-1-5-32-544', '/grant:r') + $rules)
}

function Copy-SafeFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Assert-RegularFile -Path $Source
    Assert-ExistingDirectory -Path (Split-Path -Parent (Get-CanonicalPath -Path $Destination))
    Assert-NoReparsePath -Path $Destination -AllowMissingLeaf
    Copy-Item -LiteralPath (Get-CanonicalPath -Path $Source) -Destination (Get-CanonicalPath -Path $Destination) -Force:$false -ErrorAction Stop
    Assert-RegularFile -Path $Destination
}

function Read-JsonObjectSafe {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-RegularFile -Path $Path
    try {
        $raw = [System.IO.File]::ReadAllText((Get-CanonicalPath -Path $Path))
        if ([string]::IsNullOrWhiteSpace($raw)) {
            Fail '설정 파일이 비어 있습니다.'
        }
        return ($raw | ConvertFrom-Json -ErrorAction Stop)
    }
    catch {
        Fail '설정 파일의 JSON을 읽을 수 없습니다.'
    }
}

function Set-ConfigMode {
    param(
        [Parameter(Mandatory = $true)][object]$Config,
        [Parameter(Mandatory = $true)][ValidateSet('observe', 'enforce')][string]$Mode
    )

    $property = $Config.PSObject.Properties['mode']
    if ($null -eq $property) {
        $Config | Add-Member -MemberType NoteProperty -Name mode -Value $Mode
    }
    else {
        $Config.mode = $Mode
    }
    return $Config
}

function Convert-ConfigToJson {
    param([Parameter(Mandatory = $true)][object]$Config)

    try {
        return ($Config | ConvertTo-Json -Depth 20 -ErrorAction Stop)
    }
    catch {
        Fail '설정을 JSON으로 만들 수 없습니다.'
    }
}

function Write-TextFileAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $fullPath = Get-CanonicalPath -Path $Path
    $parent = Split-Path -Parent $fullPath
    Assert-ExistingDirectory -Path $parent
    Assert-NoReparsePath -Path $fullPath -AllowMissingLeaf

    $tempName = '.{0}.{1}.tmp' -f ([System.IO.Path]::GetFileName($fullPath)), ([System.Guid]::NewGuid().ToString('N'))
    $tempPath = Join-Path -Path $parent -ChildPath $tempName
    try {
        $utf8 = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
        [System.IO.File]::WriteAllText($tempPath, $Content, $utf8)
        Assert-RegularFile -Path $tempPath

        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            [System.IO.File]::Replace($tempPath, $fullPath, $null, $true)
        }
        else {
            [System.IO.File]::Move($tempPath, $fullPath)
        }
    }
    catch {
        if (Test-Path -LiteralPath $tempPath -PathType Any) {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        }
        Fail '설정 파일을 원자적으로 저장하지 못했습니다.'
    }
}

function Read-ServiceSafe {
    try {
        return Get-Service -Name $script:WifiServiceName -ErrorAction Stop
    }
    catch {
        return $null
    }
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory = $true)][System.ServiceProcess.ServiceController]$Service,
        [Parameter(Mandatory = $true)][ValidateSet('Running', 'Stopped')][string]$State,
        [int]$TimeoutSeconds = 30
    )

    try {
        $Service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::$State, [TimeSpan]::FromSeconds($TimeoutSeconds))
    }
    catch {
        Fail '서비스 상태 변경이 제한 시간 안에 끝나지 않았습니다.'
    }
}

function Stop-WifiService {
    param([switch]$IgnoreMissing)

    $service = Read-ServiceSafe
    if ($null -eq $service) {
        if ($IgnoreMissing) {
            return $false
        }
        Fail '서비스가 설치되어 있지 않습니다.'
    }

    try {
        if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -InputObject $service -Force -ErrorAction Stop
            Wait-ServiceState -Service $service -State Stopped
        }
    }
    finally {
        $service.Dispose()
    }
    return $true
}

function Start-WifiService {
    $service = Read-ServiceSafe
    if ($null -eq $service) {
        Fail '서비스가 설치되어 있지 않습니다.'
    }
    try {
        Start-Service -InputObject $service -ErrorAction Stop
        Wait-ServiceState -Service $service -State Running
    }
    finally {
        $service.Dispose()
    }
}

function Test-WifiServiceExists {
    $service = Read-ServiceSafe
    if ($null -eq $service) {
        return $false
    }
    $service.Dispose()
    return $true
}

function Invoke-WifiExecutable {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Assert-RegularFile -Path $ExecutablePath
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = Get-CanonicalPath -Path $ExecutablePath
    $quoted = @()
    foreach ($argument in $Arguments) {
        if ($argument -match '[\"\r\n]') {
            Fail '실행 인자에 허용하지 않는 문자가 있습니다.'
        }
        $quoted += '"{0}"' -f $argument
    }
    $startInfo.Arguments = ($quoted -join ' ')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            Fail '검증 프로그램을 시작하지 못했습니다.'
        }
        $null = $process.StandardOutput.ReadToEnd()
        $null = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        return $process.ExitCode
    }
    catch {
        Fail '검증 프로그램 실행에 실패했습니다.'
    }
    finally {
        $process.Dispose()
    }
}

function Assert-ExecutableValidation {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$ConfigPath
    )

    $exitCode = Invoke-WifiExecutable -ExecutablePath $ExecutablePath -Arguments @('validate', '--config', (Get-CanonicalPath -Path $ConfigPath))
    if ($exitCode -ne 0) {
        Fail '설정 검증에 실패했습니다. 실제 값은 출력하지 않고 작업을 중단합니다.'
    }
}

function Confirm-Plan {
    param(
        [Parameter(Mandatory = $true)][string[]]$Lines,
        [Parameter(Mandatory = $true)][ValidateSet('INSTALL', 'CONFIGURE', 'ENABLE', 'UNINSTALL')][string]$Confirmation
    )

    Write-Host ''
    Write-Host '변경 예정:'
    foreach ($line in $Lines) {
        Write-Host ('  - ' + $line)
    }
    Write-Host ''
    $answer = Read-Host (('계속하려면 {0} 을(를) 그대로 입력하십시오' -f $Confirmation))
    if ($answer -cne $Confirmation) {
        Fail '확인이 일치하지 않아 변경하지 않았습니다.'
    }
}

function Get-ServiceExecutablePath {
    param([Parameter(Mandatory = $true)][object]$ServiceInfo)

    $pathName = [string]$ServiceInfo.PathName
    $match = [System.Text.RegularExpressions.Regex]::Match($pathName, '^\s*"([^"]+)"')
    if (-not $match.Success) {
        Fail '서비스 실행 경로가 예상한 형식이 아닙니다. 안전을 위해 제거하지 않습니다.'
    }
    return Get-CanonicalPath -Path $match.Groups[1].Value
}

function Assert-ExpectedServicePath {
    param([Parameter(Mandatory = $true)][string]$ExpectedExecutablePath)

    $serviceInfo = Get-CimInstance -ClassName Win32_Service -Filter ("Name='{0}'" -f $script:WifiServiceName) -ErrorAction SilentlyContinue
    if ($null -eq $serviceInfo) {
        $serviceController = Get-Service -Name $script:WifiServiceName -ErrorAction SilentlyContinue
        if ($null -eq $serviceController) {
            return $null
        }
        Fail '서비스 실행 경로를 확인할 수 없습니다. 안전을 위해 건드리지 않습니다.'
    }
    $actual = Get-ServiceExecutablePath -ServiceInfo $serviceInfo
    if ($actual -ne (Get-CanonicalPath -Path $ExpectedExecutablePath)) {
        Fail '동일한 이름의 서비스가 다른 실행 파일을 가리킵니다. 안전을 위해 건드리지 않습니다.'
    }
    return $serviceInfo
}

function Assert-SafeTree {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-ExistingDirectory -Path $Path
    $items = @(Get-ChildItem -LiteralPath (Get-CanonicalPath -Path $Path) -Force -Recurse -ErrorAction Stop)
    foreach ($item in $items) {
        if (Test-ReparsePoint -Item $item) {
            Fail '제거 대상에 재분석 지점이 포함되어 있어 중단합니다.'
        }
    }
}
