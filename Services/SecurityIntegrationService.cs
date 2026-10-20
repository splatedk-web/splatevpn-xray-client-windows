using System.Diagnostics;

namespace SplateClientDesktop.Services;

public static class SecurityIntegrationService
{
    public static bool EnableKillSwitch(string xrayPath, int httpPort, int socksPort)
        => throw new NotSupportedException("Kill Switch требует отдельного WFP-фильтра. Эта сборка не поддерживает его.");

    public static void DisableKillSwitch()
    {
        // Remove legacy rules by exact name only. Never modify the Windows firewall default policy.
        if (!TunDeviceManager.IsAdministrator()) return;
        foreach (var name in new[] { "SplateVPN_KillSwitch_BlockOutbound", "SplateVPN_KillSwitch_AllowApp", "SplateVPN_KillSwitch_AllowTun", "SplateVPN_KillSwitch_Loopback" })
        {
            try { TunDeviceManager.RunAsync("netsh.exe", new[] { "advfirewall", "firewall", "delete", "rule", $"name={name}" }).GetAwaiter().GetResult(); }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }
    }

    public static void SetDnsLeakProtection(bool enable)
    {
        if (enable) throw new NotSupportedException("Используйте TUN для перехвата DNS. Изменение глобальной DNS-политики Windows не обеспечивает защиту от утечек.");
    }
}
