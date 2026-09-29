using System.Security.AccessControl;
using System.Security.Principal;

namespace WifiProfileSwitcher.Windows;

internal sealed class SafeException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal static class Safety
{
    public const string ServiceName = "WifiProfileSwitcher";
    public static readonly string InstallDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ServiceName);
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ServiceName);
    public static readonly string ConfigPath = Path.Combine(DataDirectory, "config.json");
    public static readonly string StatusPath = Path.Combine(DataDirectory, "status.json");
    public static readonly string BackupPath = Path.Combine(DataDirectory, "snapshot.json");
    public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static void RequireAdministrator()
    {
        if (!IsAdministrator) throw new SafeException("administrator_required");
    }

    public static void NoReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new SafeException("unsafe_reparse_path");
        }
    }

    public static void ProtectedPath(string path)
    {
        NoReparsePoints(path);
        FileSystemSecurity acl = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !Trusted(owner)) throw new SafeException("unsafe_file_owner");
        const FileSystemRights mutation = FileSystemRights.Write |
            FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & mutation) != 0 && !Trusted((SecurityIdentifier)rule.IdentityReference))
                throw new SafeException("unsafe_write_permissions");
    }

    private static bool Trusted(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);

    public static void RequireInstalled()
    {
        RequireAdministrator();
        if (!string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), InstallDirectory, StringComparison.OrdinalIgnoreCase))
            throw new SafeException("protected_installation_required");
        ProtectedPath(InstallDirectory);
        ProtectedPath(DataDirectory);
        ProtectedPath(ConfigPath);
        ProtectedPath(Path.Combine(InstallDirectory, "WifiProfileSwitcher.exe"));
        ProtectedPath(Path.Combine(InstallDirectory, "scripts"));
        ProtectedPath(Path.Combine(InstallDirectory, "scripts", "network.ps1"));
    }
}
