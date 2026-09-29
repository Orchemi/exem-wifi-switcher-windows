using System.Net;
using System.Text;
using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class ConfigValidatorTests
{
    [Fact]
    public void StaticProfileRequiresCompleteSafeIpv4Configuration()
    {
        var config = TestData.ValidConfig(profiles:
        [
            new NetworkProfile
            {
                Id = "internal",
                Ssids = ["Example-Internal"],
                Address = "192.0.2.10",
                PrefixLength = 24,
                Gateway = "198.51.100.1",
                DnsServers = []
            }
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.GatewaySubnetMismatch, errors);
        Assert.Contains(ConfigValidationCode.DnsRequired, errors);
    }

    [Theory]
    [InlineData("192.0.2")]
    [InlineData("192.0.2.01")]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    public void RejectsNonUsableIpv4Addresses(string address)
    {
        var config = TestData.ValidConfig(profiles:
        [
            new NetworkProfile
            {
                Id = "internal",
                Ssids = ["Example-Internal"],
                Address = address,
                PrefixLength = 24,
                Gateway = "192.0.2.1",
                DnsServers = ["192.0.2.53"]
            }
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.AddressInvalid, errors);
    }

    [Fact]
    public void RejectsPrefixOutsideSupportedHostRange()
    {
        var config = TestData.ValidConfig(profiles:
        [
            TestData.StaticProfile(prefixLength: 31)
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.PrefixLengthInvalid, errors);
    }

    [Fact]
    public void RejectsDhcpProfileWithStaticFields()
    {
        var config = TestData.ValidConfig(profiles:
        [
            new NetworkProfile
            {
                Id = "external",
                Ssids = ["Example-External"],
                Mode = "dhcp",
                Address = "192.0.2.10",
                DnsServers = ["192.0.2.53"]
            }
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.DhcpStaticFields, errors);
    }

    [Fact]
    public void RejectsDuplicateProfileIdsAndSsids()
    {
        var config = TestData.ValidConfig(profiles:
        [
            TestData.StaticProfile(id: "same", ssid: "Example-Internal"),
            TestData.StaticProfile(id: "same", ssid: "Example-Internal")
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.ProfileIdDuplicate, errors);
        Assert.Contains(ConfigValidationCode.SsidDuplicate, errors);
    }

    [Fact]
    public void RejectsSsidLongerThan80211ByteLimit()
    {
        var longSsid = string.Concat(Enumerable.Repeat("가", 11));
        Assert.True(Encoding.UTF8.GetByteCount(longSsid) > 32);
        var config = TestData.ValidConfig(profiles:
        [
            TestData.StaticProfile(ssid: longSsid)
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.SsidTooLong, errors);
    }

    [Fact]
    public void RejectsEnforceWithoutAnInternalProfile()
    {
        var config = TestData.ValidConfig(mode: "enforce", profiles: []);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.ProfilesRequired, errors);
    }

    [Fact]
    public void AllowsObserveConfigurationWithoutProfiles()
    {
        var config = TestData.ValidConfig(mode: "observe", profiles: []);

        var errors = ConfigValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsNullListsFromJsonObjectGraph()
    {
        var config = TestData.ValidConfig(profiles:
        [
            new NetworkProfile
            {
                Id = "internal",
                Ssids = null!,
                Address = "192.0.2.10",
                PrefixLength = 24,
                Gateway = "192.0.2.1",
                DnsServers = null!
            }
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.SsidRequired, errors);
        Assert.Contains(ConfigValidationCode.DnsRequired, errors);
    }

    [Fact]
    public void RejectsGatewayEqualToAddress()
    {
        var config = TestData.ValidConfig(profiles:
        [
            TestData.StaticProfile(gateway: "192.0.2.10")
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.GatewaySameAsAddress, errors);
    }

    [Fact]
    public void RejectsMoreThanFourDnsServers()
    {
        var config = TestData.ValidConfig(profiles:
        [
            new NetworkProfile
            {
                Id = "internal",
                Ssids = ["Example-Internal"],
                Address = "192.0.2.10",
                PrefixLength = 24,
                Gateway = "192.0.2.1",
                DnsServers = ["192.0.2.53", "198.51.100.53", "203.0.113.53", "192.0.2.54", "198.51.100.54"]
            }
        ]);

        var errors = ConfigValidator.Validate(config);

        Assert.Contains(ConfigValidationCode.DnsTooMany, errors);
    }

    [Fact]
    public void AcceptsDocumentOnlyIpv4Profile()
    {
        var errors = ConfigValidator.Validate(TestData.ValidConfig());

        Assert.Empty(errors);
        Assert.True(IPAddress.TryParse("192.0.2.10", out _));
    }
}
