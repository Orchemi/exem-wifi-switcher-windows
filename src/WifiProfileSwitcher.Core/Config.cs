using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WifiProfileSwitcher.Core;

/// <summary>
/// Validates user supplied configuration without returning any of its values.
/// </summary>
public static class ConfigValidator
{
    private const int CurrentVersion = 1;
    private const int MinimumPollSeconds = 3;
    private const int MaximumPollSeconds = 60;
    private const int MaximumIdLength = 64;
    private const int MaximumSsidBytes = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static List<string> Validate(SwitcherConfig? config)
    {
        var errors = new List<string>();
        if (config is null)
        {
            errors.Add(ConfigValidationCode.Null);
            return errors;
        }

        if (config.Version != CurrentVersion)
        {
            errors.Add(ConfigValidationCode.VersionUnsupported);
        }

        if (config.AdapterId == Guid.Empty)
        {
            errors.Add(ConfigValidationCode.AdapterIdRequired);
        }

        if (config.Mode is not ("observe" or "enforce"))
        {
            errors.Add(ConfigValidationCode.ModeInvalid);
        }

        if (config.Fallback is not ("dhcp" or "hold"))
        {
            errors.Add(ConfigValidationCode.FallbackInvalid);
        }

        if (config.PollSeconds is < MinimumPollSeconds or > MaximumPollSeconds)
        {
            errors.Add(ConfigValidationCode.PollSecondsOutOfRange);
        }

        if (config.Profiles is null)
        {
            errors.Add(ConfigValidationCode.ProfilesListInvalid);
            return errors;
        }

        if (config.Mode == "enforce" && config.Profiles.Count == 0)
        {
            errors.Add(ConfigValidationCode.ProfilesRequired);
        }

        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        var ssids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var profile in config.Profiles)
        {
            if (profile is null)
            {
                errors.Add(ConfigValidationCode.ProfileNull);
                continue;
            }

            ValidateProfile(profile, profileIds, ssids, errors);
        }

        return errors;
    }

    internal static bool TryUsableIpv4(string? text, out uint value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || text.Trim() != text)
        {
            return false;
        }

        var octets = text.Split('.', StringSplitOptions.None);
        if (octets.Length != 4)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];
        for (var index = 0; index < octets.Length; index++)
        {
            var octet = octets[index];
            if (octet.Length is < 1 or > 3 || (octet.Length > 1 && octet[0] == '0'))
            {
                return false;
            }

            var parsed = 0;
            foreach (var character in octet)
            {
                if (character is < '0' or > '9')
                {
                    return false;
                }

                parsed = parsed * 10 + character - '0';
            }

            if (parsed > byte.MaxValue)
            {
                return false;
            }

            bytes[index] = (byte)parsed;
        }

        // Keep special-purpose and non-unicast values out of user profiles.
        if (bytes[0] == 0 || bytes[0] == 127 || bytes[0] >= 224 ||
            (bytes[0] == 169 && bytes[1] == 254))
        {
            return false;
        }

        value = ((uint)bytes[0] << 24) |
                ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) |
                bytes[3];
        return true;
    }

    private static void ValidateProfile(
        NetworkProfile profile,
        HashSet<string> profileIds,
        HashSet<string> ssids,
        List<string> errors)
    {
        var hasId = !string.IsNullOrWhiteSpace(profile.Id);
        if (!hasId)
        {
            errors.Add(ConfigValidationCode.ProfileIdRequired);
        }
        else
        {
            if (!IsSafeIdentifier(profile.Id))
            {
                errors.Add(ConfigValidationCode.ProfileIdInvalid);
            }

            if (!profileIds.Add(profile.Id))
            {
                errors.Add(ConfigValidationCode.ProfileIdDuplicate);
            }
        }

        if (profile.Ssids is null || profile.Ssids.Length == 0)
        {
            errors.Add(ConfigValidationCode.SsidRequired);
        }
        else
        {
            var profileSsids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ssid in profile.Ssids)
            {
                if (string.IsNullOrEmpty(ssid))
                {
                    errors.Add(ConfigValidationCode.SsidRequired);
                    continue;
                }

                if (!IsValidSsid(ssid, out var tooLong))
                {
                    errors.Add(tooLong ? ConfigValidationCode.SsidTooLong : ConfigValidationCode.SsidInvalid);
                }

                if (!profileSsids.Add(ssid) || !ssids.Add(ssid))
                {
                    errors.Add(ConfigValidationCode.SsidDuplicate);
                }
            }
        }

        if (profile.Mode is not ("static" or "dhcp"))
        {
            errors.Add(ConfigValidationCode.ProfileModeInvalid);
            return;
        }

        if (profile.Mode == "dhcp")
        {
            if (profile.Address is not null || profile.PrefixLength is not null ||
                profile.Gateway is not null || profile.DnsServers is null || profile.DnsServers.Length > 0)
            {
                errors.Add(ConfigValidationCode.DhcpStaticFields);
            }

            return;
        }

        ValidateStaticProfile(profile, errors);
    }

    private static void ValidateStaticProfile(NetworkProfile profile, List<string> errors)
    {
        var addressValid = false;
        var address = 0u;
        if (string.IsNullOrWhiteSpace(profile.Address))
        {
            errors.Add(ConfigValidationCode.AddressRequired);
        }
        else if (!TryUsableIpv4(profile.Address, out address))
        {
            errors.Add(ConfigValidationCode.AddressInvalid);
        }
        else
        {
            addressValid = true;
        }

        var prefixValid = profile.PrefixLength is >= 1 and <= 30;
        if (profile.PrefixLength is null)
        {
            errors.Add(ConfigValidationCode.PrefixLengthRequired);
        }
        else if (!prefixValid)
        {
            errors.Add(ConfigValidationCode.PrefixLengthInvalid);
        }

        var gatewayValid = false;
        var gateway = 0u;
        if (string.IsNullOrWhiteSpace(profile.Gateway))
        {
            errors.Add(ConfigValidationCode.GatewayRequired);
        }
        else if (!TryUsableIpv4(profile.Gateway, out gateway))
        {
            errors.Add(ConfigValidationCode.GatewayInvalid);
        }
        else
        {
            gatewayValid = true;
        }

        if (addressValid && prefixValid && IsNetworkOrBroadcast(address, profile.PrefixLength!.Value))
        {
            errors.Add(ConfigValidationCode.AddressInvalid);
            addressValid = false;
        }

        if (gatewayValid && prefixValid && IsNetworkOrBroadcast(gateway, profile.PrefixLength!.Value))
        {
            errors.Add(ConfigValidationCode.GatewayInvalid);
            gatewayValid = false;
        }

        if (addressValid && gatewayValid && prefixValid)
        {
            if (address == gateway)
            {
                errors.Add(ConfigValidationCode.GatewaySameAsAddress);
            }
            else if (!SameSubnet(address, gateway, profile.PrefixLength!.Value))
            {
                errors.Add(ConfigValidationCode.GatewaySubnetMismatch);
            }
        }

        if (profile.DnsServers is null || profile.DnsServers.Length == 0)
        {
            errors.Add(ConfigValidationCode.DnsRequired);
            return;
        }

        if (profile.DnsServers.Length > 4)
        {
            errors.Add(ConfigValidationCode.DnsTooMany);
        }

        var dnsServers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dns in profile.DnsServers)
        {
            if (!TryUsableIpv4(dns, out _))
            {
                errors.Add(ConfigValidationCode.DnsInvalid);
            }

            if (dns is null || !dnsServers.Add(dns))
            {
                errors.Add(ConfigValidationCode.DnsDuplicate);
            }
        }
    }

    private static bool IsSafeIdentifier(string value)
    {
        return value.Length <= MaximumIdLength && value.Trim() == value &&
               value.All(character => !char.IsControl(character));
    }

    private static bool IsValidSsid(string value, out bool tooLong)
    {
        tooLong = false;
        try
        {
            if (StrictUtf8.GetByteCount(value) > MaximumSsidBytes)
            {
                tooLong = true;
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        return value.All(character => !char.IsControl(character));
    }

    private static bool SameSubnet(uint first, uint second, int prefixLength)
    {
        var mask = uint.MaxValue << (32 - prefixLength);
        return (first & mask) == (second & mask);
    }

    private static bool IsNetworkOrBroadcast(uint address, int prefixLength)
    {
        var mask = uint.MaxValue << (32 - prefixLength);
        var network = address & mask;
        var broadcast = network | ~mask;
        return address == network || address == broadcast;
    }
}

/// <summary>
/// Strict JSON boundary for the public configuration file.
/// </summary>
public static class ConfigCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = true
    };

    public static SwitcherConfig Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ConfigException(ConfigErrorCode.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

            if (HasDuplicateProperties(document.RootElement))
            {
                throw new ConfigException(ConfigErrorCode.InvalidJson);
            }

            var config = document.RootElement.Deserialize<SwitcherConfig>(JsonOptions)
                         ?? throw new ConfigException(ConfigErrorCode.InvalidJson);
            return ValidateOrThrow(config);
        }
        catch (ConfigException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new ConfigException(ConfigErrorCode.InvalidJson);
        }
        catch (NotSupportedException)
        {
            throw new ConfigException(ConfigErrorCode.InvalidJson);
        }
    }

    public static string Serialize(SwitcherConfig config)
    {
        var validConfig = ValidateOrThrow(config);
        return JsonSerializer.Serialize(validConfig, JsonOptions);
    }

    private static SwitcherConfig ValidateOrThrow(SwitcherConfig config)
    {
        var errors = ConfigValidator.Validate(config);
        if (errors.Count != 0)
        {
            throw new ConfigException(ConfigErrorCode.ValidationFailed, errors);
        }

        return config;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name) || HasDuplicateProperties(property.Value))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array:
                return element.EnumerateArray().Any(HasDuplicateProperties);

            default:
                return false;
        }
    }
}
