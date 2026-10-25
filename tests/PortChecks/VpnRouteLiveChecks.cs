using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class VpnRouteLiveChecks
{
    public static Task RunAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.BeginInvoke(async () =>
            {
                try { await CheckAsync(); done.SetResult(); }
                catch(Exception error) { done.SetException(error); }
                finally { app.Shutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground=true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }
    private static async Task CheckAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SplateVPN");
        var nodes = JsonSerializer.Deserialize<List<ServerNode>>(await File.ReadAllTextAsync(Path.Combine(data,"servers.json")))!;
        var settings = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(Path.Combine(data,"settings.json")))!;
        var node = nodes.FirstOrDefault(n=>n.IsSelected) ?? throw new Exception("No selected server stored");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var route = await ProbeRoute.CaptureAsync(timeout.Token);
        var http = Port(); var socks = Port();
        var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node, mode:"global", enableAiBypass:true,
            enableAiDns:false, aiRoutingMode:"vpn", uplinkInterface:route.Uplink?.Name, socksPort:socks, httpPort:http,
            enableSniffing:true, enableTlsFragment:settings.EnableTlsFragment, xhttpModeOverride:settings.XhttpModeOverride,
            enableMux:settings.EnableMux, muxConcurrency:settings.MuxConcurrency,
            tlsFragmentLength:settings.TlsFragmentLength, tlsFragmentInterval:settings.TlsFragmentInterval, tlsFragmentPackets:settings.TlsFragmentPackets))!;
        if(!IPAddress.TryParse(node.Address,out _))
        {
            var addresses = await route.ResolveAsync(node.Address,timeout.Token);
            var ips = new JsonArray(addresses.Where(a=>a.AddressFamily==AddressFamily.InterNetwork).Select(a=>JsonValue.Create(a.ToString())).ToArray());
            if(ips.Count==0) throw new Exception("No IPv4 address for selected server");
            config["dns"]!["hosts"]![node.Address]=ips;
            if(XrayConfigGenerator.ServerAddressAlias(node) is {} alias) config["dns"]!["hosts"]![alias]=ips.DeepClone();
        }
        var core = new XrayProcessManager(root);
        try
        {
            if(!await core.StartAsync(config.ToJsonString(),http,socks,timeout.Token)) throw new Exception("Isolated VPN core did not start");
            Console.WriteLine("Isolated selected-server check: global VPN, no SmartDNS outbound; existing connection/settings untouched.");
            var geoTask = IpLocationService.FetchEgressIpAsync(socks,8000);
            var geminiTask = GeminiPageVerifier.CheckAsync(socks,timeout.Token);
            using var client = new HttpClient(new SocketsHttpHandler {UseProxy=true,Proxy=new WebProxy($"socks5://127.0.0.1:{socks}")}) {Timeout=TimeSpan.FromSeconds(12)};
            try
            {
                using var geo = JsonDocument.Parse(await client.GetStringAsync("https://ipinfo.io/json",timeout.Token));
                Console.WriteLine("ipinfo country="+(geo.RootElement.TryGetProperty("country",out var country)?country.GetString():"unknown"));
            }
            catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException) { Console.WriteLine("ipinfo unavailable: "+e.GetType().Name); }
            var location = await geoTask;
            Console.WriteLine($"VPN egress country={location.CountryCode}; location={location.Country}; confirmed={location.IsSuccess}");
            var gemini = await geminiTask;
            Console.WriteLine($"Gemini fresh rendered public page over selected VPN: {gemini.Outcome}; elapsed={gemini.ElapsedMs}ms; detail={gemini.Detail}");
        }
        finally { core.Stop(); }
    }
    private static int Port() { var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port; }
}
