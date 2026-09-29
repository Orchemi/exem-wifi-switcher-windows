using System.Globalization;

namespace WifiProfileSwitcher.Core;

/// <summary>
/// Stable field identifiers used by the setup UI when it validates a manual
/// network profile. They intentionally contain no user-provided values.
/// </summary>
public static class ManualInputField
{
    public const string AdapterId = "adapterId";
    public const string Ssid = "ssid";
    public const string Address = "address";
    public const string SubnetMask = "subnetMask";
    public const string Gateway = "gateway";
    public const string Dns = "dns";
    public const string Configuration = "configuration";
}

/// <summary>
/// Stable, value-free error identifiers returned by <see cref="ManualProfileInput"/>.
/// </summary>
public static class ManualInputErrorCode
{
    public const string AdapterIdRequired = "manual_adapter_id_required";
    public const string SsidRequired = "manual_ssid_required";
    public const string SsidInvalid = "manual_ssid_invalid";
    public const string SsidTooLong = "manual_ssid_too_long";
    public const string AddressRequired = "manual_address_required";
    public const string AddressInvalid = "manual_address_invalid";
    public const string SubnetMaskRequired = "manual_subnet_mask_required";
    public const string SubnetMaskInvalid = "manual_subnet_mask_invalid";
    public const string GatewayRequired = "manual_gateway_required";
    public const string GatewayInvalid = "manual_gateway_invalid";
    public const string GatewaySubnetMismatch = "manual_gateway_subnet_mismatch";
    public const string GatewaySameAsAddress = "manual_gateway_same_as_address";
    public const string DnsRequired = "manual_dns_required";
    public const string DnsInvalid = "manual_dns_invalid";
    public const string DnsDuplicate = "manual_dns_duplicate";
    public const string DnsTooMany = "manual_dns_too_many";
    public const string ConfigurationInvalid = "manual_configuration_invalid";
}

/// <summary>
/// A safe issue that the setup UI can associate with one input field.
/// </summary>
public sealed record ManualInputIssue(string Field, string Code);

/// <summary>
/// A validation failure from manual network input. The exception text and all
/// issue properties are safe to show in diagnostics because they never include
/// the entered SSID or network values.
/// </summary>
public sealed class ManualInputException : Exception
{
    public ManualInputException(IReadOnlyList<ManualInputIssue> issues)
        : base("Manual network settings are invalid.")
    {
        if (issues is null || issues.Count == 0)
        {
            throw new ArgumentException("At least one issue is required.", nameof(issues));
        }

        Issues = issues.ToArray();
        Field = Issues[0].Field;
        Code = Issues[0].Code;
    }

    public string Field { get; }
    public string Code { get; }
    public IReadOnlyList<ManualInputIssue> Issues { get; }
}

/// <summary>
/// Converts fields from a setup form into one disabled (observe-only) static
/// profile. It is deliberately independent of Windows APIs so the same
/// validation is used by the setup UI and unit tests.
/// </summary>
public static class ManualProfileInput
{
    private const int MinimumPrefixLength = 1;
    private const int MaximumPrefixLength = 30;
    private static readonly char[] DnsSeparators = [',', ';', ' ', '\t', '\r', '\n', '\f', '\v'];

    /// <summary>
    /// Creates an observe-only company profile candidate. SSID casing and any
    /// intentional non-control whitespace are preserved; IP fields are trimmed.
    /// </summary>
    public static SwitcherConfig Create(
        Guid adapterId,
        string? ssid,
        string? address,
        string? subnetMask,
        string? gateway,
        string? dnsText)
    {
        var issues = new List<ManualInputIssue>();

        if (adapterId == Guid.Empty)
        {
            AddIssue(issues, ManualInputField.AdapterId, ManualInputErrorCode.AdapterIdRequired);
        }

        if (string.IsNullOrWhiteSpace(ssid))
        {
            AddIssue(issues, ManualInputField.Ssid, ManualInputErrorCode.SsidRequired);
        }

        var normalizedAddress = ParseIpv4(
            address,
            ManualInputField.Address,
            ManualInputErrorCode.AddressRequired,
            ManualInputErrorCode.AddressInvalid,
            issues);
        var prefixLength = ParseSubnetMask(subnetMask, issues);
        var normalizedGateway = ParseIpv4(
            gateway,
            ManualInputField.Gateway,
            ManualInputErrorCode.GatewayRequired,
            ManualInputErrorCode.GatewayInvalid,
            issues);
        var dnsServers = ParseDns(dnsText, issues);

        if (issues.Count != 0)
        {
            throw new ManualInputException(issues);
        }

        var config = new SwitcherConfig
        {
            Version = 1,
            AdapterId = adapterId,
            Mode = "observe",
            Fallback = "dhcp",
            PollSeconds = 5,
            Profiles =
            [
                new NetworkProfile
                {
                    Id = "office",
                    Ssids = [ssid!],
                    Mode = "static",
                    Address = normalizedAddress,
                    PrefixLength = prefixLength,
                    Gateway = normalizedGateway,
                    DnsServers = dnsServers.ToArray()
                }
            ]
        };

        var validationIssues = ConfigValidator.Validate(config)
            .Select(MapValidationIssue)
            .ToList();
        if (validationIssues.Count != 0)
        {
            throw new ManualInputException(Deduplicate(validationIssues));
        }

        return config;
    }

    /// <summary>
    /// Converts a supported host prefix length to dotted IPv4 subnet-mask
    /// notation for display in the setup form.
    /// </summary>
    public static string ToSubnetMask(int prefixLength)
    {
        if (prefixLength is < MinimumPrefixLength or > MaximumPrefixLength)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), "Only /1 through /30 are supported.");
        }

        var mask = uint.MaxValue << (32 - prefixLength);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(mask >> 24) & 0xff}.{(mask >> 16) & 0xff}.{(mask >> 8) & 0xff}.{mask & 0xff}");
    }

    private static string? ParseIpv4(
        string? input,
        string field,
        string requiredCode,
        string invalidCode,
        List<ManualInputIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            AddIssue(issues, field, requiredCode);
            return null;
        }

        var normalized = input.Trim();
        if (!ConfigValidator.TryUsableIpv4(normalized, out _))
        {
            AddIssue(issues, field, invalidCode);
            return null;
        }

        return normalized;
    }

    private static int? ParseSubnetMask(string? input, List<ManualInputIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            AddIssue(issues, ManualInputField.SubnetMask, ManualInputErrorCode.SubnetMaskRequired);
            return null;
        }

        var normalized = input.Trim();
        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            var prefixText = normalized[1..].Trim();
            if (TryParsePrefix(prefixText, out var slashPrefix))
            {
                return slashPrefix;
            }
        }
        else if (TryParseDottedMask(normalized, out var dottedPrefix))
        {
            return dottedPrefix;
        }

        AddIssue(issues, ManualInputField.SubnetMask, ManualInputErrorCode.SubnetMaskInvalid);
        return null;
    }

    private static List<string> ParseDns(string? input, List<ManualInputIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            AddIssue(issues, ManualInputField.Dns, ManualInputErrorCode.DnsRequired);
            return [];
        }

        var servers = input
            .Split(DnsSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .ToList();

        if (servers.Count == 0)
        {
            AddIssue(issues, ManualInputField.Dns, ManualInputErrorCode.DnsRequired);
            return servers;
        }

        if (servers.Count > 4)
        {
            // Keep every token so the caller receives an error instead of a
            // silently truncated profile.
            AddIssue(issues, ManualInputField.Dns, ManualInputErrorCode.DnsTooMany);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in servers)
        {
            if (!ConfigValidator.TryUsableIpv4(server, out _))
            {
                AddIssue(issues, ManualInputField.Dns, ManualInputErrorCode.DnsInvalid);
            }

            if (!seen.Add(server))
            {
                AddIssue(issues, ManualInputField.Dns, ManualInputErrorCode.DnsDuplicate);
            }
        }

        return servers;
    }

    private static bool TryParsePrefix(string value, out int prefixLength)
    {
        prefixLength = 0;
        if (value.Length == 0 || value.Any(character => character is < '0' or > '9') ||
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength))
        {
            return false;
        }

        return prefixLength is >= MinimumPrefixLength and <= MaximumPrefixLength;
    }

    private static bool TryParseDottedMask(string value, out int prefixLength)
    {
        prefixLength = 0;
        var octets = value.Split('.', StringSplitOptions.None);
        if (octets.Length != 4)
        {
            return false;
        }

        uint mask = 0;
        foreach (var octet in octets)
        {
            if (!TryParseOctet(octet, out var parsed))
            {
                return false;
            }

            mask = (mask << 8) | parsed;
        }

        var encounteredZero = false;
        for (var bit = 31; bit >= 0; bit--)
        {
            if ((mask & (1u << bit)) != 0)
            {
                if (encounteredZero)
                {
                    return false;
                }

                prefixLength++;
            }
            else
            {
                encounteredZero = true;
            }
        }

        return prefixLength is >= MinimumPrefixLength and <= MaximumPrefixLength;
    }

    private static bool TryParseOctet(string value, out uint octet)
    {
        octet = 0;
        if (value.Length is < 1 or > 3 || (value.Length > 1 && value[0] == '0') ||
            value.Any(character => character is < '0' or > '9') ||
            !uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out octet))
        {
            return false;
        }

        return octet <= byte.MaxValue;
    }

    private static ManualInputIssue MapValidationIssue(string code) => code switch
    {
        ConfigValidationCode.SsidRequired => new(ManualInputField.Ssid, ManualInputErrorCode.SsidRequired),
        ConfigValidationCode.SsidInvalid => new(ManualInputField.Ssid, ManualInputErrorCode.SsidInvalid),
        ConfigValidationCode.SsidTooLong => new(ManualInputField.Ssid, ManualInputErrorCode.SsidTooLong),
        ConfigValidationCode.AddressRequired => new(ManualInputField.Address, ManualInputErrorCode.AddressRequired),
        ConfigValidationCode.AddressInvalid => new(ManualInputField.Address, ManualInputErrorCode.AddressInvalid),
        ConfigValidationCode.GatewayRequired => new(ManualInputField.Gateway, ManualInputErrorCode.GatewayRequired),
        ConfigValidationCode.GatewayInvalid => new(ManualInputField.Gateway, ManualInputErrorCode.GatewayInvalid),
        ConfigValidationCode.GatewaySubnetMismatch => new(ManualInputField.Gateway, ManualInputErrorCode.GatewaySubnetMismatch),
        ConfigValidationCode.GatewaySameAsAddress => new(ManualInputField.Gateway, ManualInputErrorCode.GatewaySameAsAddress),
        ConfigValidationCode.DnsRequired => new(ManualInputField.Dns, ManualInputErrorCode.DnsRequired),
        ConfigValidationCode.DnsInvalid => new(ManualInputField.Dns, ManualInputErrorCode.DnsInvalid),
        ConfigValidationCode.DnsDuplicate => new(ManualInputField.Dns, ManualInputErrorCode.DnsDuplicate),
        ConfigValidationCode.DnsTooMany => new(ManualInputField.Dns, ManualInputErrorCode.DnsTooMany),
        _ => new(ManualInputField.Configuration, ManualInputErrorCode.ConfigurationInvalid)
    };

    private static IReadOnlyList<ManualInputIssue> Deduplicate(IEnumerable<ManualInputIssue> issues)
    {
        var seen = new HashSet<(string Field, string Code)>();
        return issues.Where(issue => seen.Add((issue.Field, issue.Code))).ToArray();
    }

    private static void AddIssue(List<ManualInputIssue> issues, string field, string code)
    {
        if (!issues.Any(issue => issue.Field == field && issue.Code == code))
        {
            issues.Add(new ManualInputIssue(field, code));
        }
    }
}
