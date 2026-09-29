using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

internal static class TestData
{
    internal static SwitcherConfig ValidConfig(
        string mode = "enforce",
        string fallback = "dhcp",
        List<NetworkProfile>? profiles = null)
    {
        return new SwitcherConfig
        {
            Version = 1,
            AdapterId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Mode = mode,
            Fallback = fallback,
            PollSeconds = 5,
            Profiles = profiles ?? [StaticProfile()]
        };
    }

    internal static NetworkProfile StaticProfile(
        string id = "internal",
        string ssid = "Example-Internal",
        int prefixLength = 24,
        string gateway = "192.0.2.1")
    {
        return new NetworkProfile
        {
            Id = id,
            Ssids = [ssid],
            Mode = "static",
            Address = "192.0.2.10",
            PrefixLength = prefixLength,
            Gateway = gateway,
            DnsServers = ["192.0.2.53"]
        };
    }

    internal static NetworkSnapshot StaticSnapshot()
    {
        return new NetworkSnapshot
        {
            DhcpEnabled = false,
            Addresses = ["192.0.2.10"],
            PrefixLengths = [24],
            Gateways = ["192.0.2.1"],
            DnsAutomatic = false,
            DnsServers = ["192.0.2.53"]
        };
    }

    internal static NetworkSnapshot MismatchedSnapshot()
    {
        return new NetworkSnapshot
        {
            DhcpEnabled = false,
            Addresses = ["192.0.2.11"],
            PrefixLengths = [24],
            Gateways = ["192.0.2.1"],
            DnsAutomatic = false,
            DnsServers = ["192.0.2.53"]
        };
    }
}
