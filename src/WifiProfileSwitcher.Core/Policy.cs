namespace WifiProfileSwitcher.Core;

/// <summary>
/// Resolves a connected SSID and compares an adapter snapshot with a target profile.
/// </summary>
public static class Policy
{
    public static NetworkProfile? TargetFor(SwitcherConfig config, string? ssid)
    {
        ArgumentNullException.ThrowIfNull(config);

        // A missing SSID is an unknown connection state, not an outside network.
        // Applying DHCP while disconnected can race a reconnect on another adapter.
        if (string.IsNullOrEmpty(ssid))
        {
            return null;
        }

        var profiles = config.Profiles ?? [];
        var matching = profiles.FirstOrDefault(profile =>
            profile is not null && profile.Ssids is not null &&
            profile.Ssids.Contains(ssid, StringComparer.Ordinal));
        if (matching is not null)
        {
            return matching;
        }

        return config.Fallback == "dhcp"
            ? new NetworkProfile { Id = "fallback-dhcp", Mode = "dhcp", Ssids = [], DnsServers = [] }
            : null;
    }

    public static bool Matches(NetworkProfile profile, NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.AddressesReady || snapshot.Addresses is null || snapshot.PrefixLengths is null ||
            snapshot.Gateways is null || snapshot.DnsServers is null)
        {
            return false;
        }

        if (profile.Mode == "dhcp")
        {
            // DHCP being enabled is not enough: Windows can report it before a usable
            // lease arrives, and a 169.254/16 address means the lease failed.
            return snapshot.DhcpEnabled && snapshot.DnsAutomatic &&
                   snapshot.Addresses.Any(address => ConfigValidator.TryUsableIpv4(address, out _));
        }

        if (profile.Mode != "static" || profile.Address is null || profile.PrefixLength is null ||
            profile.Gateway is null || profile.DnsServers is null)
        {
            return false;
        }

        if (snapshot.DhcpEnabled || snapshot.DnsAutomatic)
        {
            return false;
        }

        return SequenceEqual(snapshot.Addresses, [profile.Address]) &&
               snapshot.PrefixLengths.SequenceEqual([profile.PrefixLength.Value]) &&
               SequenceEqual(snapshot.Gateways, [profile.Gateway]) &&
               SequenceEqual(snapshot.DnsServers, profile.DnsServers);
    }

    private static bool SequenceEqual(IEnumerable<string>? first, IEnumerable<string>? second)
    {
        if (first is null || second is null)
        {
            return false;
        }

        return first.SequenceEqual(second, StringComparer.Ordinal);
    }
}
