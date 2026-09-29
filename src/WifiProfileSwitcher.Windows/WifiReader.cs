using System.Runtime.InteropServices;
using Windows.Networking.Connectivity;

namespace WifiProfileSwitcher.Windows;

internal sealed record WifiAdapter(Guid Id, string Description, int State);
internal sealed record WifiReading(string Code, string? Ssid);

internal static class WifiReader
{
    // Enumeration needs no BSSID scan. Description is displayed only by the local adapters command.
    public static IReadOnlyList<WifiAdapter> Adapters()
    {
        var error = WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        if (error != 0) throw new SafeException(error == 5 ? "wifi_access_denied" : "wlan_service_unavailable");
        try
        {
            error = WlanEnumInterfaces(handle, IntPtr.Zero, out var list);
            if (error != 0) throw new SafeException("wifi_enumeration_failed");
            try
            {
                var count = Marshal.ReadInt32(list);
                if (count < 0 || count > 256) throw new SafeException("wifi_enumeration_failed");
                var result = new List<WifiAdapter>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<WlanInterface>(IntPtr.Add(list, 8 + i * Marshal.SizeOf<WlanInterface>()));
                    result.Add(new(item.Id, item.Description, item.State));
                }
                return result;
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    public static WifiReading Read(Guid adapterId)
    {
        try
        {
            var adapter = Adapters().SingleOrDefault(a => a.Id == adapterId);
            if (adapter is null) return new("adapter_missing", null);
            if (adapter.State != 1) return new("wifi_disconnected", null);
            // Do not use GetInternetConnectionProfile: a static-IP mismatch can mean no Internet yet.
            var profiles = NetworkInformation.GetConnectionProfiles()
                .Where(p => p.IsWlanConnectionProfile && p.NetworkAdapter?.NetworkAdapterId == adapterId)
                .ToArray();
            if (profiles.Length != 1) return new("ssid_unavailable", null);
            var ssid = profiles[0].WlanConnectionProfileDetails.GetConnectedSsid();
            return string.IsNullOrEmpty(ssid) ? new("ssid_unavailable", null) : new("connected", ssid);
        }
        catch (SafeException ex) { return new(ex.Code, null); }
        catch (UnauthorizedAccessException) { return new("location_or_policy_denied", null); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
        { return new("location_or_policy_denied", null); }
        catch { return new("ssid_api_unavailable", null); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterface
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public int State;
    }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
}
