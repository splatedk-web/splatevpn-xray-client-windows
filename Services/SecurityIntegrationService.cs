using System;
using System.Diagnostics;

namespace SplateClientDesktop.Services;

public static class SecurityIntegrationService
{
    private const string OutboundRuleName = "SplateVPN_KillSwitch_BlockOutbound";
    private const string AllowRuleName = "SplateVPN_KillSwitch_AllowApp";

    /// <summary>
    /// Enables Kill Switch by configuring Windows Firewall rules:
    /// blocks outbound traffic except for loopback and DNS/Xray process.
    /// </summary>
    public static bool EnableKillSwitch(string xrayPath, int httpPort, int socksPort)
    {
        try
        {
            // Remove existing rules first
            DisableKillSwitch();

            // Allow local loopback
            RunNetsh($"advfirewall firewall add rule name=\"SplateVPN_KillSwitch_Loopback\" dir=out action=allow remoteip=127.0.0.1,::1 protocol=any");

            // Allow xray.exe specifically so it can reach the remote VPN server
            if (!string.IsNullOrEmpty(xrayPath))
            {
                RunNetsh($"advfirewall firewall add rule name=\"{AllowRuleName}\" dir=out action=allow program=\"{xrayPath}\" enable=yes");
                
                var tunPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(xrayPath) ?? "", "tun2socks.exe");
                if (System.IO.File.Exists(tunPath))
                {
                    RunNetsh($"advfirewall firewall add rule name=\"SplateVPN_KillSwitch_AllowTun\" dir=out action=allow program=\"{tunPath}\" enable=yes");
                }
            }

            // Block all other outbound traffic
            RunNetsh($"advfirewall firewall add rule name=\"{OutboundRuleName}\" dir=out action=block profile=any enable=yes");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enable Kill Switch: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disables Kill Switch rules in Windows Firewall.
    /// </summary>
    public static void DisableKillSwitch()
    {
        try
        {
            RunNetsh($"advfirewall firewall delete rule name=\"{OutboundRuleName}\"");
            RunNetsh($"advfirewall firewall delete rule name=\"{AllowRuleName}\"");
            RunNetsh("advfirewall firewall delete rule name=\"SplateVPN_KillSwitch_AllowTun\"");
            RunNetsh("advfirewall firewall delete rule name=\"SplateVPN_KillSwitch_Loopback\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to disable Kill Switch: {ex.Message}");
        }
    }

    /// <summary>
    /// Disables Smart Multi-Homed Name Resolution in Windows (Group Policy / Registry) to prevent DNS leak.
    /// </summary>
    public static void SetDnsLeakProtection(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                @"Software\Policies\Microsoft\Windows NT\DNSClient", true);
            if (key != null)
            {
                if (enable)
                {
                    key.SetValue("DisableSmartNameResolution", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue("DisableSmartNameResolution", false);
                }
            }
        }
        catch
        {
            // Fallback: CurrentUser
            try
            {
                using var cuKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Policies\Microsoft\Windows NT\DNSClient", true);
                if (cuKey != null)
                {
                    if (enable)
                        cuKey.SetValue("DisableSmartNameResolution", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        cuKey.DeleteValue("DisableSmartNameResolution", false);
                }
            }
            catch { }
        }
    }

    private static void RunNetsh(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(3000);
    }
}
