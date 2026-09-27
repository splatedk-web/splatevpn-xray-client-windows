using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public class TunDeviceManager
{
    private Process? _tunProcess;
    private string? _bypassedServerIp;
    private string? _originalGateway;
    private const string TunDeviceName = "SplateTUN";
    private const string TunIp = "172.19.0.1";
    private const string TunGateway = "172.19.0.2";
    private const string TunMask = "255.255.255.252";

    public bool IsRunning => _tunProcess != null && !_tunProcess.HasExited;

    public async Task<bool> StartTunAsync(int socksPort, string serverHost, Action<string>? log = null)
    {
        try
        {
            StopTun(log);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var tunExe = Path.Combine(baseDir, "core", "tun2socks.exe");
            var wintunDll = Path.Combine(baseDir, "core", "wintun.dll");

            if (!File.Exists(tunExe) || !File.Exists(wintunDll))
            {
                log?.Invoke("[TUN ERROR] Файлы tun2socks.exe или wintun.dll не найдены в core/.");
                return false;
            }

            // Resolve server IP to avoid routing loops
            _originalGateway = GetDefaultGateway();
            _bypassedServerIp = ResolveIp(serverHost);

            log?.Invoke($"[TUN] Запуск Wintun адаптера '{TunDeviceName}' через SOCKS5 :{socksPort}...");

            var psi = new ProcessStartInfo
            {
                FileName = tunExe,
                Arguments = $"-device wintun://{TunDeviceName} -proxy socks5://127.0.0.1:{socksPort} -loglevel warn",
                WorkingDirectory = Path.Combine(baseDir, "core"),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _tunProcess = Process.Start(psi);
            if (_tunProcess == null || _tunProcess.HasExited)
            {
                log?.Invoke("[TUN ERROR] Не удалось запустить процесс tun2socks.exe");
                return false;
            }

            // Allow the virtual adapter to initialize in Windows
            await Task.Delay(1000);

            // Configure IP and Gateway on the virtual adapter
            RunCmd($"netsh interface ip set address name=\"{TunDeviceName}\" static {TunIp} {TunMask} {TunGateway}");
            RunCmd($"netsh interface ip set dns name=\"{TunDeviceName}\" static 1.1.1.1");

            // Bypass route for the remote VPN server host through physical gateway
            if (!string.IsNullOrEmpty(_bypassedServerIp) && !string.IsNullOrEmpty(_originalGateway))
            {
                RunCmd($"route add {_bypassedServerIp} mask 255.255.255.255 {_originalGateway} metric 1");
            }

            // Route all internet traffic through Wintun
            RunCmd($"route add 0.0.0.0 mask 128.0.0.0 {TunGateway} metric 5");
            RunCmd($"route add 128.0.0.0 mask 128.0.0.0 {TunGateway} metric 5");

            log?.Invoke("[TUN] Wintun адаптер успешно поднят! Весь сетевой трафик Windows направлен в туннель.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[TUN ERROR] Ошибка инициализации TUN: {ex.Message}");
            StopTun();
            return false;
        }
    }

    public void StopTun(Action<string>? log = null)
    {
        try
        {
            // Remove routes
            RunCmd($"route delete 0.0.0.0 mask 128.0.0.0 {TunGateway}");
            RunCmd($"route delete 128.0.0.0 mask 128.0.0.0 {TunGateway}");

            if (!string.IsNullOrEmpty(_bypassedServerIp))
            {
                RunCmd($"route delete {_bypassedServerIp} mask 255.255.255.255");
                _bypassedServerIp = null;
            }

            if (_tunProcess != null)
            {
                if (!_tunProcess.HasExited)
                {
                    _tunProcess.Kill(true);
                    _tunProcess.WaitForExit(1500);
                }
                _tunProcess.Dispose();
                _tunProcess = null;
                log?.Invoke("[TUN] Виртуальный адаптер TUN остановлен.");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[TUN ERROR] Ошибка остановки TUN: {ex.Message}");
        }
    }

    private static string? ResolveIp(string host)
    {
        if (IPAddress.TryParse(host, out _))
            return host;

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var addr in addresses)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                    return addr.ToString();
            }
        }
        catch { }
        return null;
    }

    private static string? GetDefaultGateway()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c route print 0.0.0.0",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[0] == "0.0.0.0" && parts[1] == "0.0.0.0")
                {
                    // parts[2] is Gateway
                    return parts[2];
                }
            }
        }
        catch { }
        return null;
    }

    private static void RunCmd(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
        }
        catch { }
    }
}
