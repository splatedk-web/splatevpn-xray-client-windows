using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public class XrayProcessManager : IXrayProcessManager
{
    private Process? _process;
    private readonly string _corePath;
    private readonly string _configPath;

    public event Action<string>? OnLogMessage;
    public event Action<int>? OnProcessExited;
    public bool IsRunning => _process != null && !_process.HasExited;

    public XrayProcessManager(string baseDir)
    {
        _corePath = Path.Combine(baseDir, "core", "xray.exe");
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SplateVPN"
        );
        Directory.CreateDirectory(appData);
        _configPath = Path.Combine(appData, "config.json");
    }

    public async Task<bool> StartAsync(string configJson, int httpPort = 10809, int socksPort = 10808)
    {
        Stop();
        EnsurePortsAvailable(httpPort, socksPort);
        await Task.Delay(150);

        await File.WriteAllTextAsync(_configPath, configJson);

        if (!File.Exists(_corePath))
        {
            OnLogMessage?.Invoke($"[ERROR] Xray binary not found at {_corePath}");
            return false;
        }

        var coreDir = Path.GetDirectoryName(_corePath) ?? "";
        var psi = new ProcessStartInfo
        {
            FileName = _corePath,
            Arguments = $"run -c \"{_configPath}\"",
            WorkingDirectory = coreDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (!string.IsNullOrEmpty(coreDir))
        {
            psi.Environment["XRAY_LOCATION_ASSET"] = coreDir;
        }

        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnLogMessage?.Invoke(e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnLogMessage?.Invoke(e.Data);
            };
            _process.Exited += (_, _) =>
            {
                var exitCode = -1;
                try { exitCode = _process?.ExitCode ?? -1; } catch { }
                OnProcessExited?.Invoke(exitCode);
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            OnLogMessage?.Invoke($"[CORE] Xray core started (PID: {_process.Id})");

            // Wait for port readiness (up to 4 seconds)
            for (int i = 0; i < 40; i++)
            {
                if (_process == null || _process.HasExited)
                {
                    OnLogMessage?.Invoke($"[ERROR] Xray exited prematurely with code {_process?.ExitCode}");
                    return false;
                }

                if (IsPortListening(httpPort) && IsPortListening(socksPort))
                {
                    await Task.Delay(50);
                    if (_process != null && !_process.HasExited)
                    {
                        OnLogMessage?.Invoke($"[CORE] Core listening on HTTP:{httpPort} SOCKS:{socksPort}");
                        return true;
                    }
                    return false;
                }
                await Task.Delay(100);
            }

            if (_process != null && !_process.HasExited)
            {
                OnLogMessage?.Invoke($"[CORE] Timeout waiting for ports, but process is alive. Ready.");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"[ERROR] Failed to start Xray: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.EnableRaisingEvents = false;
                _process.Kill(true);
                _process.WaitForExit(1000);
                OnLogMessage?.Invoke("[CORE] Xray core stopped");
            }
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
        }

        KillOrphanedXrayProcesses();
    }

    private static void EnsurePortsAvailable(int httpPort, int socksPort)
    {
        KillOrphanedXrayProcesses();
        FreePortIfOccupied(httpPort);
        FreePortIfOccupied(socksPort);
    }

    private static void FreePortIfOccupied(int port)
    {
        if (!IsPortListening(port)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano -p tcp | findstr \":{port}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(500);
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 4 && int.TryParse(parts[^1], out var pid) && pid > 0 && pid != Environment.ProcessId)
                        {
                            try
                            {
                                var target = Process.GetProcessById(pid);
                                target.Kill(true);
                                target.WaitForExit(500);
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void KillOrphanedXrayProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("xray"))
            {
                try
                {
                    proc.Kill(true);
                    proc.WaitForExit(500);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            using var tcp = new TcpClient();
            var ar = tcp.BeginConnect("127.0.0.1", port, null, null);
            var success = ar.AsyncWaitHandle.WaitOne(100);
            if (success)
            {
                tcp.EndConnect(ar);
                return true;
            }
        }
        catch { }
        return false;
    }
}