namespace SplateClientDesktop.Services;

public interface IConnectionPlatform
{
    bool IsTunRunning { get; }
    bool IsAdministrator { get; }
    event Action<int>? OnTunExited;
    Task<NetworkUplink> GetUplinkAsync(CancellationToken token);
    Task<bool> StartTunAsync(int socksPort, NetworkUplink uplink, bool blockIpv6, Action<string> log, CancellationToken token);
    void StopTun(Action<string> log);
    void EnableProxy(int httpPort, int socksPort);
    void DisableProxy();
}

public sealed class WindowsConnectionPlatform : IConnectionPlatform
{
    private readonly TunDeviceManager _tun = new();
    public bool IsTunRunning => _tun.IsRunning;
    public bool IsAdministrator => TunDeviceManager.IsAdministrator();
    public event Action<int>? OnTunExited { add => _tun.OnProcessExited += value; remove => _tun.OnProcessExited -= value; }
    public Task<NetworkUplink> GetUplinkAsync(CancellationToken token) => TunDeviceManager.GetUplinkAsync(token);
    public Task<bool> StartTunAsync(int port, NetworkUplink uplink, bool blockIpv6, Action<string> log, CancellationToken token) => _tun.StartTunAsync(port, uplink, blockIpv6, log, token);
    public void StopTun(Action<string> log) => _tun.StopTun(log);
    public void EnableProxy(int httpPort, int socksPort) => SystemProxyManager.EnableProxy(httpPort, socksPort);
    public void DisableProxy() => SystemProxyManager.DisableProxy();
}

public interface IConnectionProbes
{
    async Task<System.Net.IPAddress[]> ResolveServerAsync(string host, CancellationToken token)
    {
        var route = await ProbeRoute.CaptureAsync(token);
        var addresses = await route.ResolveAsync(host, token);
        token.ThrowIfCancellationRequested();
        route.Validate();
        return addresses;
    }
    Task<long> CurrentAsync(int httpPort, int timeoutMs, CancellationToken token);
    Task CandidatesAsync(IReadOnlyList<Models.ServerNode> nodes, Action<Models.ServerNode, long> result, CancellationToken token);
    bool NetworkAvailable { get; }
}

public sealed class ConnectionProbes : IConnectionProbes
{
    public bool NetworkAvailable => System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
    public Task<long> CurrentAsync(int port, int budget, CancellationToken token) => PingService.MeasureHttpProxyLatencyAsync(port, budget, token);
    public Task CandidatesAsync(IReadOnlyList<Models.ServerNode> nodes, Action<Models.ServerNode, long> result, CancellationToken token) =>
        PingService.MeasureRealPingBatchAsync(nodes, System.IO.Path.Combine(AppContext.BaseDirectory, "core", "xray.exe"), timeoutMs: 3000, onSingleNodePinged: result, ct: token);
}
