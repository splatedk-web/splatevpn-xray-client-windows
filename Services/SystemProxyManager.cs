using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using Microsoft.Win32;

namespace SplateClientDesktop.Services;

public static class SystemProxyManager
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static readonly object Gate = new();
    private static readonly string SnapshotPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplateVPN", "proxy-restore.json");
    private record SavedValue(string Name, bool Exists, RegistryValueKind Kind, string? Text, int Number);
    private record Snapshot(string OwnedServer, List<SavedValue> Values);
    private static Snapshot? _snapshot;
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);

    static SystemProxyManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisableProxy();
    }

    public static void RecoverStaleProxy()
    {
        lock (Gate)
        {
            if (_snapshot != null || !File.Exists(SnapshotPath)) return;
            try { _snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SnapshotPath)); DisableProxy(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Proxy recovery: " + ex.Message); }
        }
    }

    public static void EnableProxy(int httpPort = 10809, int socksPort = 10808)
    {
        lock (Gate)
        {
            RecoverStaleProxy();
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true)
                ?? throw new InvalidOperationException("Нет доступа к настройкам системного прокси Windows.");
            var server = $"http=127.0.0.1:{httpPort};https=127.0.0.1:{httpPort}";
            if (_snapshot == null)
            {
                var values = new List<SavedValue>();
                foreach (var name in new[] { "ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL" })
                {
                    var val = key.GetValue(name);
                    var kind = val == null ? RegistryValueKind.String : key.GetValueKind(name);
                    values.Add(new SavedValue(name, val != null, kind, val as string, val is int n ? n : 0));
                }
                _snapshot = new Snapshot(server, values);
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
                File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(_snapshot));
            }
            try
            {
                key.SetValue("ProxyServer", server, RegistryValueKind.String);
                key.SetValue("ProxyOverride", "<local>;localhost;127.*;10.*;192.168.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*", RegistryValueKind.String);
                key.DeleteValue("AutoConfigURL", false);
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                Refresh();
            }
            catch { DisableProxy(); throw; }
        }
    }

    public static void DisableProxy()
    {
        lock (Gate)
        {
            if (_snapshot == null) return;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true)
                    ?? throw new InvalidOperationException("Нет доступа к настройкам прокси.");
                // A user or another VPN may have changed the proxy since we connected.
                if (string.Equals(key.GetValue("ProxyServer") as string, _snapshot.OwnedServer, StringComparison.Ordinal))
                {
                    foreach (var val in _snapshot.Values)
                    {
                        if (!val.Exists) key.DeleteValue(val.Name, false);
                        else key.SetValue(val.Name, val.Kind == RegistryValueKind.DWord ? (object)val.Number : val.Text ?? "", val.Kind);
                    }
                    Refresh();
                }
                File.Delete(SnapshotPath);
                _snapshot = null;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Proxy restore: " + ex.Message); }
        }
    }

    public static bool IsProxyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey);
        return key?.GetValue("ProxyEnable") is int n && n == 1;
    }

    private static void Refresh()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }
}
