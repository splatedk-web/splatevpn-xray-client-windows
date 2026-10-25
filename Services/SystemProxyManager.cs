using System.IO;
using System.Runtime.InteropServices;

namespace SplateClientDesktop.Services;

public static class SystemProxyManager
{
    private static readonly ProxySession Session = new(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplateVPN", "proxy-restore.json"), Refresh);
    static SystemProxyManager() { AppDomain.CurrentDomain.ProcessExit += (_, _) => DisableProxy(); }
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);
    public static void RecoverStaleProxy()
    {
        try { Session.Recover(); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine("Proxy recovery: " + error.Message); }
    }
    public static void EnableProxy(int httpPort = 10809, int socksPort = 10808) => Session.Enable(httpPort);
    public static void DisableProxy() => Session.Restore();
    public static ProxyPreflight InspectForTun() => Session.InspectForTun();
    public static void SuspendForTun(ProxyPreflight approved) => Session.SuspendForTun(approved);
    public static bool IsProxyEnabled() => Session.ReadStatus().Manual;
    public static ProxyPreflight ReadStatus() => Session.ReadStatus();
    public static HttpProxyEndpoint OriginalHttpProxy() => Session.OriginalHttpProxy();
    private static void Refresh()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }
}
