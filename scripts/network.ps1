# Runtime backend: stdin JSON in, a single JSON reply out. No caller-supplied code is evaluated.
[CmdletBinding()]
param([switch]$LibraryOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-LocalPath([string]$Path) {
    try { $cursor = [IO.Path]::GetFullPath($Path) }
    catch { throw 'unsafe_path' }
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            try { $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop }
            catch { throw 'unsafe_path' }
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'unsafe_path' }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Get-BackupDirectory {
    return Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'WifiProfileSwitcher'
}

function Get-BackupPath {
    return Join-Path (Get-BackupDirectory) 'snapshot.json'
}

function Assert-BackupPath([string]$Path) {
    Assert-LocalPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'backup_missing' }
    try { $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop }
    catch { throw 'backup_invalid' }
    if (-not ($item -is [IO.FileInfo]) -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'backup_invalid'
    }

    # The installed data directory is protected by install.ps1 and Safety.ProtectedPath.
    # Keep this second check in the script so a copied script cannot consume a user-writable
    # recovery point.  Only SYSTEM and Administrators may have an allow rule with write rights.
    if ([Environment]::OSVersion.Platform.ToString() -eq 'Win32NT') {
        try {
            $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
            $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
            if ($owner -notin @('S-1-5-18', 'S-1-5-32-544')) { throw 'backup_invalid' }
            $writeMask = [Security.AccessControl.FileSystemRights]::Write -bor
                [Security.AccessControl.FileSystemRights]::Modify -bor
                [Security.AccessControl.FileSystemRights]::Delete -bor
                [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
                [Security.AccessControl.FileSystemRights]::TakeOwnership
            foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
                $sid = $rule.IdentityReference.Value
                if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
                    (($rule.FileSystemRights -band $writeMask) -ne 0) -and
                    $sid -notin @('S-1-5-18', 'S-1-5-32-544')) {
                    throw 'backup_invalid'
                }
            }
        }
        catch {
            if ($_.Exception.Message -eq 'backup_invalid') { throw }
            throw 'backup_invalid'
        }
    }
}

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'administrator_required' }
}

function Test-NoInstanceError($ErrorRecord) {
    $text = [string]$ErrorRecord.Exception.Message
    return $text -match '(?i)no\s+(?:matching\s+)?(?:MSFT_\S+\s+)?objects?\s+found|no\s+instances?\s+found'
}

function Get-SelectedAdapter([guid]$Id) {
    try {
        $found = @(Get-NetAdapter -IncludeHidden -ErrorAction Stop | Where-Object { [guid]$_.InterfaceGuid -eq $Id })
    }
    catch {
        if ($_.Exception.Message -match '(?i)no .*adapter|not found|does not contain') { throw 'adapter_missing' }
        throw
    }
    if ($found.Count -ne 1) { throw 'adapter_missing' }
    $adapter = $found[0]
    if (-not $adapter.HardwareInterface -or [int]$adapter.NdisPhysicalMedium -notin @(1, 9)) { throw 'adapter_not_physical_wifi' }
    return $adapter
}

function Get-IPv4Snapshot($Adapter) {
    $index = $Adapter.ifIndex
    try { $ipif = @(Get-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop) }
    catch { if (Test-NoInstanceError $_) { $ipif = @() } else { throw } }
    if ($ipif.Count -ne 1) { throw 'complex_network_configuration' }
    try { $addresses = @(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -PolicyStore ActiveStore -ErrorAction Stop) }
    catch { if (Test-NoInstanceError $_) { $addresses = @() } else { throw } }
    try { $routes = @(Get-NetRoute -InterfaceIndex $index -AddressFamily IPv4 -PolicyStore ActiveStore -ErrorAction Stop | Where-Object { $_.DestinationPrefix -eq '0.0.0.0/0' }) }
    catch { if (Test-NoInstanceError $_) { $routes = @() } else { throw } }
    try { $dns = @(Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop) }
    catch { if (Test-NoInstanceError $_) { throw 'complex_network_configuration' } else { throw } }
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{' + ([guid]$Adapter.InterfaceGuid).ToString() + '}'
    $values = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
    $nameServer = $values.PSObject.Properties['NameServer']
    $automatic = $null -eq $nameServer -or [string]::IsNullOrWhiteSpace([string]$nameServer.Value)
    $addressReady = @($addresses | Where-Object {
        $text = [string]$_.IPAddress
        $state = [string]$_.AddressState
        $text -match '^(?:25[0-5]|2[0-4][0-9]|1?[0-9]{1,2})(?:\.(?:25[0-5]|2[0-4][0-9]|1?[0-9]{1,2})){3}$' -and
            $text -notmatch '^169\.254\.' -and $state -eq 'Preferred'
    })
    return [ordered]@{
        dhcpEnabled = [string]$ipif[0].Dhcp -eq 'Enabled'
        addresses = @($addresses | ForEach-Object { [string]$_.IPAddress })
        prefixLengths = @($addresses | ForEach-Object { [int]$_.PrefixLength })
        gateways = @($routes | ForEach-Object { [string]$_.NextHop } | Sort-Object -Unique)
        addressesReady = $addresses.Count -eq 1 -and $addressReady.Count -eq 1
        dnsAutomatic = $automatic
        dnsServers = @($dns | ForEach-Object { $_.ServerAddresses } | ForEach-Object { [string]$_ })
    }
}

function Assert-NoUnsupportedState($Adapter) {
    foreach ($store in @('PersistentStore', 'ActiveStore')) {
        try { $routes = @(Get-NetRoute -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -PolicyStore $store -ErrorAction Stop) }
        catch { if (Test-NoInstanceError $_) { $routes = @() } else { throw } }
        $custom = @($routes | Where-Object { $_.DestinationPrefix -ne '0.0.0.0/0' -and $_.NextHop -ne '0.0.0.0' })
        if ($custom.Count -gt 0 -or @($routes | Where-Object { $_.DestinationPrefix -eq '0.0.0.0/0' }).Count -gt 1) {
            throw 'complex_network_configuration'
        }
        try { $manualAddresses = @(Get-NetIPAddress -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -PolicyStore $store -ErrorAction Stop |
                Where-Object { [string]$_.PrefixOrigin -eq 'Manual' }) }
        catch { if (Test-NoInstanceError $_) { $manualAddresses = @() } else { throw } }
        if ($manualAddresses.Count -gt 1) { throw 'complex_network_configuration' }
    }
}

function Assert-SimpleConfiguration($Adapter, $Snapshot) {
    # This prototype owns a single ordinary IPv4 configuration, never multi-address or custom routed NICs.
    if ($Snapshot.addresses.Count -gt 1 -or $Snapshot.gateways.Count -gt 1) { throw 'complex_network_configuration' }
    Assert-NoUnsupportedState $Adapter
    if (-not $Snapshot.dhcpEnabled -and ($Snapshot.addresses.Count -ne 1 -or $Snapshot.gateways.Count -ne 1)) {
        throw 'complex_network_configuration'
    }
}

function Assert-RecoverableConfiguration($Adapter, $Snapshot) {
    # A failed prior apply can leave zero active addresses. DHCP recovery is precisely the
    # operation that repairs that state, so do not require a current address or gateway here.
    if ($Snapshot.addresses.Count -gt 1 -or $Snapshot.gateways.Count -gt 1) { throw 'complex_network_configuration' }
    Assert-NoUnsupportedState $Adapter
}

function Assert-IPv4([string]$Value) {
    if ($Value -notmatch '^(0|[1-9][0-9]{0,2})(\.(0|[1-9][0-9]{0,2})){3}$') { throw 'invalid_request' }
    $octets = @($Value.Split('.') | ForEach-Object { [int]$_ })
    if (@($octets | Where-Object { $_ -gt 255 }).Count -gt 0 -or $octets[0] -eq 0 -or $octets[0] -eq 127 -or $octets[0] -ge 224) { throw 'invalid_request' }
}

function Test-Field($Object, [string]$Name) {
    if ($Object -is [Collections.IDictionary]) { return $Object.Contains($Name) }
    return $null -ne $Object.PSObject.Properties[$Name]
}

function Assert-Profile($Profile, [switch]$AllowEmptyDns) {
    if ($null -eq $Profile -or -not (Test-Field $Profile 'mode') -or $null -eq $Profile.mode) { throw 'invalid_request' }
    if ($Profile.mode -eq 'dhcp') { return }
    if ($Profile.mode -ne 'static') { throw 'invalid_request' }
    foreach ($required in @('address', 'gateway', 'prefixLength', 'dnsServers')) {
        if (-not (Test-Field $Profile $required)) { throw 'invalid_request' }
    }
    Assert-IPv4 $Profile.address
    Assert-IPv4 $Profile.gateway
    if ($null -eq $Profile.prefixLength -or [int]$Profile.prefixLength -lt 1 -or [int]$Profile.prefixLength -gt 30) { throw 'invalid_request' }
    if ((-not $AllowEmptyDns -and @($Profile.dnsServers).Count -lt 1) -or @($Profile.dnsServers).Count -gt 4) { throw 'invalid_request' }
    foreach ($server in $Profile.dnsServers) { Assert-IPv4 $server }
}

function Get-IPv4DnsObject([int]$Index) {
    try { $objects = @(Get-DnsClientServerAddress -InterfaceIndex $Index -AddressFamily IPv4 -ErrorAction Stop) }
    catch { if (Test-NoInstanceError $_) { $objects = @() } else { throw } }
    if ($objects.Count -ne 1) { throw 'complex_network_configuration' }
    return $objects[0]
}

function Set-IPv4Profile($Adapter, $Profile, [Nullable[bool]]$DnsAutomatic = $null) {
    Assert-Profile $Profile -AllowEmptyDns:($null -ne $DnsAutomatic -and $Profile.mode -eq 'static')
    $index = $Adapter.ifIndex
    # Resolve the IPv4 DNS CIM object before any mutating command. A missing/ambiguous
    # object must never leave the adapter half converted.
    $dnsObject = Get-IPv4DnsObject $index
    # Remove only IPv4 manual state on the selected NIC, including persistent state from a previous static profile.
    foreach ($store in @('PersistentStore', 'ActiveStore')) {
        try { $routes = @(Get-NetRoute -InterfaceIndex $index -AddressFamily IPv4 -PolicyStore $store -ErrorAction Stop |
                Where-Object {
                    $_.DestinationPrefix -eq '0.0.0.0/0' -and
                    ($Profile.mode -eq 'static' -or [string]$_.Protocol -in @('NetMgmt', 'Static', 'Manual'))
                }) }
        catch { if (Test-NoInstanceError $_) { $routes = @() } else { throw } }
        foreach ($route in $routes) { $route | Remove-NetRoute -Confirm:$false }
        try { $addresses = @(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -PolicyStore $store -ErrorAction Stop |
                Where-Object { [string]$_.PrefixOrigin -eq 'Manual' }) }
        catch { if (Test-NoInstanceError $_) { $addresses = @() } else { throw } }
        foreach ($address in $addresses) { $address | Remove-NetIPAddress -Confirm:$false }
    }
    if ($Profile.mode -eq 'dhcp') {
        Set-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -Dhcp Enabled -ErrorAction Stop
    } else {
        Set-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -Dhcp Disabled -ErrorAction Stop
        $null = New-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -IPAddress $Profile.address -PrefixLength $Profile.prefixLength -DefaultGateway $Profile.gateway -ErrorAction Stop
    }

    # A recovery snapshot keeps the original DNS mode. Ordinary DHCP always means
    # automatic DNS; ordinary static profiles always carry an explicit DNS list.
    $useAutomaticDns = if ($null -eq $DnsAutomatic) { $Profile.mode -eq 'dhcp' } else { [bool]$DnsAutomatic }
    if ($useAutomaticDns) {
        $dnsObject | Set-DnsClientServerAddress -ResetServerAddresses -ErrorAction Stop
    } else {
        $dnsObject | Set-DnsClientServerAddress -ServerAddresses @($Profile.dnsServers) -ErrorAction Stop
    }
}

function ConvertTo-RestorePlan($Saved, [guid]$AdapterId) {
    if ($null -eq $Saved -or $null -eq $Saved.version -or [int]$Saved.version -ne 1 -or
        $null -eq $Saved.adapterId -or $null -eq $Saved.snapshot) { throw 'backup_invalid' }
    try { $savedId = [guid]$Saved.adapterId }
    catch { throw 'backup_invalid' }
    if ($savedId -ne $AdapterId) { throw 'backup_invalid' }

    $old = $Saved.snapshot
    if ($old.dhcpEnabled -isnot [bool] -or $old.dnsAutomatic -isnot [bool]) { throw 'backup_invalid' }
    $addresses = @($old.addresses)
    $prefixLengths = @($old.prefixLengths)
    $gateways = @($old.gateways)
    $dnsServers = @($old.dnsServers)
    if ($addresses.Count -ne $prefixLengths.Count -or $addresses.Count -gt 1 -or $gateways.Count -gt 1 -or $dnsServers.Count -gt 4) {
        throw 'backup_invalid'
    }
    foreach ($address in $addresses) { Assert-IPv4 $address }
    foreach ($gateway in $gateways) { Assert-IPv4 $gateway }
    foreach ($prefix in $prefixLengths) {
        if ([int]$prefix -lt 1 -or [int]$prefix -gt 30) { throw 'backup_invalid' }
    }
    foreach ($server in $dnsServers) { Assert-IPv4 $server }
    if (-not $old.dnsAutomatic -and ($dnsServers.Count -lt 1 -or $dnsServers.Count -gt 4)) { throw 'backup_invalid' }

    if ($old.dhcpEnabled) {
        # Keep the DNS list on the plan even though ordinary DHCP profiles do not
        # need one; a backup may intentionally contain DHCP + manual DNS.
        $profile = @{ mode = 'dhcp'; dnsServers = $dnsServers }
    } else {
        if ($addresses.Count -ne 1 -or $prefixLengths.Count -ne 1 -or $gateways.Count -ne 1) { throw 'backup_invalid' }
        $profile = @{ mode = 'static'; address = $addresses[0]; prefixLength = $prefixLengths[0]; gateway = $gateways[0]; dnsServers = $dnsServers }
    }
    Assert-Profile $profile -AllowEmptyDns
    return [pscustomobject]@{ Profile = $profile; DnsAutomatic = [bool]$old.dnsAutomatic }
}

function Read-BackupPlan([string]$Path, [guid]$AdapterId) {
    Assert-BackupPath $Path
    try {
        $raw = [IO.File]::ReadAllText($Path)
        if ([string]::IsNullOrWhiteSpace($raw) -or $raw.Length -gt 65536) { throw 'backup_invalid' }
        $saved = $raw | ConvertFrom-Json -ErrorAction Stop
        return ConvertTo-RestorePlan $saved $AdapterId
    }
    catch {
        if ($_.Exception.Message -eq 'backup_invalid') { throw }
        throw 'backup_invalid'
    }
}

function Write-InitialBackup([string]$Path, [guid]$AdapterId, $Snapshot) {
    $content = @{ version = 1; adapterId = $AdapterId.ToString(); snapshot = $Snapshot } | ConvertTo-Json -Depth 8
    try {
        # CreateNew prevents a concurrent apply from replacing the first recovery point.
        $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($content)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally { $stream.Dispose() }
    }
    catch [IO.IOException] {
        # A file appearing between Test-Path and CreateNew is a concurrency event.
        # Abort before touching the adapter; the next invocation can validate it.
        throw 'backup_write_failed'
    }
    catch { throw 'backup_write_failed' }
}

function Invoke-NetworkRequest($Request) {
    if ($null -eq $Request -or $Request -is [array] -or
        -not (Test-Field $Request 'operation') -or
        $Request.operation -notin @('read', 'capture', 'apply', 'recover', 'restore')) { throw 'invalid_request' }
    try { $id = [guid]$Request.adapterId }
    catch { throw 'invalid_request' }
    if ($id -eq [guid]::Empty) { throw 'invalid_request' }
    $adapter = Get-SelectedAdapter $id
    $snapshot = Get-IPv4Snapshot $adapter
    if ($Request.operation -eq 'read') { return @{ ok = $true; snapshot = $snapshot } }
    if ($Request.operation -eq 'capture') {
        # Initial import is read-only, but it must reject custom routes and
        # multi-address adapters before the snapshot leaves this boundary.
        Assert-SimpleConfiguration $adapter $snapshot
        return @{ ok = $true; snapshot = $snapshot }
    }
    Assert-Administrator
    $directory = Get-BackupDirectory
    $backup = Get-BackupPath
    Assert-LocalPath $backup
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw 'unsafe_path' }

    if ($Request.operation -eq 'restore') {
        # Parse, schema-check, ACL-check, and validate every value before touching
        # the adapter. This includes static IPv4 + automatic DNS and DHCP + manual DNS.
        $plan = Read-BackupPlan $backup $id
        Assert-RecoverableConfiguration $adapter $snapshot
        Set-IPv4Profile $adapter $plan.Profile $plan.DnsAutomatic
    } else {
        if ($Request.operation -eq 'recover') { $profile = @{ mode = 'dhcp' } }
        else { $profile = $Request.profile }
        Assert-Profile $profile
        if ($Request.operation -eq 'recover') { Assert-RecoverableConfiguration $adapter $snapshot }
        else { Assert-SimpleConfiguration $adapter $snapshot }
        if (Test-Path -LiteralPath $backup) {
            # An existing recovery point is immutable and must be valid before an apply.
            $null = Read-BackupPlan $backup $id
        } else {
            # First snapshot is never overwritten. A partially failed static apply can
            # have no address/gateway to restore; DHCP recovery may repair that state,
            # but must not write an unusable recovery point for it.
            $restorable = $Request.operation -ne 'recover' -or $snapshot.dhcpEnabled -or
                ($snapshot.addresses.Count -eq 1 -and $snapshot.prefixLengths.Count -eq 1 -and $snapshot.gateways.Count -eq 1)
            if ($restorable) { Write-InitialBackup $backup $id $snapshot }
        }
        Set-IPv4Profile $adapter $profile
    }
    return @{ ok = $true }
}

if (-not $LibraryOnly) {
    try {
        if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { throw 'powershell_language_restricted' }
        [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        $ProgressPreference = 'SilentlyContinue'
        try { Import-Module NetTCPIP, DnsClient, NetAdapter -ErrorAction Stop }
        catch { throw 'network_modules_unavailable' }
        $raw = [Console]::In.ReadToEnd()
        if ($raw.Length -gt 65536) { throw 'invalid_request' }
        $request = $raw | ConvertFrom-Json
        $result = Invoke-NetworkRequest $request
        $result | ConvertTo-Json -Depth 8 -Compress
        exit 0
    } catch {
        $safe = @('adapter_missing', 'adapter_not_physical_wifi', 'administrator_required', 'complex_network_configuration',
            'backup_invalid', 'backup_missing', 'backup_write_failed', 'unsafe_path', 'invalid_request',
            'powershell_language_restricted', 'network_modules_unavailable', 'address_conflict')
        $code = 'network_command_failed'
        if ($_.Exception.Message -in $safe) { $code = $_.Exception.Message }
        elseif ($_.Exception -is [UnauthorizedAccessException]) { $code = 'policy_or_access_denied' }
        elseif ($_.Exception.Message -match '(?i)access is denied|permission|not authorized|privilege') { $code = 'policy_or_access_denied' }
        elseif ($_.Exception.Message -match '(?i)already exists|duplicate|address conflict|object.*exists') { $code = 'address_conflict' }
        @{ ok = $false; code = $code } | ConvertTo-Json -Compress
        exit 1
    }
}
