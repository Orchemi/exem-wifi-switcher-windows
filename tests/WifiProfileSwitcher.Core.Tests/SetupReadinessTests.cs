using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class SetupReadinessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:01:00Z");
    private static ServiceObservation Good => new(1, Now, "observe", "already_matched", true, true, false, false);
    private static ServiceObservation ManualProfileOnDhcp =>
        new(1, Now, "observe", "observe_only", true, true, true, false);

    [Fact]
    public void OnlyFreshMatchingReadOnlyServiceEvidenceEnablesSwitching()
    {
        Assert.True(SetupReadiness.CanEnable(Good, Now.AddSeconds(-10), Now));
        ServiceObservation?[] invalid =
        [
            null, Good with { SchemaVersion = 2 }, Good with { Mode = "enforce" },
            Good with { Code = "apply", WouldChange = true }, Good with { Connected = false },
            Good with { TargetFound = false }, Good with { WouldChange = true },
            Good with { MutationAttempted = true }, Good with { ObservedAt = Now.AddMinutes(-1) },
            Good with { ObservedAt = Now.AddMinutes(1) }
        ];
        foreach (var status in invalid) Assert.False(SetupReadiness.CanEnable(status, Now.AddSeconds(-10), Now));
    }

    [Fact]
    public void ManualProfileMayBeEnabledAfterReadOnlyObservationWhileDhcpIsActive()
    {
        Assert.True(SetupReadiness.CanEnable(ManualProfileOnDhcp, Now.AddSeconds(-10), Now));
        Assert.True(SetupReadiness.CanEnable(ManualProfileOnDhcp with { WouldChange = false }, Now.AddSeconds(-10), Now));
        Assert.False(SetupReadiness.CanEnable(ManualProfileOnDhcp with { MutationAttempted = true }, Now.AddSeconds(-10), Now));
    }

    [Fact]
    public void AlreadyMatchedEvidenceMustNotClaimAChange()
    {
        Assert.False(SetupReadiness.CanEnable(Good with { WouldChange = true }, Now.AddSeconds(-10), Now));
        Assert.False(SetupReadiness.CanEnable(Good with { Code = "apply", WouldChange = true }, Now.AddSeconds(-10), Now));
    }

    [Fact]
    public void EvidenceFromBeforeServiceRestartIsRejected()
    {
        Assert.False(SetupReadiness.CanEnable(Good with { ObservedAt = Now.AddSeconds(-1) }, Now, Now));
    }
}
