using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class StateEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequiresTwoConsecutiveKnownSsidObservations()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = TestData.MismatchedSnapshot();

        var first = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0);
        var second = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(5));

        Assert.False(first.ShouldApply);
        Assert.Equal(StateDecisionCode.Stabilizing, first.Code);
        Assert.True(second.ShouldApply);
        Assert.Equal(StateDecisionCode.Apply, second.Code);
        Assert.Equal("internal", second.Target?.Id);
    }

    [Fact]
    public void ObserveModeReturnsTargetButNeverApplies()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "observe");
        var snapshot = TestData.MismatchedSnapshot();

        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0);
        var decision = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(5));

        Assert.False(decision.ShouldApply);
        Assert.Equal(StateDecisionCode.ObserveOnly, decision.Code);
        Assert.Equal("internal", decision.Target?.Id);
    }

    [Fact]
    public void UnknownSsidUsesFallbackAfterStabilization()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = TestData.MismatchedSnapshot();

        _ = engine.Evaluate(config, config.AdapterId, "Example-Guest", snapshot, T0);
        var decision = engine.Evaluate(config, config.AdapterId, "Example-Guest", snapshot, T0.AddSeconds(5));

        Assert.True(decision.ShouldApply);
        Assert.Equal(StateDecisionCode.Apply, decision.Code);
        Assert.Equal("fallback-dhcp", decision.Target?.Id);
    }

    [Fact]
    public void UnknownConnectionDoesNotResetFailureBackoff()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = TestData.MismatchedSnapshot();

        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0);
        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(5));
        engine.RecordAttempt(T0.AddSeconds(5));
        engine.RecordFailure(T0.AddSeconds(5));

        var unavailable = engine.Evaluate(config, config.AdapterId, null, null, T0.AddSeconds(6));
        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(10));
        var retry = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(12));

        Assert.Equal(StateDecisionCode.SsidUnavailable, unavailable.Code);
        Assert.Equal(StateDecisionCode.Backoff, retry.Code);
        Assert.False(retry.ShouldApply);
    }

    [Fact]
    public void FailureBackoffGrowsAndStopsAtFiveFailures()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = TestData.MismatchedSnapshot();
        var now = T0;

        for (var failure = 1; failure <= 5; failure++)
        {
            _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, now);
            _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, now.AddSeconds(5));
            engine.RecordAttempt(now.AddSeconds(5));
            engine.RecordFailure(now.AddSeconds(5));
            now = now.AddSeconds(5 + (failure == 1 ? 10 : failure == 2 ? 30 : failure == 3 ? 90 : 270));
        }

        var decision = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, now.AddSeconds(1));

        Assert.Equal(StateDecisionCode.FailureLimit, decision.Code);
        Assert.False(decision.ShouldApply);
        Assert.Equal(5, engine.ConsecutiveFailures);
    }

    [Fact]
    public void SuccessWaitsForSettlingThenStopsAsIneffectiveIfSnapshotNeverMatches()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = TestData.MismatchedSnapshot();

        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0);
        var apply = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(5));
        Assert.True(apply.ShouldApply);
        engine.RecordAttempt(T0.AddSeconds(5));
        engine.RecordSuccess(T0.AddSeconds(5));

        var settling = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(10));
        var ineffective = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.Add(StateEngine.SettleInterval).AddSeconds(6));

        Assert.Equal(StateDecisionCode.Settling, settling.Code);
        Assert.Equal(StateDecisionCode.Ineffective, ineffective.Code);
        Assert.False(ineffective.ShouldApply);
    }

    [Fact]
    public void MatchingSnapshotClearsFailureAndAllowsFutureDriftRecovery()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = TestData.MismatchedSnapshot();
        var matching = TestData.StaticSnapshot();

        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0);
        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot, T0.AddSeconds(5));
        engine.RecordAttempt(T0.AddSeconds(5));
        engine.RecordFailure(T0.AddSeconds(5));

        var settled = engine.Evaluate(config, config.AdapterId, "Example-Internal", matching, T0.AddSeconds(6));
        var drifted = engine.Evaluate(config, config.AdapterId, "Example-Internal", snapshot with { Addresses = ["192.0.2.11"] }, T0.AddSeconds(40));

        Assert.Equal(StateDecisionCode.AlreadyMatched, settled.Code);
        Assert.Equal(0, engine.ConsecutiveFailures);
        Assert.True(drifted.ShouldApply);
        Assert.Equal(StateDecisionCode.Apply, drifted.Code);
    }

    [Fact]
    public void RejectsWrongAdapterBeforeChangingState()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");

        var decision = engine.Evaluate(config, Guid.NewGuid(), "Example-Internal", null, T0);

        Assert.Equal(StateDecisionCode.AdapterMismatch, decision.Code);
        Assert.False(decision.ShouldApply);
        Assert.Null(decision.Target);
    }

    [Fact]
    public void MissingNetworkSnapshotHoldsBeforeAnyWrite()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");

        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", null, T0);
        var decision = engine.Evaluate(config, config.AdapterId, "Example-Internal", null, T0.AddSeconds(5));

        Assert.Equal(StateDecisionCode.NetworkStateUnavailable, decision.Code);
        Assert.Equal("internal", decision.Target?.Id);
        Assert.False(decision.ShouldApply);
    }

    [Fact]
    public void DriftWritesAreCappedForOneKnownSsid()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var matching = TestData.StaticSnapshot();
        var drifted = TestData.MismatchedSnapshot();

        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", matching, T0);
        _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", matching, T0.AddSeconds(5));

        for (var attempt = 0; attempt < StateEngine.WriteAttemptLimit; attempt++)
        {
            var at = T0.AddSeconds(40 + attempt * 100);
            var decision = engine.Evaluate(config, config.AdapterId, "Example-Internal", drifted, at);
            Assert.True(decision.ShouldApply);
            engine.RecordAttempt(at);
            _ = engine.Evaluate(config, config.AdapterId, "Example-Internal", matching, at.AddSeconds(1));
        }

        var capped = engine.Evaluate(config, config.AdapterId, "Example-Internal", drifted, T0.AddSeconds(600));

        Assert.Equal(StateDecisionCode.FailureLimit, capped.Code);
        Assert.False(capped.ShouldApply);
        Assert.Equal(StateEngine.WriteAttemptLimit, engine.WriteAttempts);
    }

    [Fact]
    public void ARealSsidChangeResetsFailureAndWriteBudget()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = TestData.MismatchedSnapshot();

        _ = engine.Evaluate(config, config.AdapterId, "Example-Guest-A", snapshot, T0);
        _ = engine.Evaluate(config, config.AdapterId, "Example-Guest-A", snapshot, T0.AddSeconds(5));
        engine.RecordAttempt(T0.AddSeconds(5));
        engine.RecordFailure(T0.AddSeconds(5));

        _ = engine.Evaluate(config, config.AdapterId, "Example-Guest-B", snapshot, T0.AddSeconds(6));
        var decision = engine.Evaluate(config, config.AdapterId, "Example-Guest-B", snapshot, T0.AddSeconds(7));

        Assert.Equal(StateDecisionCode.Apply, decision.Code);
        Assert.Equal(0, engine.ConsecutiveFailures);
        engine.RecordAttempt(T0.AddSeconds(7));
        Assert.Equal(1, engine.WriteAttempts);
    }

    [Fact]
    public void MatchingDhcpSnapshotAvoidsApplyForFallback()
    {
        var engine = new StateEngine();
        var config = TestData.ValidConfig(mode: "enforce");
        var snapshot = new NetworkSnapshot
        {
            DhcpEnabled = true,
            Addresses = ["192.0.2.10"],
            DnsAutomatic = true
        };

        _ = engine.Evaluate(config, config.AdapterId, "Example-Guest", snapshot, T0);
        var decision = engine.Evaluate(config, config.AdapterId, "Example-Guest", snapshot, T0.AddSeconds(5));

        Assert.Equal(StateDecisionCode.AlreadyMatched, decision.Code);
        Assert.False(decision.ShouldApply);
    }
}
