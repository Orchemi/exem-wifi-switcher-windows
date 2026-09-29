using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using WifiProfileSwitcher.Windows;

namespace WifiProfileSwitcher.Setup;

internal sealed class SetupPayload : IDisposable
{
    public string Root { get; }
    public string Package => Path.Combine(Root, "package");
    public string Config => Path.Combine(Root, "captured.json");
    private SetupPayload(string root) { Root = root; }

    public static SetupPayload Open()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup-payload.zip")
            ?? throw new SafeException("setup_payload_missing");
        var parent = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Safety.NoReparsePoints(parent);
        var path = Path.Combine(parent, ".WifiProfileSwitcher-setup-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path)) throw new SafeException("unsafe_path");
        var acl = new DirectorySecurity();
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        acl.SetOwner(admins);
        acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { admins, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(acl);
        var payload = new SetupPayload(path);
        try
        {
            Safety.ProtectedPath(path);
            Directory.CreateDirectory(payload.Package);
            using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
            if (archive.Entries.Count > 4096) throw new SafeException("setup_payload_invalid");
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > 512L * 1024 * 1024 || entry.FullName.Contains(':') || entry.FullName.Contains('\\') ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new SafeException("setup_payload_invalid");
                var target = Path.GetFullPath(Path.Combine(payload.Package, entry.FullName));
                if (!target.StartsWith(payload.Package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new SafeException("setup_payload_invalid");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
            using var notice = Assembly.GetExecutingAssembly().GetManifestResourceStream("desktop-license.txt")
                ?? throw new SafeException("setup_payload_invalid");
            using (var output = new FileStream(Path.Combine(payload.Package, "DESKTOP-LICENSE.txt"), FileMode.CreateNew))
                notice.CopyTo(output);
            Safety.ProtectedPath(Path.Combine(payload.Package, "WifiProfileSwitcher.exe"));
            return payload;
        }
        catch { payload.Dispose(); throw; }
    }

    public void Dispose()
    {
        try
        {
            Safety.ProtectedPath(Root);
            foreach (var item in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                Safety.NoReparsePoints(item);
            Directory.Delete(Root, recursive: true);
        }
        catch { /* A failed cleanup leaves administrator-only data, never a public log. */ }
    }
}
