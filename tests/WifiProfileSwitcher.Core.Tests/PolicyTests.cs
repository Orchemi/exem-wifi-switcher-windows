using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class PolicyTests
{
    [Fact]
    public void TargetForUsesExactSsidAndDhcpFallback()
    {
        var config = TestData.ValidConfig(profiles:
        [
            TestData.StaticProfile()
        ]);

        var internalTarget = Policy.TargetFor(config, "Example-Internal");
        var outsideTarget = Policy.TargetFor(config, "Example-Guest");
        var noConnectionTarget = Policy.TargetFor(config, null);

        Assert.Equal("internal", internalTarget?.Id);
        Assert.Equal("fallback-dhcp", outsideTarget?.Id);
        Assert.Null(noConnectionTarget);
    }

    [Fact]
    public void TargetForCanHoldWhenFallbackIsHold()
    {
        var config = TestData.ValidConfig(fallback: "hold");

        Assert.Null(Policy.TargetFor(config, "Example-Guest"));
    }

    [Fact]
    public void TargetForDoesNotTreatSsidCaseAsEqual()
    {
        var config = TestData.ValidConfig();

        var target = Policy.TargetFor(config, "example-internal");

        Assert.Equal("fallback-dhcp", target?.Id);
    }

    [Fact]
    public void MatchesStaticRequiresAllIpv4AndDnsFields()
    {
        var profile = TestData.StaticProfile();
        var matching = new NetworkSnapshot
        {
            DhcpEnabled = false,
            Addresses = ["192.0.2.10"],
            PrefixLengths = [24],
            Gateways = ["192.0.2.1"],
            DnsAutomatic = false,
            DnsServers = ["192.0.2.53"]
        };

        Assert.True(Policy.Matches(profile, matching));
        Assert.False(Policy.Matches(profile, matching with { DnsServers = ["198.51.100.53"] }));
        Assert.False(Policy.Matches(profile, matching with { DnsAutomatic = true }));
        Assert.False(Policy.Matches(profile, matching with { DhcpEnabled = true }));
        Assert.False(Policy.Matches(profile, matching with { PrefixLengths = [25] }));
    }

    [Fact]
    public void MatchesDhcpRequiresDhcpAndAutomaticDns()
    {
        var profile = new NetworkProfile { Id = "fallback-dhcp", Mode = "dhcp" };
        var matching = new NetworkSnapshot
        {
            DhcpEnabled = true,
            Addresses = ["192.0.2.10"],
            DnsAutomatic = true,
            DnsServers = ["198.51.100.53"]
        };

        Assert.True(Policy.Matches(profile, matching));
        Assert.False(Policy.Matches(profile, matching with { DnsAutomatic = false }));
        Assert.False(Policy.Matches(profile, matching with { DhcpEnabled = false }));
    }
}
