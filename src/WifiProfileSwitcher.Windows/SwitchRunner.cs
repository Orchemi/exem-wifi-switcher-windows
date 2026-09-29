using System.Text.Json;
using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Windows;

internal sealed record SafeStatus(int SchemaVersion, DateTimeOffset ObservedAt, string Mode, string Code,
    bool Connected, bool TargetFound, bool WouldChange, bool MutationAttempted);

internal sealed class SwitchRunner(SwitcherConfig config, bool writeStatus)
{
    private readonly StateEngine engine = new();
    private readonly NetworkBackend backend = new();
    private readonly string originalConfig = JsonSerializer.Serialize(config, NetworkBackend.Json);

    public async Task Run(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await Tick(stop);
            try { await Task.Delay(TimeSpan.FromSeconds(config.PollSeconds), stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        }
    }

    public async Task Tick(CancellationToken stop)
    {
        var connected = false;
        var targetFound = false;
        var wouldChange = false;
        var attempted = false;
        var code = "starting";
        try
        {
            if (config.Mode == "enforce" || writeStatus)
            {
                Safety.RequireInstalled();
                if (JsonSerializer.Serialize(Application.Load(Safety.ConfigPath), NetworkBackend.Json) != originalConfig)
                    throw new SafeException("config_changed_restart_required");
            }
            var wifi = WifiReader.Read(config.AdapterId);
            connected = wifi.Ssid is not null;
            var snapshot = wifi.Ssid is null ? null : await backend.Read(config.AdapterId, stop);
            var decision = engine.Evaluate(config, config.AdapterId, wifi.Ssid, snapshot, DateTimeOffset.UtcNow);
            targetFound = decision.Target is not null;
            wouldChange = decision.Target is not null && snapshot is not null && !Policy.Matches(decision.Target, snapshot);
            code = wifi.Ssid is null ? wifi.Code : decision.Code;
            if (decision.ShouldApply && decision.Target is not null)
            {
                // Network reads and PowerShell startup can take time. Recheck association before the write.
                var latest = WifiReader.Read(config.AdapterId);
                if (latest.Ssid is null || !string.Equals(wifi.Ssid, latest.Ssid, StringComparison.Ordinal))
                {
                    code = "connection_changed_before_apply";
                    _ = engine.Evaluate(config, config.AdapterId, null, null, DateTimeOffset.UtcNow);
                }
                else
                {
                    Safety.RequireInstalled();
                    engine.RecordAttempt(DateTimeOffset.UtcNow);
                    attempted = true;
                    await backend.Apply(config.AdapterId, decision.Target, stop);
                    engine.RecordSuccess(DateTimeOffset.UtcNow);
                    code = "apply_command_completed_waiting_verification";
                }
            }
        }
        catch (SafeException ex)
        {
            if (attempted) engine.RecordFailure(DateTimeOffset.UtcNow);
            else _ = engine.Evaluate(config, config.AdapterId, null, null, DateTimeOffset.UtcNow);
            code = ex.Code;
        }
        catch (ConfigException) { code = "config_invalid"; }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        catch
        {
            if (attempted) engine.RecordFailure(DateTimeOffset.UtcNow);
            else _ = engine.Evaluate(config, config.AdapterId, null, null, DateTimeOffset.UtcNow);
            code = "observation_failed";
        }

        var status = new SafeStatus(1, DateTimeOffset.UtcNow, config.Mode, code, connected, targetFound, wouldChange, attempted);
        if (!writeStatus) Application.Print(status);
        else
        {
            // A single bounded status file; no raw exception/IP/SSID output or unbounded network history.
            Safety.NoReparsePoints(Safety.StatusPath);
            var temp = Path.Combine(Safety.DataDirectory, "status.tmp");
            Safety.NoReparsePoints(temp);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(status, NetworkBackend.Json), stop);
            File.Move(temp, Safety.StatusPath, overwrite: true);
        }
    }
}
