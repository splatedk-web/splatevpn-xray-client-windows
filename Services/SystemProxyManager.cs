using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SplateClientDesktop.Services;

public static class SystemProxyManager
{
    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    static SystemProxyManager()
    {
        // Safety failsafe: disable proxy if application crashes or closes unexpectedly
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisableProxy();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => DisableProxy();
    }

    public static void EnableProxy(int httpPort = 10809)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", $"127.0.0.1:{httpPort}", RegistryValueKind.String);
                key.SetValue("ProxyOverride", "<local>;localhost;127.*;10.*;192.168.*;172.16.*", RegistryValueKind.String);
            }
            RefreshWinInet();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enabling proxy: {ex.Message}");
        }
    }

    public static void DisableProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }
            RefreshWinInet();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disabling proxy: {ex.Message}");
        }
    }

    public static bool IsProxyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false);
            if (key != null)
            {
                var val = key.GetValue("ProxyEnable");
                return val is int i && i == 1;
            }
        }
        catch { }
        return false;
    }

    private static void RefreshWinInet()
    {
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }
}