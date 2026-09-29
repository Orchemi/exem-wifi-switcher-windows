using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class InitialProfileCaptureTests
{
    private static readonly Guid AdapterId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void CapturesManualConfigurationAsAnObserveOnlyOfficeProfile()
    {
        var config = InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot());

        Assert.Equal(1, config.Version);
        Assert.Equal(AdapterId, config.AdapterId);
        Assert.Equal("observe", config.Mode);
        Assert.Equal("dhcp", config.Fallback);
        var profile = Assert.Single(config.Profiles);
        Assert.Equal("office", profile.Id);
        Assert.Equal(["Example-Internal"], profile.Ssids);
        Assert.Equal("static", profile.Mode);
        Assert.Equal("192.0.2.10", profile.Address);
        Assert.Equal(24, profile.PrefixLength);
        Assert.Equal("192.0.2.1", profile.Gateway);
        Assert.Equal(["198.51.100.53", "203.0.113.53"], profile.DnsServers);
        Assert.Empty(ConfigValidator.Validate(config));
    }

    [Fact]
    public void RejectsDhcpSoALeaseIsNeverFrozenAsACompanyProfile()
    {
        var exception = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with { DhcpEnabled = true }));

        Assert.Equal(CaptureErrorCode.DhcpEnabled, exception.Code);
    }

    [Fact]
    public void RejectsUnavailableSsid()
    {
        var exception = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, null, ManualSnapshot()));

        Assert.Equal(CaptureErrorCode.SsidUnavailable, exception.Code);
    }

    [Fact]
    public void RejectsAutomaticDns()
    {
        var exception = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with { DnsAutomatic = true }));

        Assert.Equal(CaptureErrorCode.DnsAutomatic, exception.Code);
    }

    [Fact]
    public void RejectsAnUnreadyAddressBeforeCreatingAProfile()
    {
        var exception = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with { AddressesReady = false }));

        Assert.Equal(CaptureErrorCode.AddressesNotReady, exception.Code);
    }

    [Theory]
    [InlineData(CaptureErrorCode.AddressMissing)]
    [InlineData(CaptureErrorCode.AddressAmbiguous)]
    public void RejectsMissingOrAmbiguousAddresses(string expectedCode)
    {
        var snapshot = ManualSnapshot() with
        {
            Addresses = expectedCode == CaptureErrorCode.AddressMissing
                ? []
                : ["192.0.2.10", "192.0.2.11"]
        };

        var exception = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", snapshot));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void RejectsMissingOrAmbiguousGateway()
    {
        var missing = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with { Gateways = [] }));
        var ambiguous = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with
            {
                Gateways = ["192.0.2.1", "192.0.2.2"]
            }));

        Assert.Equal(CaptureErrorCode.GatewayMissing, missing.Code);
        Assert.Equal(CaptureErrorCode.GatewayAmbiguous, ambiguous.Code);
    }

    [Fact]
    public void RejectsMissingOrAmbiguousDns()
    {
        var missing = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with { DnsServers = [] }));
        var ambiguous = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with
            {
                DnsServers = ["198.51.100.53", "203.0.113.53", "198.51.100.54", "203.0.113.54", "192.0.2.53"]
            }));

        Assert.Equal(CaptureErrorCode.DnsMissing, missing.Code);
        Assert.Equal(CaptureErrorCode.DnsAmbiguous, ambiguous.Code);
    }

    [Fact]
    public void ReturnsSafeValidationErrorsForAnInvalidCapturedConfiguration()
    {
        var exception = Assert.Throws<CaptureException>(() =>
            InitialProfileCapture.Create(AdapterId, "Example-Internal", ManualSnapshot() with
            {
                Gateways = ["203.0.113.1"]
            }));

        Assert.Equal(CaptureErrorCode.ConfigurationInvalid, exception.Code);
        Assert.Contains(ConfigValidationCode.GatewaySubnetMismatch, exception.Errors);
    }

    private static NetworkSnapshot ManualSnapshot() => new()
    {
        DhcpEnabled = false,
        Addresses = ["192.0.2.10"],
        PrefixLengths = [24],
        Gateways = ["192.0.2.1"],
        AddressesReady = true,
        DnsAutomatic = false,
        DnsServers = ["198.51.100.53", "203.0.113.53"]
    };
}
