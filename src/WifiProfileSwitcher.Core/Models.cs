namespace WifiProfileSwitcher.Core;

/// <summary>
/// The configuration consumed by both the foreground prototype and the Windows service.
/// Values are intentionally plain data so the policy can be exercised without Windows APIs.
/// </summary>
public sealed record SwitcherConfig
{
    public int Version { get; init; } = 1;
    public Guid AdapterId { get; init; }
    public string Mode { get; init; } = "observe";
    public string Fallback { get; init; } = "dhcp";
    public int PollSeconds { get; init; } = 5;
    public List<NetworkProfile> Profiles { get; init; } = [];
}

/// <summary>
/// A configuration to use when the connected SSID is recognized.
/// </summary>
public sealed record NetworkProfile
{
    public string Id { get; init; } = string.Empty;
    public string[] Ssids { get; init; } = [];
    public string Mode { get; init; } = "static";
    public string? Address { get; init; }
    public int? PrefixLength { get; init; }
    public string? Gateway { get; init; }
    public string[] DnsServers { get; init; } = [];
}

/// <summary>
/// The IPv4/DNS facts read for the selected Wi-Fi adapter.
/// IPv6 is deliberately outside this contract and must be filtered by the Windows reader.
/// </summary>
public sealed record NetworkSnapshot
{
    public bool DhcpEnabled { get; init; }
    public string[] Addresses { get; init; } = [];
    public int[] PrefixLengths { get; init; } = [];
    public string[] Gateways { get; init; } = [];
    public bool DnsAutomatic { get; init; }
    public string[] DnsServers { get; init; } = [];

    /// <summary>
    /// False when an address is tentative, duplicate, or otherwise not usable yet.
    /// The default keeps the protocol compatible with simple readers that only expose
    /// the older fields; Windows readers should set it explicitly when the state is known.
    /// </summary>
    public bool AddressesReady { get; init; } = true;
}

/// <summary>
/// A safe, stable identifier for a configuration parsing failure.
/// These values may be printed in diagnostics; raw JSON and exception text must not be.
/// </summary>
public static class ConfigErrorCode
{
    public const string Empty = "config_empty";
    public const string InvalidJson = "config_invalid_json";
    public const string ValidationFailed = "config_validation_failed";
}

/// <summary>
/// Safe validation identifiers. They contain no user supplied values.
/// </summary>
public static class ConfigValidationCode
{
    public const string Null = "config_null";
    public const string VersionUnsupported = "version_unsupported";
    public const string AdapterIdRequired = "adapter_id_required";
    public const string ModeInvalid = "mode_invalid";
    public const string FallbackInvalid = "fallback_invalid";
    public const string PollSecondsOutOfRange = "poll_seconds_out_of_range";
    public const string ProfilesRequired = "profiles_required";
    public const string ProfilesListInvalid = "profiles_list_invalid";
    public const string ProfileNull = "profile_null";
    public const string ProfileIdRequired = "profile_id_required";
    public const string ProfileIdInvalid = "profile_id_invalid";
    public const string ProfileIdDuplicate = "profile_id_duplicate";
    public const string SsidRequired = "ssid_required";
    public const string SsidInvalid = "ssid_invalid";
    public const string SsidTooLong = "ssid_too_long";
    public const string SsidDuplicate = "ssid_duplicate";
    public const string ProfileModeInvalid = "profile_mode_invalid";
    public const string AddressRequired = "address_required";
    public const string AddressInvalid = "address_invalid";
    public const string PrefixLengthRequired = "prefix_length_required";
    public const string PrefixLengthInvalid = "prefix_length_invalid";
    public const string GatewayRequired = "gateway_required";
    public const string GatewayInvalid = "gateway_invalid";
    public const string GatewaySubnetMismatch = "gateway_subnet_mismatch";
    public const string GatewaySameAsAddress = "gateway_same_as_address";
    public const string DnsRequired = "dns_required";
    public const string DnsInvalid = "dns_invalid";
    public const string DnsDuplicate = "dns_duplicate";
    public const string DnsTooMany = "dns_too_many";
    public const string DhcpStaticFields = "dhcp_static_fields";
}

/// <summary>
/// A configuration error whose code and validation list are safe to expose to a user.
/// </summary>
public sealed class ConfigException : Exception
{
    public ConfigException(string code, IReadOnlyList<string>? errors = null)
        : base(MessageFor(code))
    {
        Code = code;
        Errors = errors is null ? Array.Empty<string>() : errors.ToArray();
    }

    public string Code { get; }
    public IReadOnlyList<string> Errors { get; }

    private static string MessageFor(string code) => code switch
    {
        ConfigErrorCode.Empty => "Configuration text is empty.",
        ConfigErrorCode.InvalidJson => "Configuration text is not valid JSON.",
        ConfigErrorCode.ValidationFailed => "Configuration values are invalid.",
        _ => "Configuration could not be loaded."
    };
}

/// <summary>
/// Safe result identifiers returned by <see cref="StateEngine.Evaluate"/>.
/// </summary>
public static class StateDecisionCode
{
    public const string Apply = "apply";
    public const string AlreadyMatched = "already_matched";
    public const string AdapterMismatch = "adapter_mismatch";
    public const string Backoff = "backoff";
    public const string ConfigInvalid = "config_invalid";
    public const string FailureLimit = "failure_limit";
    public const string Ineffective = "ineffective";
    public const string NetworkStateUnavailable = "network_state_unavailable";
    public const string NoTarget = "no_target";
    public const string ObserveOnly = "observe_only";
    public const string Settling = "settling";
    public const string SsidUnavailable = "ssid_unavailable";
    public const string Stabilizing = "stabilizing";
}

/// <summary>
/// The pure decision returned to the host runner.
/// </summary>
public sealed record Decision(string Code, NetworkProfile? Target, bool ShouldApply);
