namespace WifiProfileSwitcher.Core;

/// <summary>
/// Serial, in-memory guard against repeated network writes.
/// The host owns serialization and should create a new instance when the configuration changes.
/// </summary>
public sealed class StateEngine
{
    public const int RequiredStableObservations = 2;
    public const int FailureLimit = 5;
    public const int WriteAttemptLimit = 5;
    public static readonly TimeSpan SettleInterval = TimeSpan.FromSeconds(30);

    private string? observedSsid;
    private string? lastKnownSsid;
    private int stableObservations;
    private NetworkProfile? pendingTarget;
    private string? attemptedTargetId;
    private string? settledTargetId;
    private string? ineffectiveTargetId;
    private int consecutiveFailures;
    private DateTimeOffset? lastAttemptAt;
    private DateTimeOffset? lastSuccessAt;
    private DateTimeOffset? lastSettledAt;
    private int writeAttempts;

    public int ConsecutiveFailures => consecutiveFailures;
    public int StableObservations => stableObservations;
    public DateTimeOffset? LastAttemptAt => lastAttemptAt;
    public DateTimeOffset? LastSuccessAt => lastSuccessAt;
    public int WriteAttempts => writeAttempts;

    /// <summary>
    /// Evaluates the current observation. This method never changes the adapter itself.
    /// A null SSID is treated as unknown and cannot select the DHCP fallback.
    /// </summary>
    public Decision Evaluate(
        SwitcherConfig config,
        Guid adapterId,
        string? ssid,
        NetworkSnapshot? snapshot,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.AdapterId == Guid.Empty || adapterId == Guid.Empty || config.AdapterId != adapterId)
        {
            pendingTarget = null;
            return new Decision(StateDecisionCode.AdapterMismatch, null, false);
        }

        if (ConfigValidator.Validate(config).Count != 0)
        {
            pendingTarget = null;
            return new Decision(StateDecisionCode.ConfigInvalid, null, false);
        }

        if (string.IsNullOrEmpty(ssid))
        {
            observedSsid = null;
            stableObservations = 0;
            pendingTarget = null;
            return new Decision(StateDecisionCode.SsidUnavailable, null, false);
        }

        if (lastKnownSsid is not null && !string.Equals(lastKnownSsid, ssid, StringComparison.Ordinal))
        {
            ResetForSsidChange();
        }

        lastKnownSsid = ssid;

        if (string.Equals(observedSsid, ssid, StringComparison.Ordinal))
        {
            stableObservations = Math.Min(RequiredStableObservations, stableObservations + 1);
        }
        else
        {
            observedSsid = ssid;
            stableObservations = 1;
        }

        var target = Policy.TargetFor(config, ssid);
        pendingTarget = target;
        if (target is null)
        {
            return new Decision(StateDecisionCode.NoTarget, null, false);
        }

        if (snapshot is not null && Policy.Matches(target, snapshot))
        {
            RecordMatched(target.Id, now);
            return new Decision(StateDecisionCode.AlreadyMatched, target, false);
        }

        // A read failure is not evidence that the target differs. Holding here also
        // prevents a write from racing a reconnect while the adapter is between states.
        if (snapshot is null)
        {
            return new Decision(StateDecisionCode.NetworkStateUnavailable, target, false);
        }

        if (stableObservations < RequiredStableObservations)
        {
            return new Decision(StateDecisionCode.Stabilizing, target, false);
        }

        if (config.Mode == "observe")
        {
            return new Decision(StateDecisionCode.ObserveOnly, target, false);
        }

        if (writeAttempts >= WriteAttemptLimit)
        {
            return new Decision(StateDecisionCode.FailureLimit, target, false);
        }

        if (ineffectiveTargetId == target.Id)
        {
            return new Decision(StateDecisionCode.Ineffective, target, false);
        }

        if (attemptedTargetId == target.Id)
        {
            if (consecutiveFailures >= FailureLimit)
            {
                return new Decision(StateDecisionCode.FailureLimit, target, false);
            }

            if (consecutiveFailures > 0 && lastAttemptAt is not null)
            {
                var retryAt = lastAttemptAt.Value + BackoffInterval(consecutiveFailures);
                if (now < retryAt)
                {
                    return new Decision(StateDecisionCode.Backoff, target, false);
                }
            }

            if (lastSuccessAt is not null)
            {
                if (now < lastSuccessAt.Value + SettleInterval)
                {
                    return new Decision(StateDecisionCode.Settling, target, false);
                }

                // A successful command whose target was never observed is treated as
                // ineffective. Repeating the same command can otherwise fight policy
                // agents that own the adapter and create an endless write loop.
                ineffectiveTargetId = target.Id;
                return new Decision(StateDecisionCode.Ineffective, target, false);
            }

            if (consecutiveFailures == 0 && lastAttemptAt is not null)
            {
                if (now < lastAttemptAt.Value + SettleInterval)
                {
                    return new Decision(StateDecisionCode.Settling, target, false);
                }

                // The runner did not report success or failure. Treat that as an
                // unresolved write after the settling window, and stop until a new
                // SSID/configuration or a matching snapshot gives us fresh evidence.
                ineffectiveTargetId = target.Id;
                return new Decision(StateDecisionCode.Ineffective, target, false);
            }

            if (settledTargetId == target.Id && lastSettledAt is not null &&
                now < lastSettledAt.Value + SettleInterval)
            {
                return new Decision(StateDecisionCode.Settling, target, false);
            }
        }

        return new Decision(StateDecisionCode.Apply, target, true);
    }

    /// <summary>
    /// Records that the host started applying the most recent decision target.
    /// </summary>
    public void RecordAttempt(DateTimeOffset now)
    {
        if (pendingTarget is null)
        {
            return;
        }

        PrepareForTarget(pendingTarget.Id);
        if (writeAttempts >= WriteAttemptLimit)
        {
            return;
        }

        writeAttempts++;
        attemptedTargetId = pendingTarget.Id;
        lastAttemptAt = now;
        lastSuccessAt = null;
        lastSettledAt = null;
        settledTargetId = null;
        ineffectiveTargetId = null;
    }

    /// <summary>
    /// Records a successful command. The host must call Evaluate again after the
    /// settling interval and only a matching snapshot counts as a completed switch.
    /// </summary>
    public void RecordSuccess(DateTimeOffset now)
    {
        if (pendingTarget is null)
        {
            return;
        }

        PrepareForTarget(pendingTarget.Id);
        attemptedTargetId = pendingTarget.Id;
        consecutiveFailures = 0;
        lastAttemptAt = now;
        lastSuccessAt = now;
        lastSettledAt = null;
        settledTargetId = null;
        ineffectiveTargetId = null;
    }

    /// <summary>
    /// Records a failed command. Failure count and backoff are capped so a denied
    /// administrator operation cannot cause an unbounded retry loop.
    /// </summary>
    public void RecordFailure(DateTimeOffset now)
    {
        if (pendingTarget is null)
        {
            return;
        }

        PrepareForTarget(pendingTarget.Id);
        attemptedTargetId = pendingTarget.Id;
        consecutiveFailures = Math.Min(FailureLimit, consecutiveFailures + 1);
        lastAttemptAt = now;
        lastSuccessAt = null;
        lastSettledAt = null;
        settledTargetId = null;
        ineffectiveTargetId = null;
    }

    public static TimeSpan BackoffInterval(int failures)
    {
        return failures switch
        {
            <= 0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(10),
            2 => TimeSpan.FromSeconds(30),
            3 => TimeSpan.FromSeconds(90),
            _ => TimeSpan.FromSeconds(270)
        };
    }

    private void RecordMatched(string targetId, DateTimeOffset now)
    {
        PrepareForTarget(targetId);
        attemptedTargetId = targetId;
        consecutiveFailures = 0;
        lastAttemptAt = null;
        lastSuccessAt = null;
        lastSettledAt = now;
        settledTargetId = targetId;
        ineffectiveTargetId = null;
    }

    private void PrepareForTarget(string targetId)
    {
        if (attemptedTargetId == targetId)
        {
            return;
        }

        attemptedTargetId = targetId;
        consecutiveFailures = 0;
        writeAttempts = 0;
        lastAttemptAt = null;
        lastSuccessAt = null;
        lastSettledAt = null;
        settledTargetId = null;
        ineffectiveTargetId = null;
    }

    private void ResetForSsidChange()
    {
        observedSsid = null;
        stableObservations = 0;
        pendingTarget = null;
        attemptedTargetId = null;
        settledTargetId = null;
        ineffectiveTargetId = null;
        consecutiveFailures = 0;
        lastAttemptAt = null;
        lastSuccessAt = null;
        lastSettledAt = null;
        writeAttempts = 0;
    }
}
