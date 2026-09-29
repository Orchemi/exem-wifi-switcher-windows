using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class ManualProfileInputTests
{
    private static readonly Guid AdapterId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void CreatesObserveOnlyProfileAndPreservesSsidWhileTrimmingNetworkFields()
    {
        var config = ManualProfileInput.Create(
            AdapterId,
            "  Example-Internal  ",
            " 192.0.2.10 ",
            " 255.255.255.0 ",
            " 192.0.2.1 ",
            " 198.51.100.53, 203.0.113.53 ");

        Assert.Equal(AdapterId, config.AdapterId);
        Assert.Equal("observe", config.Mode);
        Assert.Equal("dhcp", config.Fallback);

        var profile = Assert.Single(config.Profiles);
        Assert.Equal("office", profile.Id);
        Assert.Equal(["  Example-Internal  "], profile.Ssids);
        Assert.Equal("static", profile.Mode);
        Assert.Equal("192.0.2.10", profile.Address);
        Assert.Equal(24, profile.PrefixLength);
        Assert.Equal("192.0.2.1", profile.Gateway);
        Assert.Equal(["198.51.100.53", "203.0.113.53"], profile.DnsServers);
        Assert.Empty(ConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData("/24", 24)]
    [InlineData(" / 24 ", 24)]
    [InlineData("255.255.255.0", 24)]
    [InlineData("255.255.0.0", 16)]
    public void AcceptsPrefixOrDottedSubnetMask(string subnetMask, int expectedPrefix)
    {
        var config = ManualProfileInput.Create(
            AdapterId,
            "Example-Internal",
            "192.0.2.10",
            subnetMask,
            "192.0.2.1",
            "198.51.100.53");

        Assert.Equal(expectedPrefix, Assert.Single(config.Profiles).PrefixLength);
    }

    [Fact]
    public void SplitsDnsOnCommaSemicolonAndWhitespaceWithoutChangingOrder()
    {
        var config = ManualProfileInput.Create(
            AdapterId,
            "Example-Internal",
            "192.0.2.10",
            "/24",
            "192.0.2.1",
            "198.51.100.53; 203.0.113.53\t192.0.2.53,198.51.100.54");

        Assert.Equal(
            ["198.51.100.53", "203.0.113.53", "192.0.2.53", "198.51.100.54"],
            Assert.Single(config.Profiles).DnsServers);
    }

    [Fact]
    public void RejectsDuplicateDnsWithFieldFriendlySafeIssue()
    {
        var exception = Assert.Throws<ManualInputException>(() => ManualProfileInput.Create(
            AdapterId,
            "Example-Internal",
            "192.0.2.10",
            "/24",
            "192.0.2.1",
            "198.51.100.53, 198.51.100.53"));

        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.Dns && issue.Code == ManualInputErrorCode.DnsDuplicate);
        Assert.Equal(ManualInputField.Dns, exception.Field);
    }

    [Fact]
    public void RejectsMoreThanFourDnsInsteadOfSilentlyDroppingTheRest()
    {
        var exception = Assert.Throws<ManualInputException>(() => ManualProfileInput.Create(
            AdapterId,
            "Example-Internal",
            "192.0.2.10",
            "/24",
            "192.0.2.1",
            "198.51.100.53 203.0.113.53 192.0.2.53 198.51.100.54 203.0.113.54"));

        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.Dns && issue.Code == ManualInputErrorCode.DnsTooMany);
    }

    [Theory]
    [InlineData("255.0.255.0")]
    [InlineData("/0")]
    [InlineData("/31")]
    [InlineData("255.255.255.255")]
    public void RejectsNonContiguousOrUnsupportedSubnetMasks(string subnetMask)
    {
        var exception = Assert.Throws<ManualInputException>(() => ManualProfileInput.Create(
            AdapterId,
            "Example-Internal",
            "192.0.2.10",
            subnetMask,
            "192.0.2.1",
            "198.51.100.53"));

        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.SubnetMask && issue.Code == ManualInputErrorCode.SubnetMaskInvalid);
    }

    [Fact]
    public void ReportsRequiredAndInvalidFieldsWithoutExposingValues()
    {
        var exception = Assert.Throws<ManualInputException>(() => ManualProfileInput.Create(
            Guid.Empty,
            "   ",
            "not-an-ip",
            "",
            "not-an-ip",
            "198.51.100.53,not-an-ip"));

        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.AdapterId && issue.Code == ManualInputErrorCode.AdapterIdRequired);
        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.Ssid && issue.Code == ManualInputErrorCode.SsidRequired);
        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.Address && issue.Code == ManualInputErrorCode.AddressInvalid);
        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.SubnetMask && issue.Code == ManualInputErrorCode.SubnetMaskRequired);
        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.Gateway && issue.Code == ManualInputErrorCode.GatewayInvalid);
        Assert.Contains(exception.Issues, issue =>
            issue.Field == ManualInputField.Dns && issue.Code == ManualInputErrorCode.DnsInvalid);
        Assert.DoesNotContain("not-an-ip", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "128.0.0.0")]
    [InlineData(16, "255.255.0.0")]
    [InlineData(24, "255.255.255.0")]
    [InlineData(30, "255.255.255.252")]
    public void ConvertsPrefixToDottedSubnetMask(int prefixLength, string expectedMask)
    {
        Assert.Equal(expectedMask, ManualProfileInput.ToSubnetMask(prefixLength));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(32)]
    public void RejectsUnsupportedPrefixWhenConvertingToSubnetMask(int prefixLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ManualProfileInput.ToSubnetMask(prefixLength));
    }
}
