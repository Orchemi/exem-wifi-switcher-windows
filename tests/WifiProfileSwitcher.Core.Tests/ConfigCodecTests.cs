using System.Text.Json;
using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class ConfigCodecTests
{
    [Fact]
    public void LoadReadsCamelCaseAndAppliesDefaults()
    {
        var adapterId = Guid.NewGuid();
        var json = $$"""
        {
          "version": 1,
          "adapterId": "{{adapterId}}",
          "profiles": [
            {
              "id": "internal",
              "ssids": ["Example-Internal"],
              "address": "192.0.2.10",
              "prefixLength": 24,
              "gateway": "192.0.2.1",
              "dnsServers": ["192.0.2.53"]
            }
          ]
        }
        """;

        var config = ConfigCodec.Load(json);

        Assert.Equal(1, config.Version);
        Assert.Equal(adapterId, config.AdapterId);
        Assert.Equal("observe", config.Mode);
        Assert.Equal("dhcp", config.Fallback);
        Assert.Equal(5, config.PollSeconds);
        Assert.Equal("static", config.Profiles[0].Mode);
    }

    [Fact]
    public void LoadRejectsUnknownMembersWithoutExposingInput()
    {
        var json = $$"""
        {
          "version": 1,
          "adapterId": "{{Guid.NewGuid()}}",
          "unexpectedSecret": "do not echo this",
          "profiles": []
        }
        """;

        var exception = Assert.Throws<ConfigException>(() => ConfigCodec.Load(json));

        Assert.Equal(ConfigErrorCode.InvalidJson, exception.Code);
        Assert.DoesNotContain("do not echo this", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsInvalidConfigurationWithStableCodes()
    {
        var json = $$"""
        {
          "version": 1,
          "adapterId": "{{Guid.Empty}}",
          "mode": "enforce",
          "fallback": "hold",
          "pollSeconds": 1,
          "profiles": []
        }
        """;

        var exception = Assert.Throws<ConfigException>(() => ConfigCodec.Load(json));

        Assert.Equal(ConfigErrorCode.ValidationFailed, exception.Code);
        Assert.Contains(ConfigValidationCode.AdapterIdRequired, exception.Errors);
        Assert.Contains(ConfigValidationCode.PollSecondsOutOfRange, exception.Errors);
        Assert.Contains(ConfigValidationCode.ProfilesRequired, exception.Errors);
        Assert.All(exception.Errors, error => Assert.DoesNotContain("192.", error, StringComparison.Ordinal));
    }

    [Fact]
    public void SerializeRoundTripsValidatedConfiguration()
    {
        var config = TestData.ValidConfig(mode: "observe");

        var json = ConfigCodec.Serialize(config);
        var roundTrip = ConfigCodec.Load(json);

        Assert.Equal(config.Version, roundTrip.Version);
        Assert.Equal(config.AdapterId, roundTrip.AdapterId);
        Assert.Equal(config.Mode, roundTrip.Mode);
        Assert.Equal(config.Fallback, roundTrip.Fallback);
        Assert.Equal(config.PollSeconds, roundTrip.PollSeconds);
        Assert.Equal(config.Profiles.Count, roundTrip.Profiles.Count);
        Assert.Equal(config.Profiles[0].Id, roundTrip.Profiles[0].Id);
        Assert.Equal(config.Profiles[0].Ssids, roundTrip.Profiles[0].Ssids);
        Assert.Equal(config.Profiles[0].Address, roundTrip.Profiles[0].Address);
        Assert.Equal(config.Profiles[0].DnsServers, roundTrip.Profiles[0].DnsServers);
        Assert.Contains("\"adapterId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AdapterId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsDuplicateJsonProperties()
    {
        var adapterId = Guid.NewGuid();
        var json = $$"""
        {
          "version": 1,
          "version": 1,
          "adapterId": "{{adapterId}}",
          "profiles": []
        }
        """;

        var exception = Assert.Throws<ConfigException>(() => ConfigCodec.Load(json));

        Assert.Equal(ConfigErrorCode.InvalidJson, exception.Code);
    }
}
