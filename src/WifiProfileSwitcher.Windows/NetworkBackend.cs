using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Windows;

internal sealed class NetworkBackend
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<NetworkSnapshot> Read(Guid adapter, CancellationToken token)
    {
        using var reply = await Run(new { operation = "read", adapterId = adapter }, token);
        return reply.RootElement.GetProperty("snapshot").Deserialize<NetworkSnapshot>(Json)
            ?? throw new SafeException("network_state_unavailable");
    }

    public async Task Apply(Guid adapter, NetworkProfile profile, CancellationToken token)
    {
        Safety.RequireInstalled();
        using var reply = await Run(new { operation = "apply", adapterId = adapter, profile }, token);
    }

    public async Task Restore(Guid adapter, bool dhcp, CancellationToken token)
    {
        Safety.RequireInstalled();
        using var reply = await Run(new { operation = dhcp ? "recover" : "restore", adapterId = adapter }, token);
    }

    private static async Task<JsonDocument> Run(object request, CancellationToken token)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "scripts", "network.ps1");
        Safety.NoReparsePoints(script);
        // Absolute Windows PowerShell path. Installation explicitly discloses process-only RemoteSigned.
        // Group Policy still takes precedence; never use Bypass or persist a machine/user policy change.
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", script }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new SafeException("powershell_start_failed");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request, Json).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            _ = await stderr; // OS messages can contain IPs/user paths. Never include them in logs or shared diagnosis.
            if (output.Length > 65536) throw new SafeException("network_reply_invalid");
            JsonDocument document;
            try { document = JsonDocument.Parse(output); }
            catch (JsonException) { throw new SafeException("powershell_blocked_or_invalid_reply"); }
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("ok", out var ok) ||
                ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                document.Dispose();
                throw new SafeException("network_reply_invalid");
            }
            if (process.ExitCode != 0 || !ok.GetBoolean())
            {
                var code = document.RootElement.TryGetProperty("code", out var value) ? value.GetString() : null;
                document.Dispose();
                // Only script-authored constant identifiers may leave this boundary.
                throw new SafeException(code is not null && AllowedCodes.Contains(code) ? code : "network_command_failed");
            }
            return document;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new SafeException("network_command_timeout_or_cancelled");
        }
    }

    private static readonly HashSet<string> AllowedCodes =
    ["adapter_missing", "adapter_not_physical_wifi", "administrator_required", "policy_or_access_denied",
     "complex_network_configuration", "backup_invalid", "backup_missing", "backup_write_failed", "unsafe_path", "invalid_request",
     "network_command_failed", "powershell_language_restricted", "network_modules_unavailable", "address_conflict"];
}
