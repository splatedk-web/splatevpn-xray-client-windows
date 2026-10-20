using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace SplateClientDesktop.Services;

public class XrayProcessManager : IXrayProcessManager
{
    private Process? _process;
    private ChildProcessJob? _job;
    private readonly string _corePath;
    private readonly string _configPath;
    private bool _isStopping;
    private int _httpPort, _socksPort;
    public event Action<string>? OnLogMessage;
    public event Action<int>? OnProcessExited;
    public bool IsRunning => _process is { HasExited: false };

    public XrayProcessManager(string baseDir)
    {
        _corePath = Path.Combine(baseDir, "core", "xray.exe");
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplateVPN");
        Directory.CreateDirectory(appData);
        _configPath = Path.Combine(appData, $"config-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
    }

    public async Task<bool> StartAsync(string configJson, int httpPort = 10809, int socksPort = 10808, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_corePath)) { OnLogMessage?.Invoke("[ERROR] В папке core отсутствует xray.exe."); return false; }
        if (httpPort == socksPort || httpPort is < 1 or > 65535 || socksPort is < 1 or > 65535)
            throw new ArgumentException("HTTP и SOCKS должны использовать разные порты от 1 до 65535.");
        // Never terminate cores owned by Karing, another client or a latency probe.
        if (!await ValidateAsync(configJson, ct)) return false;
        var checks = await Task.WhenAll(
            IsRunning && (httpPort == _httpPort || httpPort == _socksPort) ? Task.FromResult(false) : IsPortListeningAsync(httpPort),
            IsRunning && (socksPort == _httpPort || socksPort == _socksPort) ? Task.FromResult(false) : IsPortListeningAsync(socksPort));
        if (checks.Any(b => b)) { OnLogMessage?.Invoke("[ERROR] Локальные порты заняты. Измените HTTP/SOCKS порты в настройках."); return false; }
        ct.ThrowIfCancellationRequested();
        Stop();
        await File.WriteAllTextAsync(_configPath, configJson, ct);
        _httpPort = httpPort; _socksPort = socksPort;
        _isStopping = false;
        var psi = CreateStartInfo();
        foreach (var arg in new[] { "run", "-c", _configPath }) psi.ArgumentList.Add(arg);
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process = proc;
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) OnLogMessage?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) OnLogMessage?.Invoke(e.Data); };
        proc.Exited += (_, _) =>
        {
            if (_isStopping || _process != proc) return;
            OnLogMessage?.Invoke($"[CORE] Xray завершился: {proc.ExitCode}");
            OnProcessExited?.Invoke(proc.ExitCode);
        };
        try
        {
            _job = new ChildProcessJob();
            proc.Start(); _job.Attach(proc); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            for (int i = 0; i < 80; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (proc.HasExited) { Stop(); return false; }
                var ready = await IsPortListeningAsync(socksPort);
                if (ready && !proc.HasExited)
                {
                    OnLogMessage?.Invoke($"[CORE] HTTP :{httpPort}, SOCKS :{socksPort} готовы.");
                    return true;
                }
                await Task.Delay(100, ct);
            }
            OnLogMessage?.Invoke("[ERROR] Ядро не открыло локальные порты за отведённое время.");
            Stop(); return false;
        }
        catch (OperationCanceledException) { Stop(); throw; }
        catch (Exception ex) { OnLogMessage?.Invoke("[ERROR] " + ex.Message); Stop(); return false; }
    }

    private async Task<bool> ValidateAsync(string config, CancellationToken token)
    {
        var path = _configPath + ".validate";
        try
        {
            await File.WriteAllTextAsync(path, config, token);
            var info = CreateStartInfo();
            foreach (var arg in new[] { "run", "-test", "-format", "json", "-c", path }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(token); var errors = process.StandardError.ReadToEndAsync(token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { try { process.Kill(true); } catch { } throw; }
            if (process.ExitCode == 0) return true;
            OnLogMessage?.Invoke("[CONFIG ERROR] " + await output + await errors); return false;
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var psi = new ProcessStartInfo(_corePath)
        {
            WorkingDirectory = Path.GetDirectoryName(_corePath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["XRAY_LOCATION_ASSET"] = AdFilterService.EnsureAssets(Path.GetDirectoryName(Path.GetDirectoryName(_corePath))!);
        return psi;
    }

    public void Stop()
    {
        _isStopping = true;
        var proc = _process; _process = null;
        if (proc != null)
        {
            try { proc.EnableRaisingEvents = false; if (!proc.HasExited) { proc.Kill(true); proc.WaitForExit(3000); } }
            catch (Exception ex) { OnLogMessage?.Invoke("[CORE CLEANUP] " + ex.Message); }
            finally { proc.Dispose(); }
        }
        _job?.Dispose(); _job = null;
        try { File.Delete(_configPath); } catch { }
    }

    private static async Task<bool> IsPortListeningAsync(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = new CancellationTokenSource(200);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), timeout.Token);
            return true;
        }
        catch { return false; }
    }
}
