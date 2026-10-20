using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace SplateClientDesktop.Services;

public record NetworkUplink(string Name, int Index, string Gateway);

/// <summary>Owns only the adapter, process and routes created by this client.</summary>
public class TunDeviceManager
{
    private Process? _tunProcess;
    private ChildProcessJob? _job;
    private readonly List<(string Family, string Prefix)> _routes = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _stopping;
    public const string DeviceName = "SplateTUN";
    public bool IsRunning => _tunProcess is { HasExited: false };
    public event Action<int>? OnProcessExited;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static async Task<NetworkUplink> GetUplinkAsync(CancellationToken ct = default)
    {
        // Only ASCII numbers cross the Windows PowerShell pipe. Resolve the Unicode
        // adapter name in-process: OEM/UTF-8 conversion used to break Russian names.
        const string script = "Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -PolicyStore ActiveStore | Where-Object { $_.InterfaceAlias -ne 'SplateTUN' -and $_.NextHop -ne '0.0.0.0' } | Sort-Object @{Expression={$_.RouteMetric + (Get-NetIPInterface -AddressFamily IPv4 -InterfaceIndex $_.InterfaceIndex).InterfaceMetric}} | Select-Object -First 1 @{n='Index';e={$_.InterfaceIndex}},@{n='Gateway';e={$_.NextHop}} | ConvertTo-Json -Compress";
        var json = await RunAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }, ct);
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "null" : json.Trim());
        var route = document.RootElement;
        if (route.ValueKind != JsonValueKind.Object) throw new IOException("Не найден активный сетевой шлюз. Проверьте подключение к интернету.");
        return ResolveUplink(route.GetProperty("Index").GetInt32(), route.GetProperty("Gateway").GetString()!);
    }

    internal static NetworkUplink ResolveUplink(int index, string gateway)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up && n.Supports(NetworkInterfaceComponent.IPv4) &&
            n.GetIPProperties().GetIPv4Properties().Index == index);
        if (nic == null || nic.Name == DeviceName || !IPAddress.TryParse(gateway, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || address.Equals(IPAddress.Any))
            throw new IOException("Физический сетевой адаптер недоступен. Проверьте Wi-Fi или Ethernet и повторите подключение.");
        // Validate Windows interface binding before installing any capture routes.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(index)); // IP_UNICAST_IF
        return new(nic.Name, index, gateway);
    }

    public async Task<bool> StartTunAsync(int socksPort, NetworkUplink uplink, bool blockIpv6,
        Action<string>? log = null, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(log).ConfigureAwait(false);
            _stopping = false;
            if (!IsAdministrator())
                throw new InvalidOperationException("Для TUN запустите SplateVPN от имени администратора. Режим прокси доступен без повышения прав.");
            var coreDir = Path.Combine(AppContext.BaseDirectory, "core");
            var exe = Path.Combine(coreDir, "tun2socks.exe");
            if (!File.Exists(exe) || !File.Exists(Path.Combine(coreDir, "wintun.dll")))
                throw new FileNotFoundException("В папке core отсутствуют tun2socks.exe или wintun.dll.");
            var psi = BuildStartInfo(coreDir, DeviceName, socksPort);
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _tunProcess = proc;
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) log?.Invoke("[TUN] " + e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log?.Invoke("[TUN] " + e.Data); };
            proc.Exited += (_, _) => { if (!_stopping && _tunProcess == proc) { try { OnProcessExited?.Invoke(proc.ExitCode); } catch (InvalidOperationException) { } } };
            _job = new ChildProcessJob();
            proc.Start();
            _job.Attach(proc);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            var ready = false;
            for (int i = 0; i < 50; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (proc.HasExited) throw new IOException($"tun2socks завершился с кодом {proc.ExitCode}.");
                ready = NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name == DeviceName);
                if (ready) break;
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            if (!ready) throw new TimeoutException("Windows не создала адаптер SplateTUN.");
            await NetshAsync(ct, "interface", "ipv4", "set", "address", $"name={DeviceName}", "source=static", "address=172.19.0.1", "mask=255.255.255.252", "gateway=none").ConfigureAwait(false);
            await NetshAsync(ct, "interface", "ipv4", "set", "dnsservers", $"name={DeviceName}", "source=static", "address=1.1.1.1", "register=none", "validate=no").ConfigureAwait(false);
            await NetshAsync(ct, "interface", "ipv4", "set", "interface", $"interface={DeviceName}", "metric=1").ConfigureAwait(false);
            // Xray's outbound sockets are bound to uplink.Name before these routes are installed.
            // This also keeps smart-RU direct traffic out of the tunnel and prevents recursion.
            // Capture IPv6 as well: otherwise IPv6-capable desktop apps bypass an IPv4-only TUN.
            await NetshAsync(ct, "interface", "ipv6", "add", "address", DeviceName, "fdfe:dcba:9876::1/64", "store=active").ConfigureAwait(false);
            await WaitForAddressesAsync(DeviceName, "172.19.0.1", "fdfe:dcba:9876::1", ct).ConfigureAwait(false);
            await AddRouteAsync("ipv4", "0.0.0.0/1", ct).ConfigureAwait(false);
            await AddRouteAsync("ipv4", "128.0.0.0/1", ct).ConfigureAwait(false);
            await AddRouteAsync("ipv6", "::/1", ct).ConfigureAwait(false);
            await AddRouteAsync("ipv6", "8000::/1", ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (proc.HasExited) throw new IOException("TUN остановился во время настройки маршрутов.");
            log?.Invoke($"[TUN] IPv4/IPv6 → SplateTUN; выход Xray → {uplink.Name}. IPv6: {(blockIpv6 ? "блокируется ядром" : "через туннель")}.");
            return true;
        }
        catch (OperationCanceledException)
        {
            await StopCoreAsync(log).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke("[TUN ERROR] " + ex.Message);
            await StopCoreAsync(log).ConfigureAwait(false);
            return false;
        }
        finally { _lifecycle.Release(); }
    }

    internal static async Task WaitForAddressesAsync(string deviceName, string ipv4, string ipv6, CancellationToken ct = default)
    {
        for (var i = 0; i < 80; i++)
        {
            ct.ThrowIfCancellationRequested();
            var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == deviceName);
            if (nic?.OperationalStatus == OperationalStatus.Up)
            {
                var addresses = nic.GetIPProperties().UnicastAddresses;
                bool Ready(string ip) => addresses.Any(a => a.Address.ToString() == ip && a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred);
                if (Ready(ipv4) && Ready(ipv6)) return;
                if (addresses.Any(a => (a.Address.ToString() == ipv4 || a.Address.ToString() == ipv6) && a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Duplicate))
                    throw new IOException("Адрес TUN конфликтует с другим устройством.");
            }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("Windows не завершила настройку IPv4/IPv6 адресов TUN.");
    }

    internal static ProcessStartInfo BuildStartInfo(string coreDir, string deviceName, int socksPort)
    {
        if (socksPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(socksPort));
        var psi = new ProcessStartInfo(Path.Combine(coreDir, "tun2socks.exe"))
        {
            WorkingDirectory = coreDir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        // "tun" is the device driver; Wintun is its Windows implementation.
        foreach (var arg in new[] { "--device", $"tun://{deviceName}", "--proxy", $"socks5://127.0.0.1:{socksPort}",
            "--mtu", "1500", "--tcp-auto-tuning", "--loglevel", "warn" }) psi.ArgumentList.Add(arg);
        return psi;
    }

    private async Task AddRouteAsync(string family, string prefix, CancellationToken ct)
    {
        await NetshAsync(ct, "interface", family, "add", "route", $"prefix={prefix}", $"interface={DeviceName}", family == "ipv4" ? "nexthop=0.0.0.0" : "nexthop=::", "metric=1", "store=active").ConfigureAwait(false);
        _routes.Add((family, prefix));
    }

    public void StopTun(Action<string>? log = null) => StopTunAsync(log).GetAwaiter().GetResult();

    public async Task StopTunAsync(Action<string>? log = null)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync(log).ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync(Action<string>? log)
    {
        _stopping = true;
        foreach (var route in _routes.AsEnumerable().Reverse().ToArray())
        {
            try
            {
                await NetshAsync(CancellationToken.None, "interface", route.Family, "delete", "route", $"prefix={route.Prefix}", $"interface={DeviceName}", "store=active").ConfigureAwait(false);
                _routes.Remove(route);
            }
            catch (Exception ex) { log?.Invoke("[TUN CLEANUP] " + ex.Message); }
        }
        var proc = _tunProcess;
        _tunProcess = null;
        if (proc == null) { _job?.Dispose(); _job = null; return; }
        try { if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync().ConfigureAwait(false); } }
        catch (Exception ex) { log?.Invoke("[TUN CLEANUP] " + ex.Message); }
        finally { proc.Dispose(); _job?.Dispose(); _job = null; }
        log?.Invoke("[TUN] Адаптер остановлен, маршруты освобождены.");
    }

    private static Task<string> NetshAsync(CancellationToken ct, params string[] args) => RunAsync("netsh.exe", args, ct);

    internal static async Task<string> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi) ?? throw new IOException($"Не удалось запустить {exe}.");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch { try { if (!proc.HasExited) proc.Kill(true); } catch { } throw; }
        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        if (proc.ExitCode != 0) throw new IOException($"{exe} ({proc.ExitCode}): {output.Trim()} {error.Trim()}");
        return output;
    }
}
