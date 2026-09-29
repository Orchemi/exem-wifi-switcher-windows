using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WifiProfileSwitcher.Core;
using WifiProfileSwitcher.Windows;

return await Application.Run(args);

internal static class Application
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new SafeException("windows_required");
            if (args.Length == 0 || args is ["help"] || args is ["--help"])
            {
                Console.WriteLine("Wi-Fi Profile Switcher — Windows prototype\n" +
                    "doctor | adapters | validate --config PATH | observe --config PATH\n" +
                    "watch [--config PATH] | service | status | recover-dhcp --confirm | restore --confirm\n" +
                    "기본 관찰 모드. 실제 변경은 보호된 설치 및 관리자 권한이 필요합니다.\n" +
                    "adapters 출력은 로컬 설정용이며 공유하지 마세요.");
                return 0;
            }
            if (args is ["validate", "--config", var validationPath])
            {
                _ = Load(validationPath);
                Print(new { code = "config_valid" });
                return 0;
            }
            if (args is ["adapters"])
            {
                Console.WriteLine("로컬 설정용 정보 — 공개 이슈나 진단 결과에 붙여넣지 마세요.");
                Print(WifiReader.Adapters());
                return 0;
            }
            if (args is ["doctor"]) return await Doctor();
            if (args is ["status"])
            {
                Safety.RequireAdministrator();
                Safety.ProtectedPath(Safety.DataDirectory);
                Safety.NoReparsePoints(Safety.StatusPath);
                if (!File.Exists(Safety.StatusPath)) throw new SafeException("status_not_available");
                // Fixed schema only, never echo arbitrary content from a local file.
                var status = JsonSerializer.Deserialize<SafeStatus>(await File.ReadAllTextAsync(Safety.StatusPath), NetworkBackend.Json)
                    ?? throw new SafeException("status_not_available");
                Print(status);
                return 0;
            }
            if (args is ["recover-dhcp", "--confirm"] || args is ["restore", "--confirm"])
            {
                Safety.RequireInstalled();
                if (ServiceRunning()) throw new SafeException("stop_service_before_recovery");
                using var gate = InstanceGate.Acquire();
                var config = Load(Safety.ConfigPath);
                Console.WriteLine(args[0] == "restore"
                    ? "최초 백업을 복원합니다. 백업 당시 네트워크에 연결되어 있어야 합니다. 연결이 끊길 수 있습니다."
                    : "지정된 Wi-Fi의 IPv4와 DNS를 자동으로 전환합니다. 고정 IP 전용 망에서는 연결이 끊길 수 있습니다.");
                Console.Write("계속하려면 RECOVER 입력: ");
                if (Console.ReadLine() != "RECOVER") throw new SafeException("cancelled");
                var backend = new NetworkBackend();
                await backend.Restore(config.AdapterId, args[0] == "recover-dhcp", CancellationToken.None);
                var actual = await backend.Read(config.AdapterId, CancellationToken.None);
                Print(new { code = "recovery_command_completed", dhcpEnabled = actual.DhcpEnabled, dnsAutomatic = actual.DnsAutomatic,
                    note = "연결 및 실제 설정은 Windows 네트워크 설정에서 확인하세요. 서비스는 중지 상태로 유지됩니다." });
                return 0;
            }
            if (args is ["observe", "--config", var observePath])
            {
                var config = Load(observePath) with { Mode = "observe" };
                var runner = new SwitchRunner(config, writeStatus: false);
                await runner.Tick(CancellationToken.None);
                return 0;
            }
            if (args is ["service"])
            {
                Safety.RequireInstalled();
                using var gate = InstanceGate.Acquire();
                var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    Args = [], ContentRootPath = Safety.InstallDirectory, DisableDefaults = true
                });
                builder.Services.AddWindowsService(options => options.ServiceName = Safety.ServiceName);
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton(new SwitchRunner(Load(Safety.ConfigPath), writeStatus: true));
                builder.Services.AddHostedService<Worker>();
                using var host = builder.Build();
                await host.RunAsync();
                return 0;
            }
            if (args is ["watch"] || args is ["watch", "--config", _])
            {
                var path = args.Length == 3 ? args[2] : Safety.ConfigPath;
                var config = Load(path);
                if (config.Mode == "enforce")
                {
                    Safety.RequireInstalled();
                    if (!string.Equals(Path.GetFullPath(path), Safety.ConfigPath, StringComparison.OrdinalIgnoreCase))
                        throw new SafeException("installed_config_required");
                }
                using var gate = config.Mode == "enforce" ? InstanceGate.Acquire() : null;
                using var stop = new CancellationTokenSource();
                Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
                await new SwitchRunner(config, writeStatus: false).Run(stop.Token);
                return 0;
            }
            throw new SafeException("invalid_command");
        }
        catch (ConfigException ex) { Print(new { code = ex.Code, errors = ex.Errors }); return 2; }
        catch (SafeException ex) { Print(new { code = ex.Code }); return 3; }
        catch (UnauthorizedAccessException) { Print(new { code = "access_denied" }); return 3; }
        catch (OperationCanceledException) { return 0; }
        catch { Print(new { code = "unexpected_failure" }); return 4; }
    }

    internal static SwitcherConfig Load(string path)
    {
        Safety.NoReparsePoints(path);
        if (!File.Exists(path)) throw new SafeException("config_missing");
        if (new FileInfo(path).Length > 65536) throw new SafeException("config_too_large");
        return ConfigCodec.Load(File.ReadAllText(path));
    }

    internal static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, NetworkBackend.Json));

    private static async Task<int> Doctor()
    {
        var wifiCode = "not_configured";
        var networkCode = "not_configured";
        var adapterCount = 0;
        try { adapterCount = WifiReader.Adapters().Count; }
        catch (SafeException ex) { wifiCode = ex.Code; }
        if (File.Exists(Safety.ConfigPath) && Safety.IsAdministrator)
        {
            try
            {
                var config = Load(Safety.ConfigPath);
                wifiCode = WifiReader.Read(config.AdapterId).Code;
                _ = await new NetworkBackend().Read(config.AdapterId, CancellationToken.None);
                networkCode = "read_ok";
            }
            catch (ConfigException) { networkCode = "config_invalid"; }
            catch (SafeException ex) { networkCode = ex.Code; }
            catch { networkCode = "read_failed"; }
        }
        Print(new
        {
            schemaVersion = 1, prototype = true, osBuild = Environment.OSVersion.Version.Build,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), administrator = Safety.IsAdministrator,
            wifiAdapterCount = adapterCount, wifiCode, networkCode, serviceRunning = ServiceRunning(),
            instructions = "docs/PERMISSIONS.md 참조. 이 출력만 공유하고 adapters/config/snapshot 원문은 공유하지 마세요."
        });
        return 0;
    }

    private static bool ServiceRunning()
    {
        try { using var service = new ServiceController(Safety.ServiceName); return service.Status != ServiceControllerStatus.Stopped; }
        catch (InvalidOperationException) { return false; }
    }
}

internal sealed class InstanceGate : IDisposable
{
    private readonly FileStream handle;
    private InstanceGate(FileStream handle) { this.handle = handle; }
    public static InstanceGate Acquire()
    {
        Safety.ProtectedPath(Safety.DataDirectory);
        var path = Path.Combine(Safety.DataDirectory, "writer.lock");
        Safety.NoReparsePoints(path);
        try { return new(File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException) { throw new SafeException("another_instance_running"); }
    }
    public void Dispose() => handle.Dispose();
}

internal sealed class Worker(SwitchRunner runner) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => runner.Run(stoppingToken);
}
