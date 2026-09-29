namespace WifiProfileSwitcher.Core;

public sealed record ServiceObservation(int SchemaVersion, DateTimeOffset ObservedAt, string Mode, string Code,
    bool Connected, bool TargetFound, bool WouldChange, bool MutationAttempted);

public static class SetupReadiness
{
    public static bool CanEnable(ServiceObservation? status, DateTimeOffset serviceStartedAt, DateTimeOffset now)
    {
        if (status is not { SchemaVersion: 1, Mode: "observe", Connected: true, TargetFound: true,
            MutationAttempted: false } || status.ObservedAt < serviceStartedAt || status.ObservedAt > now ||
            now - status.ObservedAt > TimeSpan.FromSeconds(15))
        {
            return false;
        }

        // An automatically captured profile is already matched at setup time.
        // A manually entered profile can be reviewed while the adapter is still
        // on DHCP, so the service correctly reports observe_only + WouldChange.
        // Neither path is allowed to enable after a mutation attempt.
        return status.Code switch
        {
            "already_matched" => !status.WouldChange,
            "observe_only" => true,
            _ => false
        };
    }
}
