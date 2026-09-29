using System.Diagnostics;
using System.ServiceProcess;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using WifiProfileSwitcher.Core;
using WifiProfileSwitcher.Windows;

namespace WifiProfileSwitcher.Setup;

internal sealed class SetupOperations(SetupPayload payload)
{
    private NetworkBackend Backend => new(Path.Combine(payload.Package, "scripts", "network.ps1"));
    public static bool Installed => Directory.Exists(Safety.InstallDirectory) || Directory.Exists(Safety.DataDirectory);

    /// <summary>
    /// Lists WLAN interfaces without asking Windows for the connected SSID. This is
    /// deliberately separate from Capture: a location/SSID permission failure must
    /// still leave the user with a safe adapter choice for manual setup.
    /// </summary>
    public IReadOnlyList<WifiAdapter> GetAdapters()
    {
        SafeException? wlanFailure = null;
        try
        {
            var wlan = WifiReader.Adapters()
                .Where(adapter => adapter.Id != Guid.Empty)
                .ToArray();
            if (wlan.Length != 0) return wlan;
        }
        catch (SafeException error)
        {
            wlanFailure = error;
            // The SSID-capable WLAN API can be unavailable while the ordinary
            // adapter inventory still works. Manual setup can proceed from this
            // inventory; ReadAdapterSettings performs the authoritative physical
            // Wi-Fi check before anything is saved.
        }

        var fallback = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .Select(network => Guid.TryParse(network.Id, out var id)
                ? new WifiAdapter(id, network.Description, network.OperationalStatus == OperationalStatus.Up ? 1 : 0)
                : null)
            .Where(adapter => adapter is not null && adapter.Id != Guid.Empty)
            .Cast<WifiAdapter>()
            .ToArray();
        if (fallback.Length != 0) return fallback;
        throw wlanFailure ?? new SafeException("wifi_enumeration_failed");
    }

    /// <summary>
    /// Reads IPv4/DNS state only. The backend validates that the GUID maps to a
    /// physical Wi-Fi adapter; it never changes the adapter in this operation.
    /// </summary>
    public async Task<NetworkSnapshot> ReadAdapterSettings(Guid adapterId, CancellationToken token = default)
    {
        EnsureListedAdapter(adapterId);
        return await Backend.Read(adapterId, token);
    }

    public async Task<SwitcherConfig> Capture()
    {
        var adapters = WifiReader.Adapters().Where(a => a.State == 1).ToArray();
        if (adapters.Length != 1) throw new SafeException(adapters.Length == 0 ? "wifi_disconnected" : "multiple_connected_wifi");
        var id = adapters[0].Id;
        var before = WifiReader.Read(id);
        if (before.Ssid is null) throw new SafeException(before.Code);
        var snapshot = await Backend.Capture(id, CancellationToken.None);
        var after = WifiReader.Read(id);
        if (after.Ssid != before.Ssid) throw new SafeException("connection_changed");
        return InitialProfileCapture.Create(id, before.Ssid, snapshot);
    }

    public async Task RequireUnchanged(SwitcherConfig expected)
    {
        var current = await Capture();
        var observed = current.Profiles.Single();
        var reviewed = expected.Profiles.SingleOrDefault(p => p.Ssids.Contains(observed.Ssids.Single(), StringComparer.Ordinal));
        if (current.AdapterId != expected.AdapterId || reviewed is null || reviewed.Mode != "static" ||
            reviewed.Address != observed.Address || reviewed.PrefixLength != observed.PrefixLength ||
            reviewed.Gateway != observed.Gateway || !reviewed.DnsServers.SequenceEqual(observed.DnsServers))
            throw new SafeException("connection_changed");
    }

    public static SwitcherConfig LoadInstalled()
    {
        Safety.ProtectedPath(Safety.InstallDirectory);
        Safety.ProtectedPath(Safety.DataDirectory);
        Safety.ProtectedPath(Safety.ConfigPath);
        if (new FileInfo(Safety.ConfigPath).Length > 65536) throw new SafeException("config_invalid");
        return ConfigCodec.Load(File.ReadAllText(Safety.ConfigPath));
    }

    public async Task Install(SwitcherConfig config, bool manualEntry = false)
    {
        if (Installed) throw new SafeException("already_installed");
        var observeConfig = config with { Mode = "observe" };
        _ = ConfigCodec.Serialize(observeConfig);
        if (manualEntry)
        {
            // Manual entry intentionally does not require SSID capture or a
            // currently matching static address. A read-only backend check still
            // prevents selecting a non-physical or unknown adapter GUID.
            await ReadAdapterSettings(observeConfig.AdapterId, CancellationToken.None);
        }
        else
        {
            await RequireUnchanged(observeConfig);
        }

        File.WriteAllText(payload.Config, ConfigCodec.Serialize(observeConfig), new UTF8Encoding(false));
        await Script(Path.Combine(payload.Package, "scripts", "install.ps1"),
            "-ConfigPath", payload.Config, "-PackagePath", payload.Package, "-GuiConfirmed");
        await Service("Start");
    }

    /// <summary>
    /// Saves an observe-only profile on an existing installation. Stopping the
    /// service first prevents an old enforce config from racing this replacement.
    /// A failed save leaves the service stopped so the previous profile cannot
    /// silently continue changing the adapter.
    /// </summary>
    public async Task Save(SwitcherConfig config)
    {
        var observeConfig = config with { Mode = "observe" };
        _ = ConfigCodec.Serialize(observeConfig);
        await Service("Stop");
        var installed = LoadInstalled();
        if (installed.AdapterId != observeConfig.AdapterId)
            throw new SafeException("adapter_change_not_supported");
        await ReadAdapterSettings(observeConfig.AdapterId, CancellationToken.None);
        File.WriteAllText(payload.Config, ConfigCodec.Serialize(observeConfig), new UTF8Encoding(false));
        await Script(Path.Combine(payload.Package, "scripts", "configure.ps1"),
            "-ConfigPath", payload.Config, "-GuiConfirmed");
        await Service("Start");
    }

    public async Task VerifyService()
    {
        await Service("Stop");
        if (LoadInstalled().Mode != "observe") throw new SafeException("config_changed");
        var started = DateTimeOffset.UtcNow;
        await Service("Start");
        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(2000);
            var status = ReadStatus();
            if (SetupReadiness.CanEnable(status, started, DateTimeOffset.UtcNow))
                return;
        }
        throw new SafeException("service_observation_failed");
    }

    public static bool? ServiceIsRunning()
    {
        try
        {
            using var service = new ServiceController("WifiProfileSwitcher");
            return service.Status == ServiceControllerStatus.Running;
        }
        catch { return null; }
    }

    public static ServiceObservation? ReadStatus()
    {
        if (!File.Exists(Safety.StatusPath)) return null;
        Safety.ProtectedPath(Safety.StatusPath);
        if (new FileInfo(Safety.StatusPath).Length > 8192) throw new SafeException("status_invalid");
        try { return JsonSerializer.Deserialize<ServiceObservation>(File.ReadAllText(Safety.StatusPath), NetworkBackend.Json); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task Enable(SwitcherConfig reviewed, bool manualEntry = true)
    {
        var installed = LoadInstalled();
        if (installed.Mode != "observe" || ConfigCodec.Serialize(installed with { Mode = "observe" }) != ConfigCodec.Serialize(reviewed with { Mode = "observe" }))
            throw new SafeException("config_changed");

        if (manualEntry)
            await RequireConfiguredSsid(reviewed);
        else
            await RequireUnchanged(reviewed);

        await VerifyService();
        if (manualEntry)
            await RequireConfiguredSsid(reviewed);
        else
            await RequireUnchanged(reviewed);

        File.WriteAllText(payload.Config, ConfigCodec.Serialize(reviewed with { Mode = "observe" }), new UTF8Encoding(false));
        await Script(Path.Combine(payload.Package, "scripts", "configure.ps1"),
            "-ConfigPath", payload.Config, "-Enable", "-GuiConfirmed");
        await Service("Start");
    }

    /// <summary>
    /// Confirms the selected adapter is associated with one of the explicitly
    /// configured SSIDs. Its current IPv4 mode may be DHCP: manual setup exists
    /// precisely for that case. Both SSID reads must agree and the backend read is
    /// kept read-only, so an association change cannot authorize a write.
    /// </summary>
    private async Task RequireConfiguredSsid(SwitcherConfig expected)
    {
        var before = WifiReader.Read(expected.AdapterId);
        if (before.Ssid is null) throw new SafeException(before.Code);
        if (!expected.Profiles.Any(profile => profile.Ssids.Contains(before.Ssid, StringComparer.Ordinal)))
            throw new SafeException("ssid_not_configured");

        await ReadAdapterSettings(expected.AdapterId, CancellationToken.None);
        var after = WifiReader.Read(expected.AdapterId);
        if (after.Ssid is null || !string.Equals(after.Ssid, before.Ssid, StringComparison.Ordinal))
            throw new SafeException("connection_changed");
    }

    private void EnsureListedAdapter(Guid adapterId)
    {
        if (adapterId == Guid.Empty || !GetAdapters().Any(adapter => adapter.Id == adapterId))
            throw new SafeException("adapter_missing");
    }

    public Task Service(string action) => Script(Path.Combine(payload.Package, "scripts", "service-control.ps1"),
        "-Action", action, "-GuiConfirmed");

    public async Task Pause()
    {
        await Service("Stop");
        var config = LoadInstalled();
        File.WriteAllText(payload.Config, ConfigCodec.Serialize(config with { Mode = "observe" }), new UTF8Encoding(false));
        await Script(Path.Combine(payload.Package, "scripts", "configure.ps1"), "-ConfigPath", payload.Config, "-GuiConfirmed");
        await Service("Stop");
    }

    public async Task Uninstall()
    {
        // Run the protected payload copy so removal never deletes the script currently being read.
        await Script(Path.Combine(payload.Package, "scripts", "uninstall.ps1"), "-GuiConfirmed");
    }

    public async Task Recover(bool restore)
    {
        await Pause();
        await Execute(Path.Combine(Safety.InstallDirectory, "WifiProfileSwitcher.exe"),
            [restore ? "restore" : "recover-dhcp", "--confirm"], "RECOVER\n");
    }

    private static Task Script(string script, params string[] args)
    {
        Safety.ProtectedPath(script);
        return Execute(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", script, ..args]);
    }

    private static async Task Execute(string executable, string[] arguments, string? input = null)
    {
        Safety.NoReparsePoints(executable);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new SafeException("setup_command_failed");
        var stdout = Drain(process.StandardOutput);
        var stderr = Drain(process.StandardError);
        if (input is not null) await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        // Never terminate installation halfway through ACL changes or a configuration replacement.
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new SafeException("setup_command_failed");
    }

    private static async Task Drain(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) != 0) { }
    }
}
