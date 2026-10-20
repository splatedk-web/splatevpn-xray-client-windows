using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace SplateClientDesktop.Services;

internal sealed record ProbeRoute(NetworkUplink? Uplink, bool HadTun)
{
    private static bool TunPresent() => NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name == TunDeviceManager.DeviceName && n.OperationalStatus == OperationalStatus.Up);
    public static async Task<ProbeRoute> CaptureAsync(CancellationToken token)
    {
        var tun = TunPresent();
        var route = new ProbeRoute(tun ? await TunDeviceManager.GetUplinkAsync(token).ConfigureAwait(false) : null, tun);
        route.Validate(); return route;
    }
    public void Validate()
    {
        if (HadTun != TunPresent() || Uplink is { } uplink && !NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name == uplink.Name && n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().GetIPv4Properties()?.Index == uplink.Index))
            throw new OperationCanceledException("Сеть или режим TUN изменились во время проверки. Повторите проверку узлов.");
    }
    public void Bind(Socket socket)
    {
        if (Uplink == null) return;
        if (socket.AddressFamily == AddressFamily.InterNetwork)
        {
            socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(Uplink.Index));
            var nic = NetworkInterface.GetAllNetworkInterfaces().Single(n => n.Name == Uplink.Name);
            var address = nic.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)).Address;
            socket.Bind(new IPEndPoint(address, 0));
        }
        else
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces().Single(n => n.Name == Uplink.Name);
            socket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)31, nic.GetIPProperties().GetIPv6Properties().Index);
        }
    }
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken token)
    {
        if (IPAddress.TryParse(host, out var ip)) return [ip];
        if (!HadTun || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
        // System DNS is intercepted by TUN. Bootstrap this lookup with a literal
        // HTTPS address and a socket bound to the physical adapter instead.
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3), ConnectCallback = async (context, ct) =>
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try { Bind(socket); await socket.ConnectAsync(IPAddress.Parse(context.DnsEndPoint.Host == "dns.google" ? "8.8.8.8" : "1.1.1.1"), context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        } };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4), MaxResponseContentBufferSize = 65535 };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = new List<Task<IPAddress[]>> {
            Query("https://cloudflare-dns.com/dns-query?name="), Query("https://dns.google/resolve?name=") };
        try
        {
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var done = await Task.WhenAny(pending).ConfigureAwait(false); pending.Remove(done);
                var addresses = await done.ConfigureAwait(false);
                if (addresses.Length > 0) return addresses;
            }
            return [];
        }
        finally { lifetime.Cancel(); }
        async Task<IPAddress[]> Query(string endpoint)
        {
            try
            {
                using var response = await client.GetAsync(endpoint + Uri.EscapeDataString(host) + "&type=A", lifetime.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token).ConfigureAwait(false));
                if (document.RootElement.GetProperty("Status").GetInt32() != 0 || !document.RootElement.TryGetProperty("Answer", out var answers)) return [];
                return answers.EnumerateArray().Where(a => a.GetProperty("type").GetInt32() == 1).Select(a => IPAddress.TryParse(a.GetProperty("data").GetString(), out var value) ? value : null).OfType<IPAddress>().ToArray();
            }
            catch(Exception e) when(e is HttpRequestException or OperationCanceledException or JsonException or IOException) { return []; }
        }
    }
}
