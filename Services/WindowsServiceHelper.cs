using System;
using System.Diagnostics;
using System.IO;

namespace SplateClientDesktop.Services;

public static class WindowsServiceHelper
{
    private const string ServiceName = "SplateVPNService";

    /// <summary>
    /// Installs and configures Xray core as a background Windows Service.
    /// </summary>
    public static bool InstallService(string xrayPath, string configPath)
    {
        try
        {
            UninstallService();

            var binPath = $"\"{xrayPath}\" run -c \"{configPath}\"";
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"create {ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"SplateVPN Core Service\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to install service: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts the background Windows Service.
    /// </summary>
    public static bool StartService()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"start {ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the background Windows Service.
    /// </summary>
    public static void StopService()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"stop {ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
        }
        catch { }
    }

    /// <summary>
    /// Uninstalls the background Windows Service.
    /// </summary>
    public static bool UninstallService()
    {
        try
        {
            StopService();
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete {ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch 
        { 
            return false;
        }
    }
}
