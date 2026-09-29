namespace WifiProfileSwitcher.Core;

/// <summary>
/// Stable, value-free identifiers for failures while importing the currently
/// connected adapter's manual IPv4 configuration.
/// </summary>
public static class CaptureErrorCode
{
    public const string AdapterIdRequired = "capture_adapter_id_required";
    public const string SnapshotUnavailable = "capture_snapshot_unavailable";
    public const string SsidUnavailable = "capture_ssid_unavailable";
    public const string SsidInvalid = "capture_ssid_invalid";
    public const string DhcpEnabled = "capture_dhcp_enabled";
    public const string AddressesNotReady = "capture_addresses_not_ready";
    public const string AddressMissing = "capture_address_missing";
    public const string AddressAmbiguous = "capture_address_ambiguous";
    public const string PrefixMissing = "capture_prefix_missing";
    public const string PrefixAmbiguous = "capture_prefix_ambiguous";
    public const string GatewayMissing = "capture_gateway_missing";
    public const string GatewayAmbiguous = "capture_gateway_ambiguous";
    public const string DnsAutomatic = "capture_dns_automatic";
    public const string DnsMissing = "capture_dns_missing";
    public const string DnsAmbiguous = "capture_dns_ambiguous";
    public const string ConfigurationInvalid = "capture_configuration_invalid";
}

/// <summary>
/// A safe, user-facing failure from the read-only initial profile capture.
/// Exception messages and errors never contain captured network values.
/// </summary>
public sealed class CaptureException : Exception
{
    public CaptureException(string code, IReadOnlyList<string>? errors = null)
        : base("The current network configuration could not be captured safely.")
    {
        Code = code;
        Errors = errors is null ? Array.Empty<string>() : errors.ToArray();
    }

    public string Code { get; }
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Converts an already observed manual adapter configuration into a disabled
/// (observe-only) company profile candidate. This function never enables
/// enforcement and never treats a DHCP lease as a reusable static address.
/// </summary>
public static class InitialProfileCapture
{
    public static SwitcherConfig Create(Guid adapterId, string? ssid, NetworkSnapshot snapshot)
    {
        if (adapterId == Guid.Empty)
        {
            throw new CaptureException(CaptureErrorCode.AdapterIdRequired);
        }

        if (snapshot is null)
        {
            throw new CaptureException(CaptureErrorCode.SnapshotUnavailable);
        }

        if (string.IsNullOrWhiteSpace(ssid))
        {
            throw new CaptureException(CaptureErrorCode.SsidUnavailable);
        }

        if (snapshot.DhcpEnabled)
        {
            throw new CaptureException(CaptureErrorCode.DhcpEnabled);
        }

        if (!snapshot.AddressesReady)
        {
            throw new CaptureException(CaptureErrorCode.AddressesNotReady);
        }

        var address = RequireExactlyOne(
            snapshot.Addresses,
            CaptureErrorCode.AddressMissing,
            CaptureErrorCode.AddressAmbiguous);
        var prefixLength = RequireExactlyOne(
            snapshot.PrefixLengths,
            CaptureErrorCode.PrefixMissing,
            CaptureErrorCode.PrefixAmbiguous);
        var gateway = RequireExactlyOne(
            snapshot.Gateways,
            CaptureErrorCode.GatewayMissing,
            CaptureErrorCode.GatewayAmbiguous);

        if (snapshot.DnsAutomatic)
        {
            throw new CaptureException(CaptureErrorCode.DnsAutomatic);
        }

        if (snapshot.DnsServers is null || snapshot.DnsServers.Length == 0)
        {
            throw new CaptureException(CaptureErrorCode.DnsMissing);
        }

        if (snapshot.DnsServers.Length > 4)
        {
            throw new CaptureException(CaptureErrorCode.DnsAmbiguous);
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
                    Ssids = [ssid],
                    Mode = "static",
                    Address = address,
                    PrefixLength = prefixLength,
                    Gateway = gateway,
                    DnsServers = snapshot.DnsServers.ToArray()
                }
            ]
        };

        var errors = ConfigValidator.Validate(config);
        if (errors.Count != 0)
        {
            throw new CaptureException(CaptureErrorCode.ConfigurationInvalid, errors);
        }

        return config;
    }

    private static T RequireExactlyOne<T>(T[]? values, string missingCode, string ambiguousCode)
    {
        if (values is null || values.Length == 0)
        {
            throw new CaptureException(missingCode);
        }

        if (values.Length != 1)
        {
            throw new CaptureException(ambiguousCode);
        }

        return values[0];
    }
}
