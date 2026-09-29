#!/usr/bin/env pwsh
# Mock-only tests for scripts/network.ps1. No NetTCPIP/DnsClient cmdlet is ever
# allowed to reach a real adapter; the test functions below record every call.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts/network.ps1') -LibraryOnly

$script:AdapterId = [guid]'11111111-1111-4111-8111-111111111111'
$script:Adapter = [pscustomobject]@{
    InterfaceGuid = $script:AdapterId
    ifIndex = 42
    HardwareInterface = $true
    NdisPhysicalMedium = 9
}
$script:Calls = [System.Collections.Generic.List[object]]::new()
$script:Addresses = @{}
$script:Routes = @{}
$script:IpInterface = $null
$script:DnsObject = $null
$script:DnsNameServer = ''
$script:NoInstanceStores = @()
$testTempRoot = if ([Environment]::OSVersion.Platform.ToString() -eq 'Win32NT') {
    [IO.Path]::GetTempPath()
} else {
    # macOS /tmp is a symlink to /private/tmp; the production path guard rejects
    # that correctly, so use the canonical temporary directory for this mock.
    '/private/tmp'
}
$script:BackupDirectory = Join-Path $testTempRoot ('WifiProfileSwitcherTests-' + [guid]::NewGuid().ToString('N'))

function Add-Call {
    param([string]$Name, [hashtable]$Arguments = @{})
    $script:Calls.Add([pscustomobject]@{ Name = $Name; Arguments = $Arguments })
}

function Reset-MockState {
    $script:Calls.Clear()
    $script:Addresses = @{
        PersistentStore = @()
        ActiveStore = @()
    }
    $script:Routes = @{
        PersistentStore = @()
        ActiveStore = @()
    }
    $script:IpInterface = [pscustomobject]@{ Dhcp = 'Enabled' }
    $script:DnsObject = [pscustomobject]@{ InterfaceIndex = 42; AddressFamily = 'IPv4'; ServerAddresses = @('198.51.100.53') }
    $script:DnsNameServer = ''
    $script:NoInstanceStores = @()
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "assertion_failed: $Message" }
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -ne $Expected) { throw "assertion_failed: $Message (actual=$Actual expected=$Expected)" }
}

function Assert-Code {
    param([scriptblock]$Action, [string]$Expected)
    try {
        & $Action
        throw "assertion_failed: expected $Expected"
    }
    catch {
        Assert-Equal $_.Exception.Message $Expected "expected error code $Expected"
    }
}

function Get-NetAdapter {
    [CmdletBinding()] param([switch]$IncludeHidden)
    Add-Call 'Get-NetAdapter' @{ IncludeHidden = $IncludeHidden }
    return $script:Adapter
}

function Get-NetIPInterface {
    [CmdletBinding()] param([int]$InterfaceIndex, [string]$AddressFamily)
    Add-Call 'Get-NetIPInterface' @{ InterfaceIndex = $InterfaceIndex; AddressFamily = $AddressFamily }
    return $script:IpInterface
}

function Get-NetIPAddress {
    [CmdletBinding()] param([int]$InterfaceIndex, [string]$AddressFamily, [string]$PolicyStore)
    Add-Call 'Get-NetIPAddress' @{ InterfaceIndex = $InterfaceIndex; AddressFamily = $AddressFamily; PolicyStore = $PolicyStore }
    if ($script:NoInstanceStores -contains $PolicyStore) { throw 'No MSFT_NetIPAddress objects found' }
    return @($script:Addresses[$PolicyStore])
}

function Get-NetRoute {
    [CmdletBinding()] param([int]$InterfaceIndex, [string]$AddressFamily, [string]$PolicyStore)
    Add-Call 'Get-NetRoute' @{ InterfaceIndex = $InterfaceIndex; AddressFamily = $AddressFamily; PolicyStore = $PolicyStore }
    if ($script:NoInstanceStores -contains $PolicyStore) { throw 'No MSFT_NetRoute objects found' }
    return @($script:Routes[$PolicyStore])
}

function Get-DnsClientServerAddress {
    [CmdletBinding()] param([int]$InterfaceIndex, [string]$AddressFamily)
    Add-Call 'Get-DnsClientServerAddress' @{ InterfaceIndex = $InterfaceIndex; AddressFamily = $AddressFamily }
    return @($script:DnsObject)
}

function Get-ItemProperty {
    [CmdletBinding()] param([string]$LiteralPath)
    Add-Call 'Get-ItemProperty' @{ LiteralPath = $LiteralPath }
    return [pscustomobject]@{ NameServer = $script:DnsNameServer }
}

function Remove-NetRoute {
    [CmdletBinding()] param([Parameter(ValueFromPipeline = $true)]$InputObject, [switch]$Confirm)
    process { Add-Call 'Remove-NetRoute' @{ InputObject = $InputObject; Confirm = $Confirm } }
}

function Remove-NetIPAddress {
    [CmdletBinding()] param([Parameter(ValueFromPipeline = $true)]$InputObject, [switch]$Confirm)
    process { Add-Call 'Remove-NetIPAddress' @{ InputObject = $InputObject; Confirm = $Confirm } }
}

function Set-NetIPInterface {
    [CmdletBinding()] param([int]$InterfaceIndex, [string]$AddressFamily, [string]$Dhcp)
    Add-Call 'Set-NetIPInterface' @{ InterfaceIndex = $InterfaceIndex; AddressFamily = $AddressFamily; Dhcp = $Dhcp }
}

function New-NetIPAddress {
    [CmdletBinding()] param([int]$InterfaceIndex, [string]$AddressFamily, [string]$IPAddress, [int]$PrefixLength, [string]$DefaultGateway)
    Add-Call 'New-NetIPAddress' @{ InterfaceIndex = $InterfaceIndex; AddressFamily = $AddressFamily; IPAddress = $IPAddress; PrefixLength = $PrefixLength; DefaultGateway = $DefaultGateway }
}

function Set-DnsClientServerAddress {
    [CmdletBinding()] param(
        [Parameter(ValueFromPipeline = $true)]$InputObject,
        [string[]]$ServerAddresses,
        [switch]$ResetServerAddresses
    )
    process { Add-Call 'Set-DnsClientServerAddress' @{ InputObject = $InputObject; ServerAddresses = $ServerAddresses; Reset = $ResetServerAddresses } }
}

# Test hooks are scoped to this test process only. The production script still
# resolves ProgramData and performs its own administrator check.
function Assert-Administrator { }
function Get-BackupDirectory { return $script:BackupDirectory }
function Get-BackupPath { return (Join-Path $script:BackupDirectory 'snapshot.json') }
function Assert-BackupPath([string]$Path) {
    # The production ACL check requires SYSTEM/Administrators ownership. A CI
    # temp file is owned by the runner account, so tests retain the path/reparse
    # checks and exercise ACL policy through the production review separately.
    Assert-LocalPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'backup_missing' }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not ($item -is [IO.FileInfo]) -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'backup_invalid' }
}

function Get-CallCount([string]$Name) {
    return @($script:Calls | Where-Object Name -eq $Name).Count
}

function Get-Calls([string]$Name) {
    return @($script:Calls | Where-Object Name -eq $Name)
}

function Invoke-Tests {
    New-Item -ItemType Directory -Path $script:BackupDirectory -Force | Out-Null

    # Snapshot must report a usable IPv4 address only when it is preferred and not APIPA.
    Reset-MockState
    $script:Addresses.ActiveStore = @([pscustomobject]@{ IPAddress = '192.0.2.10'; PrefixLength = 24; PrefixOrigin = 'Manual'; AddressState = 'Preferred' })
    $snap = Get-IPv4Snapshot $script:Adapter
    Assert-True $snap.addressesReady 'preferred documentation address is ready'
    Assert-True (@($script:Calls | Where-Object {
        $property = $_.Arguments.PSObject.Properties['AddressFamily']
        $null -ne $property -and $property.Value -and $property.Value -ne 'IPv4'
    }).Count -eq 0) 'snapshot must never query IPv6'
    $script:Addresses.ActiveStore = @([pscustomobject]@{ IPAddress = '169.254.10.20'; PrefixLength = 16; PrefixOrigin = 'WellKnown'; AddressState = 'Preferred' })
    Assert-True (-not (Get-IPv4Snapshot $script:Adapter).addressesReady) 'APIPA must not be considered ready'
    $script:Addresses.ActiveStore = @([pscustomobject]@{ IPAddress = '192.0.2.10'; PrefixLength = 24; PrefixOrigin = 'Manual'; AddressState = 'Tentative' })
    Assert-True (-not (Get-IPv4Snapshot $script:Adapter).addressesReady) 'tentative address must not be considered ready'

    # Static conversion removes only IPv4 manual addresses and default routes from both stores.
    Reset-MockState
    $script:Addresses.PersistentStore = @([pscustomobject]@{ PrefixOrigin = 'Manual'; IPAddress = '192.0.2.20' })
    $script:Addresses.ActiveStore = @([pscustomobject]@{ PrefixOrigin = 'Manual'; IPAddress = '192.0.2.21' })
    $script:Routes.PersistentStore = @([pscustomobject]@{ DestinationPrefix = '0.0.0.0/0'; Protocol = 'Static' })
    $script:Routes.ActiveStore = @([pscustomobject]@{ DestinationPrefix = '0.0.0.0/0'; Protocol = 'Dhcp' })
    Set-IPv4Profile $script:Adapter @{ mode = 'static'; address = '192.0.2.30'; prefixLength = 24; gateway = '192.0.2.1'; dnsServers = @('198.51.100.53') }
    Assert-Equal (Get-CallCount 'Remove-NetRoute') 2 'remove default routes in both policy stores'
    Assert-Equal (Get-CallCount 'Remove-NetIPAddress') 2 'remove manual addresses in both policy stores'
    Assert-Equal (Get-CallCount 'New-NetIPAddress') 1 'create one IPv4 address'
    $ipCall = @(Get-Calls 'Set-NetIPInterface')[0]
    Assert-Equal $ipCall.Arguments.AddressFamily 'IPv4' 'static IP operation is IPv4-only'
    $dnsCall = @(Get-Calls 'Set-DnsClientServerAddress')[0]
    Assert-Equal $dnsCall.Arguments.ServerAddresses[0] '198.51.100.53' 'static DNS is applied explicitly'

    # DHCP conversion must reset only IPv4 DNS and must not invoke global ipconfig or CIM renewal.
    Reset-MockState
    $script:Addresses.PersistentStore = @([pscustomobject]@{ PrefixOrigin = 'Manual'; IPAddress = '192.0.2.20' })
    $script:Routes.ActiveStore = @([pscustomobject]@{ DestinationPrefix = '0.0.0.0/0'; Protocol = 'Dhcp' })
    Set-IPv4Profile $script:Adapter @{ mode = 'dhcp' }
    Assert-Equal (Get-CallCount 'Set-NetIPInterface') 1 'enable DHCP once'
    Assert-Equal (Get-CallCount 'Set-DnsClientServerAddress') 1 'reset DNS once'
    Assert-Equal (Get-CallCount 'Remove-NetRoute') 0 'DHCP route must survive DHCP/DNS-only apply'
    Assert-True ((Get-CallCount 'New-NetIPAddress') -eq 0) 'DHCP must not add a static address'
    Assert-True ((Get-CallCount 'Get-CimInstance') -eq 0 -and (Get-CallCount 'Invoke-CimMethod') -eq 0) 'DHCP must not renew through an adapter-wide CIM call'

    # Cmdletization reports an empty policy store as a terminating "no objects"
    # error on some Windows builds; that benign result must still be treated as empty.
    Reset-MockState
    $script:NoInstanceStores = @('PersistentStore')
    Set-IPv4Profile $script:Adapter @{ mode = 'dhcp' }
    Assert-Equal (Get-CallCount 'Set-NetIPInterface') 1 'empty policy stores are handled without a false failure'

    # Missing/ambiguous DNS state is rejected before any network mutation.
    Reset-MockState
    $script:DnsObject = @()
    Assert-Code { Set-IPv4Profile $script:Adapter @{ mode = 'dhcp' } } 'complex_network_configuration'
    Assert-True ((Get-CallCount 'Remove-NetRoute') -eq 0 -and (Get-CallCount 'Set-NetIPInterface') -eq 0) 'DNS preflight must precede mutations'

    # Recovery is allowed to repair a partial apply with no active address or gateway.
    Reset-MockState
    $script:IpInterface = [pscustomobject]@{ Dhcp = 'Disabled' }
    $script:Addresses.ActiveStore = @()
    $script:Routes.ActiveStore = @()
    Assert-RecoverableConfiguration $script:Adapter (Get-IPv4Snapshot $script:Adapter)
    Set-IPv4Profile $script:Adapter @{ mode = 'dhcp' }
    Assert-Equal (Get-CallCount 'Set-NetIPInterface') 1 'partial state can be repaired with DHCP'

    # Custom routes remain outside this prototype and block even DHCP recovery.
    Reset-MockState
    $script:Routes.ActiveStore = @([pscustomobject]@{ DestinationPrefix = '198.51.100.0/24'; NextHop = '192.0.2.1' })
    Assert-Code { Assert-RecoverableConfiguration $script:Adapter (Get-IPv4Snapshot $script:Adapter) } 'complex_network_configuration'

    # Invalid request profiles must fail before creating a recovery file or changing a NIC.
    Reset-MockState
    $backupPath = Get-BackupPath
    if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Force }
    $invalidProfile = @{ mode = 'static'; address = '192.0.2.30'; prefixLength = 24; gateway = '192.0.2.1'; dnsServers = @() }
    Assert-Code { Invoke-NetworkRequest @{ operation = 'apply'; adapterId = $script:AdapterId.ToString(); profile = $invalidProfile } } 'invalid_request'
    Assert-True (-not (Test-Path -LiteralPath $backupPath)) 'invalid profile must not create a backup'
    Assert-Equal (Get-CallCount 'Set-NetIPInterface') 0 'invalid profile must not mutate IP'

    # A first-time recovery from an incomplete static state is allowed, but it must
    # not save an impossible-to-restore snapshot.
    Reset-MockState
    $script:IpInterface = [pscustomobject]@{ Dhcp = 'Disabled' }
    $script:Addresses.ActiveStore = @()
    $script:Routes.ActiveStore = @()
    Assert-True (-not (Test-Path -LiteralPath $backupPath)) 'partial recovery starts without a backup'
    $result = Invoke-NetworkRequest @{ operation = 'recover'; adapterId = $script:AdapterId.ToString() }
    Assert-True $result.ok 'partial recovery succeeds'
    Assert-True (-not (Test-Path -LiteralPath $backupPath)) 'partial recovery must not save an un-restorable snapshot'

    # A malformed existing backup is rejected before any apply mutation.
    Reset-MockState
    [IO.File]::WriteAllText($backupPath, '{"version":1,"adapterId":"not-a-guid"}')
    $validProfile = @{ mode = 'static'; address = '192.0.2.30'; prefixLength = 24; gateway = '192.0.2.1'; dnsServers = @('198.51.100.53') }
    Assert-Code { Invoke-NetworkRequest @{ operation = 'apply'; adapterId = $script:AdapterId.ToString(); profile = $validProfile } } 'backup_invalid'
    Assert-Equal (Get-CallCount 'Set-NetIPInterface') 0 'bad backup must block IP mutation'
    Remove-Item -LiteralPath $backupPath -Force

    # Restore validates DNS before touching IPv4. Static + automatic DNS is valid.
    Reset-MockState
    $staticAuto = @{ version = 1; adapterId = $script:AdapterId.ToString(); snapshot = @{
        dhcpEnabled = $false; addresses = @('192.0.2.40'); prefixLengths = @(24); gateways = @('192.0.2.1');
        dnsAutomatic = $true; dnsServers = @()
    } }
    $badDns = @{ version = 1; adapterId = $script:AdapterId.ToString(); snapshot = @{
        dhcpEnabled = $false; addresses = @('192.0.2.40'); prefixLengths = @(24); gateways = @('192.0.2.1');
        dnsAutomatic = $false; dnsServers = @()
    } }
    $badDns | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $backupPath -Encoding utf8
    Assert-Code { Invoke-NetworkRequest @{ operation = 'restore'; adapterId = $script:AdapterId.ToString() } } 'backup_invalid'
    Assert-Equal (Get-CallCount 'Set-NetIPInterface') 0 'invalid DNS backup must not mutate IPv4'
    $staticAuto | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $backupPath -Encoding utf8
    $result = Invoke-NetworkRequest @{ operation = 'restore'; adapterId = $script:AdapterId.ToString() }
    Assert-True $result.ok 'valid static + automatic DNS backup restores successfully'
    Assert-Equal (Get-CallCount 'New-NetIPAddress') 1 'static restore applies the saved address'
    Assert-Equal (Get-CallCount 'Set-DnsClientServerAddress') 1 'static automatic DNS restores by reset'

    # DHCP + manual DNS is also a valid original state and is restored as such.
    Reset-MockState
    $dhcpManual = @{ version = 1; adapterId = $script:AdapterId.ToString(); snapshot = @{
        dhcpEnabled = $true; addresses = @(); prefixLengths = @(); gateways = @();
        dnsAutomatic = $false; dnsServers = @('198.51.100.53')
    } }
    $dhcpManual | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $backupPath -Encoding utf8
    $result = Invoke-NetworkRequest @{ operation = 'restore'; adapterId = $script:AdapterId.ToString() }
    Assert-True $result.ok 'valid DHCP + manual DNS backup restores successfully'
    $dnsCall = @(Get-Calls 'Set-DnsClientServerAddress')[0]
    Assert-Equal $dnsCall.Arguments.ServerAddresses[0] '198.51.100.53' 'manual DNS is preserved on DHCP restore'
}

try {
    Invoke-Tests
    Write-Output 'NetworkBackend.Tests.ps1: PASS'
}
finally {
    if (Test-Path -LiteralPath $script:BackupDirectory) {
        Remove-Item -LiteralPath $script:BackupDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
